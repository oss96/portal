---
name: release
description: Cut a Portal release - bump the version, build portal.exe locally, commit, tag, push, and publish the release with the binary on the Forgejo instance. Use when the user asks to release, cut a release, publish a version, or ship a build.
---

# Release Portal

Portal has no CI. Releases are built on this machine and published to
`git.ossalali.com/oss/Portal` through the Forgejo API by `release.sh`, which
sits next to this file. It reads the token from the git credential store at
runtime and never prints it. Do not read the token yourself.

## 1. Pick the version

1. `git fetch origin --tags`, then read `git log <last-tag>..origin/main --oneline`.
2. Choose the bump: major for behaviour or file-format breaks, minor for new
   features, patch for fixes and dependency updates. Ask the user when unclear.
3. Tags are bare `X.Y.Z`, no `v` prefix.

## 2. Dry run

```
bash .claude/skills/release/release.sh X.Y.Z --dry-run
```

Prints the plan and the generated changelog and changes nothing. Show the plan
to the user if anything in it is surprising.

The script refuses to continue unless: the current branch is `main`, the
working tree is clean, local `main` equals `origin/main`, and the tag does not
exist locally or on origin. If `main` has unpushed commits, ask the user before
pushing them. The release request itself authorises pushing `main`,
`release/X.Y.Z` and the tag.

## 3. Release

```
bash .claude/skills/release/release.sh X.Y.Z
```

In order: sets the `[package]` version in `Cargo.toml`, runs
`cargo build --release`, checks the exe's embedded file version, commits
`Release X.Y.Z` on `main`, creates the `release/X.Y.Z` branch and the annotated
tag at that commit, pushes all three, deletes the previous release entry on
Forgejo (its tag stays), creates the new release with a changelog from
`git log`, and uploads `target/release/portal.exe`.

Report the printed release URL to the user.

## If it fails

- **Before the push** (build or version check): fix the cause, reset with
  `git reset --hard origin/main`, and rerun.
- **After the push** (Forgejo API): the tag and commit are already on origin.
  Rerun with `--publish-only` to publish from the existing tag and the exe
  already in `target/release`:

  ```
  bash .claude/skills/release/release.sh X.Y.Z --publish-only
  ```

## What the in-app updater expects

`src/update.rs` reads `/releases/latest` from this repository, so a release is only
picked up when the tag is bare `X.Y.Z`, the release is neither a draft nor a
prerelease, and the attached binary is named exactly `portal.exe`. The script
already satisfies all three; keep it that way when changing it. The repository is
public so the updater needs no credentials.

## Conventions

- One release entry exists at a time. Older entries are deleted, tags accumulate.
- `release/X.Y.Z` marks the release point; `main` carries the bumped version.
- The binary is Windows only, so the release is built where it runs.
