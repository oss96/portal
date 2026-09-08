#!/usr/bin/env bash
# Cut a Portal release: bump Cargo.toml, build portal.exe, commit, branch,
# tag, push, then publish the release with the binary on Forgejo.
#
# Usage:
#   release.sh X.Y.Z                 full release
#   release.sh X.Y.Z --dry-run       validate and print the plan; change nothing
#   release.sh X.Y.Z --publish-only  skip the git steps and publish an already
#                                    pushed tag from target/release/portal.exe
#
# The Forgejo token is read from the git credential store at runtime and is
# never printed. Run from anywhere inside the repository.
set -euo pipefail

die()  { echo "release: $*" >&2; exit 1; }
step() { echo; echo "== $*"; }

VERSION="${1:-}"
MODE="${2:-}"
[[ -n "$VERSION" ]] || die "usage: release.sh X.Y.Z [--dry-run|--publish-only]"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "version '$VERSION' is not of the form X.Y.Z"
case "$MODE" in
  ""|--dry-run|--publish-only) ;;
  *) die "unknown option '$MODE'" ;;
esac

cd "$(git rev-parse --show-toplevel)"

APP_NAME="Portal"
EXE="target/release/portal.exe"
ASSET_NAME="portal.exe"

# Remote coordinates come from origin, e.g. https://git.ossalali.com/oss/Portal.git
ORIGIN_URL="$(git remote get-url origin)"
[[ "$ORIGIN_URL" =~ ^https://([^/]+)/([^/]+)/([^/]+)$ ]] || die "origin must be an https URL, got '$ORIGIN_URL'"
HOST="${BASH_REMATCH[1]}"
OWNER="${BASH_REMATCH[2]}"
REPO="${BASH_REMATCH[3]%.git}"
API="https://$HOST/api/v1/repos/$OWNER/$REPO"

TOKEN="$(printf 'protocol=https\nhost=%s\n' "$HOST" | git credential fill | sed -n 's/^password=//p')"
[[ -n "$TOKEN" ]] || die "no stored git credential for $HOST"

# api METHOD PATH [extra curl args...]
api() {
  local method="$1" path="$2"
  shift 2
  curl -sS -f -X "$method" -H "Authorization: token $TOKEN" "$API$path" "$@"
}

step "Checking the working tree"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
[[ "$BRANCH" == "main" ]] || die "must be on main (currently on $BRANCH)"
[[ -z "$(git status --porcelain)" ]] || die "working tree is not clean"
git fetch -q origin --tags

if [[ "$MODE" == "--publish-only" ]]; then
  git rev-parse -q --verify "refs/tags/$VERSION^{commit}" >/dev/null || die "tag $VERSION does not exist locally"
  [[ -f "$EXE" ]] || die "$EXE not found; build it first"
  RANGE_END="$VERSION"
else
  [[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]] \
    || die "local main differs from origin/main; push or pull first so the release matches the remote"
  ! git rev-parse -q --verify "refs/tags/$VERSION" >/dev/null || die "tag $VERSION already exists locally"
  [[ -z "$(git ls-remote --tags origin "refs/tags/$VERSION")" ]] || die "tag $VERSION already exists on origin"
  RANGE_END="HEAD"
fi

# Highest existing X.Y.Z tag other than the one being released.
PREV_TAG="$(git tag --sort=-v:refname | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | grep -vx "$VERSION" | head -1 || true)"
if [[ -n "$PREV_TAG" ]]; then
  [[ "$(printf '%s\n%s\n' "$PREV_TAG" "$VERSION" | sort -V | tail -1)" == "$VERSION" ]] \
    || die "version $VERSION is not above the latest tag $PREV_TAG"
  RANGE="$PREV_TAG..$RANGE_END"
  BODY_HEAD="## Changes since $PREV_TAG"
else
  RANGE="$RANGE_END"
  BODY_HEAD="## Changes"
fi

CHANGES="$(git log --format='- %s (%h)' "$RANGE" | grep -v -- "^- Release $VERSION (" || true)"
[[ -n "$CHANGES" ]] || die "no commits since ${PREV_TAG:-the beginning}; nothing to release"
BODY="$BODY_HEAD"$'\n\n'"$CHANGES"

step "Checking Forgejo access"
api GET "" >/dev/null || die "cannot reach $API with the stored credential"
EXISTING="$(api GET "/releases?limit=50" \
  | python -c 'import sys, json; [print(r["id"], r["tag_name"]) for r in json.load(sys.stdin)]')"

step "Plan"
echo "Release:       $APP_NAME $VERSION"
echo "Previous tag:  ${PREV_TAG:-none}"
echo "Repository:    $OWNER/$REPO on $HOST"
echo "Mode:          ${MODE:-full release}"
if [[ -n "$EXISTING" ]]; then
  echo "Release entries to delete (their tags stay): $(awk '{print $2}' <<< "$EXISTING" | tr '\n' ' ')"
fi
echo
echo "$BODY"

if [[ "$MODE" == "--dry-run" ]]; then
  echo
  echo "Dry run: nothing changed."
  exit 0
fi

if [[ "$MODE" != "--publish-only" ]]; then
  step "Setting Cargo.toml version to $VERSION"
  sed -i "0,/^version = \".*\"/s//version = \"$VERSION\"/" Cargo.toml
  grep -q "^version = \"$VERSION\"" Cargo.toml || die "failed to set the version in Cargo.toml"

  step "Building the release binary"
  cargo build --release
  [[ -f "$EXE" ]] || die "$EXE not found after the build"

  # winresource stamps CARGO_PKG_VERSION into the exe; make sure we built the bumped tree.
  EXE_VERSION="$(powershell.exe -NoProfile -Command "(Get-Item '$EXE').VersionInfo.FileVersion" | tr -d '\r')"
  [[ "$EXE_VERSION" == "$VERSION" || "$EXE_VERSION" == "$VERSION.0" ]] \
    || die "built binary reports version '$EXE_VERSION', expected $VERSION"

  CHANGED="$(git status --porcelain | awk '{print $2}' | sort | tr '\n' ' ')"
  [[ "$CHANGED" == "Cargo.lock Cargo.toml " || "$CHANGED" == "Cargo.toml " ]] \
    || die "unexpected changes after the build: $CHANGED"

  step "Committing, branching and tagging"
  git add Cargo.toml Cargo.lock
  git commit -q -m "Release $VERSION"
  git branch "release/$VERSION"
  git tag -a "$VERSION" -m "$APP_NAME $VERSION"

  step "Pushing main, release/$VERSION and tag $VERSION"
  git push origin main "release/$VERSION" "refs/tags/$VERSION"
fi

step "Publishing on Forgejo"
while read -r id tag; do
  [[ -n "$id" ]] || continue
  echo "Deleting release entry $tag (tag kept)"
  api DELETE "/releases/$id" >/dev/null
done <<< "$EXISTING"

PAYLOAD="$(VERSION="$VERSION" NAME="$APP_NAME $VERSION" BODY="$BODY" python -c '
import json, os
print(json.dumps({
    "tag_name": os.environ["VERSION"],
    "target_commitish": os.environ["VERSION"],
    "name": os.environ["NAME"],
    "body": os.environ["BODY"],
    "draft": False,
    "prerelease": False,
}))')"
RESP="$(api POST "/releases" -H "Content-Type: application/json" --data-binary "$PAYLOAD")"
RELEASE_ID="$(printf '%s' "$RESP" | python -c 'import sys, json; print(json.load(sys.stdin)["id"])')"
HTML_URL="$(printf '%s' "$RESP" | python -c 'import sys, json; print(json.load(sys.stdin)["html_url"])')"

echo "Uploading $ASSET_NAME"
api POST "/releases/$RELEASE_ID/assets?name=$ASSET_NAME" -F "attachment=@$EXE" >/dev/null

echo
echo "Published $APP_NAME $VERSION: $HTML_URL"
