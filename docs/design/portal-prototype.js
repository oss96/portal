'use strict';

const G = {
  folder: '\uE8B7', file: '\uE8A5', code: '\uE943', video: '\uE714', photo: '\uE91B', pkg: '\uE7B8',
  back: '\uE72B', fwd: '\uE72A', up: '\uE74A', refresh: '\uE72C', newfolder: '\uE8F4', del: '\uE74D',
  rename: '\uE8AC', upload: '\uE898', download: '\uE896', copy: '\uE8C8', settings: '\uE713', search: '\uE721',
  lock: '\uE72E', pc: '\uE977', server: '\uE8CE', home: '\uE80F', info: '\uE946', warn: '\uE7BA',
  error: '\uE783', ok: '\uE73E', close: '\uE711', add: '\uE710', chevR: '\uE76C', chevD: '\uE70D', chevU: '\uE70E',
  pause: '\uE769', play: '\uE768', sync: '\uE895', eye: '\uE890', pane: '\uE8A0', term: '\uE756',
  key: '\uE8D7', power: '\uE7E8', star: '\uE734', open: '\uE8E5', min: '\uE921', max: '\uE922', x: '\uE8BB',
};
const NOW = new Date('2026-11-03T14:20:00');
const KB = 1024, MB = KB * 1024, GB = MB * 1024;

function D(name, children = [], o = {}) {
  return { name, type: 'dir', children, size: 0, mtime: o.m || '2026-10-28T11:04', perm: o.p || 'rwxr-xr-x', owner: o.o || 'root', group: o.g || 'root' };
}
function F(name, size, o = {}) {
  return { name, type: 'file', size: Math.round(size), mtime: o.m || '2026-10-30T16:45', perm: o.p || 'rw-r--r--', owner: o.o || 'root', group: o.g || 'root' };
}
const NB = { o: 'nobody', g: 'users', p: 'rwxrwxrwx' };
const NBF = { o: 'nobody', g: 'users', p: 'rw-rw-rw-' };
const nb = (m, extra = {}) => ({ ...NB, m, ...extra });
const nbf = (m, extra = {}) => ({ ...NBF, m, ...extra });

const towerFS = D('/', [
  D('boot', [D('config', [
    F('go', 1180, { m: '2026-09-14T20:02', p: 'rwxr-xr-x' }), F('ident.cfg', 912), F('disk.cfg', 2240),
    D('plugins', [F('tailscale.plg', 18400), F('user.scripts.plg', 9100)]),
  ])]),
  D('mnt', [
    D('user', [
      D('appdata', [
        D('jellyfin', [D('config', [], NB), D('cache', [], NB), F('log.txt', 3.2 * MB, nbf('2026-11-03T13:58'))], nb('2026-11-03T13:58')),
        D('swag', [D('nginx', [], NB), D('log', [], NB)], nb('2026-10-20T08:15')),
        D('forgejo', [D('git', [], NB), F('app.ini', 4100, nbf('2026-10-11T19:30'))], nb('2026-10-11T19:30')),
        D('kaneo', [D('data', [], NB)], nb('2026-10-09T12:40')),
        D('tailscale', [], nb('2026-10-28T10:00')),
      ], nb('2026-11-03T13:58')),
      D('media', [
        D('Movies', [
          D('Arrival (2016)', [F('Arrival (2016).mkv', 14.2 * GB, nbf('2026-03-02T22:10')), F('poster.jpg', 410 * KB, nbf('2026-03-02T22:10'))], nb('2026-03-02T22:10')),
          D('Blade Runner 2049 (2017)', [F('Blade Runner 2049 (2017).mkv', 22.8 * GB, nbf('2026-01-18T20:00'))], nb('2026-01-18T20:00')),
          D('Dune (2021)', [F('Dune (2021).mkv', 19.6 * GB, nbf('2025-12-24T17:41'))], nb('2025-12-24T17:41')),
          D('Past Lives (2023)', [F('Past Lives (2023).mkv', 8.4 * GB, nbf('2026-06-07T21:02'))], nb('2026-06-07T21:02')),
        ], nb('2026-06-07T21:02')),
        D('TV', [D('Severance', [D('Season 01', [], NB), D('Season 02', [], NB)], nb('2026-04-11T09:00')), D('The Bear', [], nb('2026-07-01T10:00'))], nb('2026-07-01T10:00')),
        D('Music', [], nb('2026-02-14T12:00')),
      ], nb('2026-07-01T10:00')),
      D('downloads', [
        D('complete', [F('archlinux-2026.11.01-x86_64.iso', 1.3 * GB, nbf('2026-11-01T08:12'))], nb('2026-11-01T08:12')),
        D('incomplete', [], nb('2026-11-02T23:59')),
        F('ubuntu-26.04-desktop-amd64.iso', 6.1 * GB, nbf('2026-10-24T14:33')),
        F('Dune Part Two (2024).mkv', 18.4 * GB, nbf('2026-10-02T21:13')),
        F('.DS_Store', 6 * KB, nbf('2026-09-01T10:00')),
      ], nb('2026-11-02T23:59')),
      D('backups', [
        D('offsite', [], { m: '2026-08-30T03:00' }),
        F('flash-2026-10-01.zip', 412 * MB, { m: '2026-10-01T03:00' }),
        F('appdata-2026-10-28.tar.zst', 9.8 * GB, { m: '2026-10-28T03:00' }),
      ], { m: '2026-10-28T03:00' }),
    ], { m: '2026-10-28T03:00' }),
    D('cache', [], { m: '2026-10-28T03:00' }), D('disk1', []), D('disk2', []),
  ]),
  D('root', [D('.ssh', [], { p: 'rwx------' }), F('.bash_history', 22 * KB, { p: 'rw-------', m: '2026-11-03T09:01' }), F('notes.txt', 2 * KB)], { p: 'rwx------' }),
]);

const hetznerFS = D('/', [
  D('srv', [D('docker', [
    F('compose.yaml', 6.2 * KB, { m: '2026-10-29T18:22' }), F('.env', 1.1 * KB, { p: 'rw-------', m: '2026-10-29T18:22' }),
    D('caddy', [F('Caddyfile', 3.4 * KB, { m: '2026-10-29T18:20' })]), D('kaneo', []), D('forgejo', []), D('authelia', []),
  ], { m: '2026-10-29T18:22' })]),
  D('root', [F('backup.sh', 2.1 * KB, { p: 'rwxr-xr-x' })], { p: 'rwx------' }),
  D('etc', []), D('var', [D('log', [])]),
]);

const localFS = D('This PC', [
  D('C:', [D('Users', [D('Oss', [
    D('Desktop', []),
    D('Downloads', [
      F('Dune Part Two (2024).mkv', 18.4 * GB, { m: '2026-11-03T09:12' }),
      F('Severance S02E01.mkv', 2.3 * GB, { m: '2026-11-02T21:40' }),
      F('poster.jpg', 380 * KB, { m: '2026-11-03T09:15' }),
      F('report-october.pdf', 1.4 * MB, { m: '2026-11-01T17:02' }),
      F('portal-0.5.0.exe', 14 * MB, { m: '2026-10-08T22:30' }),
      D('wallpapers', [F('dunes.jpg', 4.1 * MB), F('tower.png', 2.2 * MB)], { m: '2026-10-19T12:00' }),
    ], { m: '2026-11-03T09:15' }),
    D('Documents', [F('taxes-2025.pdf', 2.8 * MB), F('lease.pdf', 900 * KB)]),
    D('Pictures', [D('Screenshots', [])]),
    D('Projects', [D('Portal', []), D('server-config', [])]),
    D('Videos', []),
  ])])]),
  D('E:', [D('Worktrees', []), D('Media', [])]),
]);

const servers = [
  { id: 'tower', name: 'Tower', user: 'root', host: 'tower', via: 'Tailscale', latency: 14, known: true, last: 'Today, 09:12', fs: towerFS, home: ['mnt', 'user'],
    favs: [['Shares', ['mnt', 'user']], ['media', ['mnt', 'user', 'media']], ['downloads', ['mnt', 'user', 'downloads']], ['appdata', ['mnt', 'user', 'appdata']], ['backups', ['mnt', 'user', 'backups']], ['Flash config', ['boot', 'config']]],
    storage: [['Array', 86, '3.1 TB free'], ['Cache', 41, '560 GB free']] },
  { id: 'hetzner', name: 'Hetzner', user: 'root', host: 'server.ossalali.com', via: 'SSH config “hetzner”', latency: 31, known: false, last: '2 Oct', fs: hetznerFS, home: ['srv', 'docker'],
    favs: [['docker', ['srv', 'docker']], ['root', ['root']], ['logs', ['var', 'log']]],
    storage: [['Data volume', 38, '31 GB free']] },
];

const LAYOUTS = {
  A: ['Twin panes', 'This PC and the server side by side, one command bar for the focused pane, transfers docked at the bottom. Closest to today, without the clutter.'],
  B: ['Server first', 'The server is the main view. A sidebar holds servers, favourite folders and disk space; a details pane edits permissions. This PC opens on demand.'],
  C: ['Workspace', 'One tab per server, both panes visible, a details pane, and a command search (Ctrl+K) for everything.'],
};

const T = loadTweaks() || { layout: 'B', theme: 'dark', density: 'comfortable', accent: 'blue', min: false };
function loadTweaks() { try { return JSON.parse(localStorage.getItem('portal-proto-tweaks')); } catch { return null; } }
function saveTweaks() { try { localStorage.setItem('portal-proto-tweaks', JSON.stringify(T)); } catch { } }

function mkPane(id, fs, path) {
  return { id, fs, path, back: [], fwd: [], sel: new Set(), anchor: null, focus: null, sort: { k: 'name', d: 1 }, filter: '', editPath: false, loading: false };
}

const S = {
  screen: 'connect', connecting: null, form: { host: '', user: 'root', port: '22', auth: 'key' },
  server: null, tabs: [], remotePanes: {},
  panes: { local: mkPane('local', localFS, ['C:', 'Users', 'Oss', 'Downloads']), remote: null },
  active: 'remote', split: false, inspector: true, queueOpen: false, showHidden: false,
  queue: [], toasts: [], dialog: null, menu: null, palette: null, drag: null,
  renaming: null, renameValue: '', pathDraft: '', sizes: {},
  settings: { parallel: 3, conflict: 'ask', reconnect: true, updates: true },
};

const $ = (s, r = document) => r.querySelector(s);
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const srv = () => servers.find(s => s.id === S.server);
const later = (ms, fn) => setTimeout(() => { fn(); render(); }, ms);

function fmtSize(b) {
  if (b < KB) return `${b} B`;
  if (b < MB) return `${Math.round(b / KB)} KB`;
  if (b < GB) return `${(b / MB).toFixed(b < 10 * MB ? 1 : 0)} MB`;
  return `${(b / GB).toFixed(1)} GB`;
}
function fmtDate(iso) {
  const d = new Date(iso), hm = d.toTimeString().slice(0, 5);
  const day = x => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((day(NOW) - day(d)) / 864e5);
  if (diff === 0) return `Today, ${hm}`;
  if (diff === 1) return `Yesterday, ${hm}`;
  const mon = d.toLocaleString('en-GB', { month: 'short' });
  return d.getFullYear() === NOW.getFullYear() ? `${d.getDate()} ${mon}` : `${d.getDate()} ${mon} ${d.getFullYear()}`;
}
function fmtEta(s) {
  if (!isFinite(s)) return '';
  if (s < 60) return `${Math.max(1, Math.round(s))} s left`;
  if (s < 3600) return `${Math.round(s / 60)} min left`;
  return `${Math.floor(s / 3600)} h ${Math.round((s % 3600) / 60)} min left`;
}
function octal(p) { return [0, 3, 6].map(i => (p[i] === 'r' ? 4 : 0) + (p[i + 1] === 'w' ? 2 : 0) + (p[i + 2] === 'x' ? 1 : 0)).join(''); }
function permParts(p) {
  const part = s => [s[0] === 'r' && 'read', s[1] === 'w' && 'write', s[2] === 'x' && 'run'].filter(Boolean).join(', ') || 'nothing';
  return [['Owner', part(p.slice(0, 3))], ['Group', part(p.slice(3, 6))], ['Everyone', part(p.slice(6))]];
}
function describePerm(p) { return permParts(p).map(([w, c]) => `${w}: ${c}`).join(' · '); }
function iconFor(n) {
  if (n.type === 'dir') return ['folder', G.folder];
  const ext = n.name.split('.').pop().toLowerCase();
  if (['mkv', 'mp4'].includes(ext)) return ['file', G.video];
  if (['jpg', 'png'].includes(ext)) return ['file', G.photo];
  if (['zip', 'zst', 'iso', 'gz'].includes(ext)) return ['file', G.pkg];
  if (['sh', 'yaml', 'cfg', 'plg', 'ini', 'go', 'env'].includes(ext) || n.name === 'Caddyfile') return ['file', G.code];
  return ['file', G.file];
}
function kindOf(n) {
  if (n.type === 'dir') return `Folder · ${n.children.length} item${n.children.length === 1 ? '' : 's'}`;
  const ext = n.name.includes('.') ? n.name.split('.').pop().toUpperCase() : 'File';
  return `${ext} file · ${fmtSize(n.size)}`;
}
const walk = (fs, path) => path.reduce((n, seg) => n && n.children && n.children.find(c => c.name === seg), fs);
const dirSize = n => n.type === 'dir' ? n.children.reduce((a, c) => a + dirSize(c), 0) : n.size;
const countFiles = n => n.type === 'dir' ? n.children.reduce((a, c) => a + countFiles(c), 0) : 1;
const clone = (n, name) => ({ ...n, name: name ?? n.name, children: n.children ? n.children.map(c => clone(c)) : undefined });

function pathLabel(p) {
  if (p.id === 'local') return p.path.length ? `${p.path[0]}\\${p.path.slice(1).join('\\')}` : 'This PC';
  return '/' + p.path.join('/');
}
function shortPath(p) { return p.path.length ? p.path[p.path.length - 1] : (p.id === 'local' ? 'This PC' : srv().name); }
function listAll(p) {
  const n = walk(p.fs, p.path);
  if (!n) return { items: [], hidden: 0 };
  let xs = n.children.filter(c => S.showHidden || !c.name.startsWith('.'));
  const before = xs.length;
  if (p.filter) xs = xs.filter(c => c.name.toLowerCase().includes(p.filter.toLowerCase()));
  const k = p.sort.k, d = p.sort.d;
  const val = c => k === 'size' ? (c.type === 'dir' ? -1 : c.size) : k === 'mtime' ? c.mtime : k === 'perm' ? c.perm : k === 'owner' ? c.owner : c.name.toLowerCase();
  xs = [...xs].sort((a, b) => (a.type === b.type ? 0 : a.type === 'dir' ? -1 : 1) || (val(a) < val(b) ? -d : val(a) > val(b) ? d : 0) || a.name.localeCompare(b.name, undefined, { numeric: true }));
  return { items: xs, hidden: before - xs.length };
}
const selNodes = p => { const n = walk(p.fs, p.path); return n ? n.children.filter(c => p.sel.has(c.name)) : []; };
function panesShown() { return T.layout === 'B' && !S.split ? ['remote'] : ['local', 'remote']; }
function other(p) { return p.id === 'local' ? S.panes.remote : S.panes.local; }

function toast(kind, title, body, link) {
  const t = { id: Math.random().toString(36).slice(2), kind, title, body, link };
  S.toasts.push(t);
  if (S.toasts.length > 3) S.toasts.shift();
  if (kind !== 'err') setTimeout(() => { S.toasts = S.toasts.filter(x => x !== t); render(); }, 6000);
}

function navigate(p, path, push = true) {
  if (push) { p.back.push(p.path); p.fwd = []; }
  p.path = path; p.sel.clear(); p.focus = null; p.anchor = null; p.filter = ''; p.editPath = false; S.renaming = null;
  S.active = p.id;
  if (p.id === 'remote') { p.loading = true; later(260, () => { p.loading = false; }); }
}

/* Connection */
function connect(id) {
  const s = servers.find(x => x.id === id);
  if (S.tabs.includes(id)) return switchServer(id);
  S.screen = 'connect';
  S.connecting = { id, step: 0, error: null };
  const steps = s.unknown ? 1 : 4;
  const advance = () => {
    if (!S.connecting || S.connecting.id !== id) return;
    const c = S.connecting;
    if (s.unknown && c.step === 0) {
      c.error = `Couldn't find “${s.host}”. Check the address, or make sure this PC is on the same network or connected to Tailscale.`;
      render(); return;
    }
    if (c.step === 1 && !s.known) {
      S.dialog = { kind: 'hostkey', server: s }; render(); return;
    }
    c.step++;
    if (c.step >= steps) { finishConnect(s); render(); return; }
    render(); setTimeout(advance, 420);
  };
  render(); setTimeout(advance, 420);
}
function finishConnect(s) {
  S.connecting = null;
  if (!S.tabs.includes(s.id)) S.tabs.push(s.id);
  if (!S.remotePanes[s.id]) S.remotePanes[s.id] = mkPane('remote', s.fs, [...s.home]);
  s.last = 'Just now';
  switchServer(s.id);
}
function switchServer(id) {
  S.server = id; S.panes.remote = S.remotePanes[id]; S.screen = 'browser'; S.active = 'remote';
}
function disconnect(id = S.server) {
  S.tabs = S.tabs.filter(t => t !== id);
  delete S.remotePanes[id];
  if (S.tabs.length) switchServer(S.tabs[S.tabs.length - 1]);
  else { S.server = null; S.panes.remote = null; S.screen = 'connect'; }
}

/* Transfers */
let batchSeq = 0;
async function transfer(from, names, to) {
  if (!to) return;
  if (!S.server) return;
  const srcDir = walk(from.fs, from.path), dstDir = walk(to.fs, to.path);
  const srcs = srcDir.children.filter(c => names.includes(c.name));
  if (!srcs.length) return;
  const up = from.id === 'local';
  const batch = { id: ++batchSeq, left: 0, total: 0, up, dest: [...to.path], server: S.server, toPane: to };
  let applyAll = S.settings.conflict === 'ask' ? null : S.settings.conflict;
  const conflicts = srcs.filter(s => dstDir.children.some(c => c.name === s.name)).length;
  let seen = 0;
  for (const s of srcs) {
    const existing = dstDir.children.find(c => c.name === s.name);
    let name = s.name, replace = false;
    if (existing) {
      seen++;
      let choice = applyAll;
      if (!choice) {
        const r = await askConflict(s, existing, up, conflicts - seen);
        choice = r.choice; if (r.all) applyAll = choice;
      }
      if (choice === 'cancel') break;
      if (choice === 'skip') continue;
      if (choice === 'keep') name = uniqueName(dstDir, s.name);
      if (choice === 'replace') replace = true;
    }
    const size = Math.max(dirSize(s), 1);
    const rate = (up ? 44 : 98) * MB * (s.type === 'dir' ? 0.7 : 1) * (0.9 + Math.random() * 0.2);
    S.queue.push({ id: Math.random().toString(36).slice(2), node: s, name, replace, up, size, done: 0, rate, status: 'queued',
      files: countFiles(s), fromLabel: pathLabel(from), toLabel: pathLabel(to), dstDir, batch, server: srv().name });
    batch.left++; batch.total++;
  }
  if (!batch.total) { render(); return; }
  if (T.layout === 'A') S.queueOpen = true;
  else toast('info', `${up ? 'Uploading' : 'Downloading'} ${batch.total} item${batch.total > 1 ? 's' : ''}`, `${up ? 'To' : 'From'} ${srv().name} · ${pathLabel(to)}`, { label: 'Show transfers', act: 'queue' });
  render();
}
function askConflict(src, dst, up, remaining) {
  return new Promise(resolve => { S.dialog = { kind: 'conflict', src, dst, up, remaining, all: false, resolve }; render(); });
}
function uniqueName(dir, name) {
  const dot = name.lastIndexOf('.');
  const base = dot > 0 ? name.slice(0, dot) : name, ext = dot > 0 ? name.slice(dot) : '';
  for (let i = 1; ; i++) { const n = `${base} (${i})${ext}`; if (!dir.children.some(c => c.name === n)) return n; }
}
function tick() {
  const max = S.settings.parallel;
  const running = S.queue.filter(q => q.status === 'running');
  for (const q of S.queue) { if (running.length >= max) break; if (q.status === 'queued') { q.status = 'running'; running.push(q); } }
  if (!running.length) return false;
  for (const q of running) {
    q.done = Math.min(q.size, q.done + q.rate * 0.25);
    if (q.size > 4 * GB && !q.failedOnce && q.done > q.size * 0.42) {
      q.status = 'failed'; q.failedOnce = true; q.error = `Connection to ${q.server} dropped`;
      toast('err', 'A transfer was interrupted', `${q.name} stopped at ${Math.round(q.done / q.size * 100)} %. Retry continues from there.`, { label: 'Retry', act: 'q-retry', arg: q.id });
      continue;
    }
    if (q.done >= q.size) finishItem(q);
  }
  return true;
}
function finishItem(q) {
  q.status = 'done';
  const dir = q.dstDir;
  if (q.replace) dir.children = dir.children.filter(c => c.name !== q.name);
  const n = clone(q.node, q.name);
  n.mtime = NOW.toISOString().slice(0, 16);
  if (q.up) { const fix = x => { x.owner = 'root'; x.group = 'root'; x.perm = x.type === 'dir' ? 'rwxr-xr-x' : (q.node.perm || 'rw-r--r--'); (x.children || []).forEach(fix); }; fix(n); }
  dir.children.push(n);
  q.batch.left--;
  if (q.batch.left === 0) {
    const b = q.batch;
    const where = b.up ? `${b.toPane === S.panes.remote ? srv().name : 'the server'} · /${b.dest.join('/')}` : `This PC · ${b.dest[0]}\\${b.dest.slice(1).join('\\')}`;
    toast('ok', `${b.up ? 'Uploaded' : 'Downloaded'} ${b.total} item${b.total > 1 ? 's' : ''}`, where, { label: 'Open folder', act: 'q-show', arg: q.id });
  }
}

/* File operations */
function startRename(p, name) { S.active = p.id; S.renaming = { pane: p.id, name }; S.renameValue = name; render(); }
function commitRename() {
  const r = S.renaming; if (!r) return;
  const p = S.panes[r.pane], dir = walk(p.fs, p.path), v = S.renameValue.trim();
  S.renaming = null;
  if (!v || v === r.name) return render();
  if (/[\\/]/.test(v)) { toast('err', 'That name isn’t allowed', 'Names can’t contain / or \\.'); return render(); }
  if (dir.children.some(c => c.name === v)) { toast('err', `“${v}” already exists`, 'Pick another name.'); return render(); }
  dir.children.find(c => c.name === r.name).name = v;
  p.sel = new Set([v]); p.focus = v;
  render();
}
function newFolder(p) {
  const dir = walk(p.fs, p.path), name = uniqueName({ children: [...dir.children, { name: 'New folder' }] }, 'New folder').replace(' (1)', dir.children.some(c => c.name === 'New folder') ? ' (1)' : '');
  const finalName = dir.children.some(c => c.name === name) ? uniqueName(dir, 'New folder') : name;
  const d = D(finalName, [], { m: NOW.toISOString().slice(0, 16) });
  dir.children.push(d);
  p.sel = new Set([finalName]); p.focus = finalName;
  startRename(p, finalName);
}
function doDelete(p, names) {
  const dir = walk(p.fs, p.path);
  const removed = dir.children.filter(c => names.includes(c.name));
  dir.children = dir.children.filter(c => !names.includes(c.name));
  p.sel.clear();
  if (p.id === 'local') {
    S.lastBin = { dir, removed, p };
    toast('ok', `Moved ${removed.length} item${removed.length > 1 ? 's' : ''} to the Recycle Bin`, pathLabel(p), { label: 'Undo', act: 'undo-delete' });
  } else toast('ok', `Deleted ${removed.length} item${removed.length > 1 ? 's' : ''} from ${srv().name}`, pathLabel(p));
}
function applyPerm() {
  const d = S.dialog, p = S.panes.remote, nodes = selNodes(p);
  const bits = d.bits.map(b => b ? 1 : 0), perm = 'rwxrwxrwx'.split('').map((ch, i) => bits[i] ? ch : '-').join('');
  const set = (n, deep) => { n.perm = n.type === 'dir' && !d.recursive ? perm : (n.type === 'dir' ? perm : perm); n.owner = d.owner; n.group = d.group; if (deep && n.children) n.children.forEach(c => set(c, true)); };
  nodes.forEach(n => set(n, d.recursive));
  S.dialog = null;
  toast('ok', `Permissions updated on ${nodes.length} item${nodes.length > 1 ? 's' : ''}`, `${octal(perm)} · ${d.owner}:${d.group}${d.recursive ? ' · including everything inside' : ''}`);
}

/* Commands shared by command bar, menus and palette */
function cmds() {
  const p = S.panes[S.active], sel = p ? selNodes(p) : [], remote = p && p.id === 'remote';
  const list = [
    { id: 'xfer', label: remote ? 'Download to This PC' : `Upload to ${srv()?.name || 'server'}`, icon: remote ? G.download : G.upload, kbd: 'Ctrl+Enter', on: sel.length > 0 },
    { id: 'newfolder', label: 'New folder', icon: G.newfolder, kbd: 'Ctrl+Shift+N', on: !!p },
    { id: 'rename', label: 'Rename', icon: G.rename, kbd: 'F2', on: sel.length === 1 },
    { id: 'perm', label: 'Permissions and owner…', icon: G.lock, on: remote && sel.length > 0 },
    { id: 'delete', label: remote ? 'Delete…' : 'Delete', icon: G.del, kbd: 'Del', on: sel.length > 0, danger: true },
    { id: 'refresh', label: 'Refresh', icon: G.refresh, kbd: 'F5', on: !!p },
    { id: 'hidden', label: S.showHidden ? 'Hide hidden files' : 'Show hidden files', icon: G.eye, kbd: 'Ctrl+H', on: true },
    { id: 'goto', label: 'Go to folder…', icon: G.chevR, kbd: 'Ctrl+L', on: !!p },
    { id: 'term', label: 'Open terminal here', icon: G.term, on: remote },
    { id: 'copypath', label: 'Copy path', icon: G.copy, on: !!p },
    { id: 'theme', label: `Switch to ${T.theme === 'dark' ? 'light' : 'dark'} theme`, icon: G.settings, on: true },
    { id: 'settings', label: 'Settings', icon: G.settings, kbd: 'Ctrl+,', on: true },
    { id: 'disconnect', label: `Disconnect from ${srv()?.name || ''}`, icon: G.power, on: !!S.server },
  ];
  for (const s of servers) if (s.id !== S.server) list.push({ id: 'server:' + s.id, label: `Connect to ${s.name}`, icon: G.server, on: true });
  return list;
}
function run(id) {
  const p = S.panes[S.active];
  if (id.startsWith('server:')) return connect(id.slice(7));
  switch (id) {
    case 'xfer': {
      if (!p) return;
      if (p.id === 'local') return transfer(p, [...p.sel], S.panes.remote);
      if (T.layout === 'B' && !S.split) toast('info', 'Downloading to Downloads', 'Open This PC from the sidebar to pick another folder.');
      return transfer(p, [...p.sel], S.panes.local);
    }
    case 'newfolder': return newFolder(p);
    case 'rename': { const n = selNodes(p)[0]; if (n) startRename(p, n.name); return; }
    case 'delete': { const n = selNodes(p); if (n.length) S.dialog = { kind: 'delete', pane: p.id, names: n.map(x => x.name) }; return render(); }
    case 'perm': {
      const n = selNodes(p); if (!n.length || p.id !== 'remote') return;
      S.dialog = { kind: 'perm', bits: n[0].perm.split('').map(c => c !== '-'), owner: n[0].owner, group: n[0].group, recursive: false, count: n.length, hasDir: n.some(x => x.type === 'dir') };
      return render();
    }
    case 'refresh': if (p.id === 'remote') { p.loading = true; later(260, () => { p.loading = false; }); } return render();
    case 'hidden': S.showHidden = !S.showHidden; return render();
    case 'goto': p.editPath = true; S.pathDraft = pathLabel(p); render(); return focusEl(`path-${p.id}`, true);
    case 'term': toast('info', 'Opening Windows Terminal', `ssh ${srv().user}@${srv().host}, starting in ${pathLabel(p)}`); return render();
    case 'copypath': toast('ok', 'Path copied', pathLabel(p)); return render();
    case 'theme': T.theme = T.theme === 'dark' ? 'light' : 'dark'; saveTweaks(); return render();
    case 'settings': S.dialog = { kind: 'settings' }; return render();
    case 'disconnect': disconnect(); return render();
  }
}
function focusEl(id, selectAll) { const el = document.getElementById(id); if (el) { el.focus(); if (selectAll) el.select(); } }

/* Views */
function titlebar() {
  const s = srv();
  const tabs = T.layout === 'C' && S.tabs.length
    ? `<div class="tabs">${S.tabs.map(id => { const x = servers.find(v => v.id === id); return `<div class="tab ${id === S.server && S.screen === 'browser' ? 'on' : ''}" data-act="tab" data-arg="${id}"><span class="dot"></span>${x.name}<span class="faint">${x.user}@${x.host}</span><span class="x" data-act="tab-close" data-arg="${id}">${G.close}</span></div>`; }).join('')}<button class="iconbtn" data-act="tab-new" title="Connect to another server">${'<i class="i">' + G.add + '</i>'}</button></div>`
    : `<span class="t">Portal${s && S.screen === 'browser' ? ` — ${s.name}` : ''}</span>`;
  const search = T.layout === 'C' && S.screen === 'browser' ? `<div class="palette-trigger" data-act="palette"><i class="i">${G.search}</i>Search files or run a command<kbd>Ctrl+K</kbd></div>` : '';
  return `<img src="../../assets/icon.png" alt="">${tabs}<span class="spacer"></span>${search}<span class="spacer"></span><div class="winctl"><span>${G.min}</span><span>${G.max}</span><span>${G.x}</span></div>`;
}

function connectView() {
  const c = S.connecting;
  const list = servers.map(s => {
    const busy = c && c.id === s.id;
    const steps = [`Finding ${s.host}${s.via === 'Tailscale' ? ' on Tailscale' : ''}`, 'Checking the server’s identity', 'Signing in with your SSH key (id_ed25519)', 'Opening files'];
    const prog = busy && !c.error ? `<div class="progress">${steps.map((t, i) => `<div class="step ${i < c.step ? 'ok' : i === c.step ? 'now' : 'wait'}"><i class="i">${i < c.step ? G.ok : i === c.step ? G.sync : ''}</i>${esc(t)}</div>`).join('')}</div>` : '';
    const err = busy && c.error ? `<div class="err"><i class="i">${G.error}</i><div>${esc(c.error)}<div style="margin-top:8px;display:flex;gap:8px"><button class="btn" data-act="connect" data-arg="${s.id}">Try again</button><button class="btn ghost" data-act="forget" data-arg="${s.id}">Remove</button></div></div></div>` : '';
    const live = S.tabs.includes(s.id);
    return `<div class="server" data-act="${busy ? '' : 'connect'}" data-arg="${s.id}">
      <div class="ic"><i class="i">${G.server}</i></div>
      <div><b>${esc(s.name)}</b><small>${esc(s.user)}@${esc(s.host)} · ${esc(s.via)} · ${live ? 'connected' : 'last used ' + esc(s.last)}</small></div>
      <div class="side">${busy && !c.error ? `<button class="btn" data-act="cancel-connect">Cancel</button>` : live ? `<button class="btn primary" data-act="connect" data-arg="${s.id}">Open</button>` : `<button class="btn primary" data-act="connect" data-arg="${s.id}">Connect</button>`}</div>
      ${prog}${err}
    </div>`;
  }).join('');
  const f = S.form;
  return `<div class="connect">
    <div class="left">
      <h1>Your servers</h1>
      <p class="lead">Pick a server to browse and manage its files. Portal signs in with your SSH keys and checks each server’s identity before it connects.</p>
      <div class="servers">${list}</div>
      <div class="cfg-hosts">Found in your SSH config:<button class="chip" data-act="cfg" data-arg="hetzner"><i class="i">${G.add}</i>hetzner</button><button class="chip" data-act="cfg" data-arg="nas"><i class="i">${G.add}</i>nas.local</button></div>
    </div>
    <div class="right">
      <h2>Add a server</h2>
      <div class="form">
        <div class="field"><label for="f-host">Address</label><input id="f-host" data-form="host" placeholder="tower, 192.168.2.3 or server.example.com" value="${esc(f.host)}"><span class="hint">A name from your SSH config works too.</span></div>
        <div class="row2">
          <div class="field"><label for="f-user">User</label><input id="f-user" data-form="user" value="${esc(f.user)}"></div>
          <div class="field"><label for="f-port">Port</label><input id="f-port" data-form="port" value="${esc(f.port)}"></div>
        </div>
        <div class="field"><label>Sign in with</label><div class="seg">${[['key', 'SSH key'], ['file', 'Key file…'], ['password', 'Password']].map(([k, l]) => `<button class="${f.auth === k ? 'on' : ''}" data-act="auth" data-arg="${k}">${l}</button>`).join('')}</div>
          <span class="hint">${f.auth === 'key' ? 'Uses the keys in C:\\Users\\Oss\\.ssh and the SSH agent. Asks for a passphrase if a key has one.' : f.auth === 'file' ? 'Choose a private key file. Portal remembers the path, never the key.' : 'Stored in Windows Credential Manager.'}</span></div>
        <div><button class="btn primary" data-act="add-server" ${f.host.trim() ? '' : 'aria-disabled="true"'}>Save and connect</button></div>
      </div>
    </div>
  </div>`;
}

function cmdbarView() {
  const p = S.panes[S.active], sel = selNodes(p), remote = p.id === 'remote';
  const c = Object.fromEntries(cmds().map(x => [x.id, x]));
  const b = (id, label, plain) => `<button class="cmd ${plain ? 'plain' : ''}" data-act="run" data-arg="${id}" ${c[id].on ? '' : 'disabled'} title="${esc(c[id].label)}${c[id].kbd ? ` (${c[id].kbd})` : ''}"><i class="i">${c[id].icon}</i>${label}</button>`;
  const total = sel.reduce((a, n) => a + (n.type === 'dir' ? 0 : n.size), 0);
  const where = remote ? srv().name : 'This PC';
  const chip = sel.length ? `${sel.length} selected${total ? ` · ${fmtSize(total)}` : ''} on ${where}` : where;
  const toggles = T.layout === 'B'
    ? `<button class="cmd plain" data-act="split" title="Show This PC next to the server"><i class="i">${G.pane}</i>${S.split ? 'Hide This PC' : 'Show This PC'}</button>`
    : '';
  const details = T.layout !== 'A' ? `<button class="cmd plain" data-act="inspector"><i class="i">${G.info}</i>Details</button>` : '';
  return `<div class="cmdbar">
    ${b('newfolder', 'New folder')}
    <span class="sep"></span>
    ${b('xfer', remote ? 'Download' : 'Upload')}
    ${b('rename', 'Rename', true)}
    ${remote ? b('perm', 'Permissions', true) : ''}
    ${b('delete', 'Delete', true)}
    <span class="grow"></span>
    <span class="context-chip">${esc(chip)}</span>
    <span class="sep"></span>
    ${b('hidden', S.showHidden ? 'Hidden files: on' : 'Hidden files', true)}
    ${b('refresh', 'Refresh', true)}
    ${toggles}${details}
  </div>`;
}

function crumbsView(p) {
  if (p.editPath) return `<div class="crumbs editing"><input id="path-${p.id}" data-path="${p.id}" value="${esc(S.pathDraft)}" spellcheck="false"></div>`;
  const root = p.id === 'local' ? (p.path.length ? `<i class="i" style="font-size:12px">${G.pc}</i>` : 'This PC') : '/';
  const parts = [root, ...p.path.map(esc)];
  return `<div class="crumbs" data-act="edit-path" data-pane="${p.id}" title="Click to type a path (Ctrl+L)">${parts.map((x, i) => `${i ? `<span class="chev">${G.chevR}</span>` : ''}<span class="c" data-act="crumb" data-pane="${p.id}" data-depth="${i}">${x}</span>`).join('')}</div>`;
}

function paneView(p, narrow) {
  const remote = p.id === 'remote', { items, hidden } = listAll(p);
  const sortArrow = k => p.sort.k === k ? `<i class="arrow">${p.sort.d > 0 ? G.chevU : G.chevD}</i>` : '';
  const col = (k, l, cls = '') => `<span class="${cls}" data-act="sort" data-pane="${p.id}" data-key="${k}">${l}${sortArrow(k)}</span>`;
  const cols = remote
    ? `${col('name', 'Name')}${col('mtime', 'Modified')}${col('size', 'Size', 'r')}${col('perm', 'Permissions')}${col('owner', 'Owner', 'own')}`
    : `${col('name', 'Name')}${col('mtime', 'Modified')}${col('size', 'Size', 'r')}`;
  let body;
  if (p.loading) body = Array.from({ length: 7 }, (_, i) => `<div class="row"><div class="skeleton" style="width:${60 - i * 5}%"></div><div class="skeleton" style="width:70%"></div><div class="skeleton"></div>${remote ? '<div class="skeleton"></div><div class="skeleton own"></div>' : ''}</div>`).join('');
  else if (!items.length && p.filter) body = `<div class="empty"><div><i class="i">${G.search}</i><b>Nothing here matches “${esc(p.filter)}”</b><button class="btn" data-act="clear-filter" data-pane="${p.id}" style="margin-top:12px">Clear filter</button></div></div>`;
  else if (!items.length) body = `<div class="empty"><div><i class="i">${G.folder}</i><b>This folder is empty</b>${remote ? 'Drag files here from This PC or Explorer to upload them.' : 'Drag files here from the server to download them.'}</div></div>`;
  else body = items.map(n => rowView(p, n, remote)).join('');
  const selN = selNodes(p), selSize = selN.reduce((a, n) => a + (n.type === 'dir' ? 0 : n.size), 0);
  const foot = `${items.length} item${items.length === 1 ? '' : 's'}${selN.length ? ` · ${selN.length} selected${selSize ? ` (${fmtSize(selSize)})` : ''}` : ''}${hidden && p.filter ? ` · ${hidden} hidden by the filter` : ''}`;
  const who = remote ? `<span class="who"><span class="dot"></span>${esc(srv().name)}</span>` : `<span class="who"><i class="i">${G.pc}</i>This PC</span>`;
  return `<section class="pane ${remote ? 'remote' : 'local'} ${S.active === p.id && panesShown().length > 1 ? 'active' : ''} ${narrow ? 'narrow' : ''}" data-pane="${p.id}">
    <div class="pane-head">
      <button class="iconbtn" data-act="back" data-pane="${p.id}" ${p.back.length ? '' : 'disabled'} title="Back (Alt+Left)"><i class="i">${G.back}</i></button>
      <button class="iconbtn" data-act="fwd" data-pane="${p.id}" ${p.fwd.length ? '' : 'disabled'} title="Forward (Alt+Right)"><i class="i">${G.fwd}</i></button>
      <button class="iconbtn" data-act="up" data-pane="${p.id}" ${p.path.length ? '' : 'disabled'} title="Up one level (Backspace)"><i class="i">${G.up}</i></button>
      ${who}${crumbsView(p)}
      <label class="filter"><i class="i">${G.search}</i><input id="filter-${p.id}" data-filter="${p.id}" placeholder="Filter ${esc(shortPath(p))}" value="${esc(p.filter)}"></label>
    </div>
    <div class="cols">${cols}</div>
    <div class="list" tabindex="0" data-list="${p.id}" data-scroll="list-${p.id}-${pathLabel(p)}">${body}</div>
    <div class="pane-foot">${foot}</div>
  </section>`;
}

function rowView(p, n, remote) {
  const [cls, ic] = iconFor(n);
  const renaming = S.renaming && S.renaming.pane === p.id && S.renaming.name === n.name;
  const size = n.type === 'dir' ? `<span class="faint">${n.children.length} item${n.children.length === 1 ? '' : 's'}</span>` : fmtSize(n.size);
  return `<div class="row ${p.sel.has(n.name) ? 'sel' : ''} ${p.focus === n.name ? 'focus' : ''} ${n.name.startsWith('.') ? 'hidden-file' : ''}" draggable="${renaming ? 'false' : 'true'}" data-row="${esc(n.name)}">
    <div class="name"><i class="i ${cls}">${ic}</i>${renaming ? `<input id="rename-input" class="rename" value="${esc(S.renameValue)}" spellcheck="false">` : `<span>${esc(n.name)}</span>`}</div>
    <div class="cell">${fmtDate(n.mtime)}</div>
    <div class="cell r">${size}</div>
    ${remote ? `<div class="mode" title="${esc(describePerm(n.perm))}">${n.type === 'dir' ? 'd' : '-'}${n.perm}</div><div class="cell own" title="Group: ${esc(n.group)}">${esc(n.owner)}</div>` : ''}
  </div>`;
}

function navView() {
  const s = srv(), r = S.panes.remote;
  const cur = '/' + r.path.join('/');
  const favs = s.favs.map(([l, path]) => `<a data-act="nav-remote" data-path="${esc(JSON.stringify(path))}" class="${cur === '/' + path.join('/') ? 'on' : ''}"><i class="i">${l === 'Shares' ? G.home : G.folder}</i>${esc(l)}</a>`).join('');
  const storage = s.storage.map(([l, pct, free]) => `<a><i class="i">${G.server}</i>${l}<span class="meta">${free}</span></a><div class="usage ${pct > 80 ? 'high' : ''}" title="${pct} % used"><i style="width:${pct}%"></i></div>`).join('');
  const srvs = servers.map(x => `<a data-act="nav-server" data-arg="${x.id}" class="${x.id === S.server ? 'on' : ''}"><i class="i">${G.server}</i>${x.name}<span class="dot ${S.tabs.includes(x.id) ? '' : 'off'}" title="${S.tabs.includes(x.id) ? 'Connected' : 'Not connected'}"></span></a>`).join('');
  const local = [['Downloads', ['C:', 'Users', 'Oss', 'Downloads']], ['Documents', ['C:', 'Users', 'Oss', 'Documents']], ['Desktop', ['C:', 'Users', 'Oss', 'Desktop']]]
    .map(([l, path]) => `<a data-act="nav-local" data-path="${esc(JSON.stringify(path))}"><i class="i">${G.folder}</i>${l}</a>`).join('');
  return `<nav class="nav" data-scroll="nav"><h4>${esc(s.name)}</h4>${favs}<h4>Storage on ${esc(s.name)}</h4>${storage}<h4>Servers</h4>${srvs}<a data-act="tab-new"><i class="i">${G.add}</i>Add server</a><h4>This PC</h4>${local}</nav>`;
}

function inspectorView() {
  const p = S.panes[S.active], sel = selNodes(p), remote = p.id === 'remote';
  const here = walk(p.fs, p.path);
  const loc = pathLabel(p);
  if (sel.length === 1) {
    const n = sel[0], [cls, ic] = iconFor(n);
    const key = loc + '/' + n.name, sz = S.sizes[key];
    const size = n.type === 'dir'
      ? (sz === 'busy' ? '<span class="faint">Calculating…</span>' : sz != null ? `${fmtSize(sz)} · ${countFiles(n)} files` : `<button class="btn" data-act="calc" data-arg="${esc(key)}" style="height:24px;font-size:12px">Calculate size</button>`)
      : fmtSize(n.size);
    return `<aside class="inspector" data-scroll="insp">
      <div class="big"><i class="i ${cls}" style="${n.type === 'dir' ? 'color:var(--folder)' : ''}">${ic}</i></div>
      <h3>${esc(n.name)}</h3><div class="muted" style="font-size:12px">${esc(kindOf(n))}</div>
      <dl><dt>Location</dt><dd class="mono">${esc(loc)}</dd><dt>Modified</dt><dd>${fmtDate(n.mtime)}</dd><dt>Size</dt><dd>${size}</dd>
      ${remote ? `<dt>Owner</dt><dd>${esc(n.owner)} <span class="faint">· group ${esc(n.group)}</span></dd><dt>Permissions</dt><dd><span class="mono">${octal(n.perm)} · ${n.perm}</span>${permParts(n.perm).map(([w, c]) => `<div class="faint" style="margin-top:2px">${w} can ${c}</div>`).join('')}</dd>` : ''}</dl>
      <div class="actions">
        <button class="btn primary" data-act="run" data-arg="xfer"><i class="i">${remote ? G.download : G.upload}</i>${remote ? 'Download' : 'Upload'}</button>
        <button class="btn" data-act="run" data-arg="rename"><i class="i">${G.rename}</i>Rename</button>
        ${remote ? `<button class="btn" data-act="run" data-arg="perm"><i class="i">${G.lock}</i>Permissions</button>` : ''}
        <button class="btn" data-act="run" data-arg="delete"><i class="i">${G.del}</i>Delete</button>
      </div>
      ${remote && n.type === 'file' ? `<div class="section"><h5>Edit on the server</h5><div class="muted" style="font-size:12px;margin-bottom:8px">Opens a copy in its usual app. When you save, Portal uploads it back to ${esc(srv().name)}.</div><button class="btn" data-act="open-file" data-arg="${esc(n.name)}"><i class="i">${G.open}</i>Open</button></div>` : ''}
    </aside>`;
  }
  if (sel.length > 1) {
    const files = sel.filter(n => n.type !== 'dir'), dirs = sel.length - files.length;
    return `<aside class="inspector" data-scroll="insp"><div class="big"><i class="i" style="color:var(--folder)">${G.copy}</i></div>
      <h3>${sel.length} items</h3><div class="muted" style="font-size:12px">${[files.length && `${files.length} file${files.length === 1 ? '' : 's'} (${fmtSize(files.reduce((a, n) => a + n.size, 0))})`, dirs && `${dirs} folder${dirs > 1 ? 's' : ''}`].filter(Boolean).join(' and ')}</div>
      <dl><dt>Location</dt><dd class="mono">${esc(loc)}</dd></dl>
      <div class="actions"><button class="btn primary" data-act="run" data-arg="xfer"><i class="i">${remote ? G.download : G.upload}</i>${remote ? 'Download all' : 'Upload all'}</button>${remote ? `<button class="btn" data-act="run" data-arg="perm"><i class="i">${G.lock}</i>Permissions</button>` : ''}<button class="btn" data-act="run" data-arg="delete"><i class="i">${G.del}</i>Delete</button></div>
    </aside>`;
  }
  return `<aside class="inspector" data-scroll="insp"><div class="big"><i class="i" style="color:var(--folder)">${G.folder}</i></div>
    <h3>${esc(shortPath(p))}</h3><div class="muted" style="font-size:12px">${here ? here.children.length : 0} items</div>
    <dl><dt>Location</dt><dd class="mono">${esc(loc)}</dd>${remote ? `<dt>Server</dt><dd>${esc(srv().name)} · ${esc(srv().user)}@${esc(srv().host)}</dd>` : ''}</dl>
    <div class="section"><div class="muted" style="font-size:12px">Select a file or folder to see its details. Shift and Ctrl select several; Ctrl+A selects everything.</div></div>
  </aside>`;
}

function queueRows(flyout) {
  if (!S.queue.length) return `<div class="empty" style="height:120px"><div><b>No transfers yet</b>Drag files between This PC and ${esc(srv().name)}, or use Upload and Download.</div></div>`;
  return [...S.queue].reverse().map(q => {
    const pct = q.done / q.size;
    const st = q.status === 'running' ? `${fmtSize(q.rate)}/s · ${fmtEta((q.size - q.done) / q.rate)}`
      : q.status === 'queued' ? (q.failedOnce ? `Resuming from ${fmtSize(q.done)}` : 'Waiting')
      : q.status === 'paused' ? 'Paused' : q.status === 'done' ? 'Done' : q.status === 'cancelled' ? 'Cancelled' : q.error;
    const acts = q.status === 'running' ? `<button class="iconbtn" data-act="q-pause" data-arg="${q.id}" title="Pause"><i class="i">${G.pause}</i></button><button class="iconbtn" data-act="q-cancel" data-arg="${q.id}" title="Cancel"><i class="i">${G.close}</i></button>`
      : q.status === 'paused' || q.status === 'queued' ? `<button class="iconbtn" data-act="q-resume" data-arg="${q.id}" title="Resume"><i class="i">${G.play}</i></button><button class="iconbtn" data-act="q-cancel" data-arg="${q.id}" title="Cancel"><i class="i">${G.close}</i></button>`
      : q.status === 'failed' ? `<button class="btn" data-act="q-retry" data-arg="${q.id}" style="height:28px">Retry</button>`
      : q.status === 'done' ? `<button class="iconbtn" data-act="q-show" data-arg="${q.id}" title="Open folder"><i class="i">${G.open}</i></button>` : '';
    return `<div class="qrow ${q.status}">
      <i class="i ${q.up ? 'dir-up' : 'dir-down'}">${q.up ? G.upload : G.download}</i>
      <div class="what"><b>${esc(q.name)}${q.files > 1 ? ` <span class="faint">· ${q.files} files</span>` : ''}</b><small>${esc(q.fromLabel)} → ${esc(q.up ? q.server + ' ' : '')}${esc(q.toLabel)}</small></div>
      ${flyout ? '' : `<div><div class="bar"><i style="width:${(pct * 100).toFixed(1)}%"></i></div></div>`}
      <div class="st" title="${esc(st)}">${flyout && q.status === 'running' ? `${Math.round(pct * 100)} % · ` : ''}${esc(st)}</div>
      <div class="acts">${acts}</div>
    </div>`;
  }).join('');
}
function queueSummary() {
  const act = S.queue.filter(q => q.status === 'running' || q.status === 'queued');
  if (!act.length) {
    const failed = S.queue.filter(q => q.status === 'failed').length;
    return failed ? `${failed} need${failed > 1 ? '' : 's'} attention` : S.queue.length ? 'All done' : '';
  }
  const left = act.reduce((a, q) => a + q.size - q.done, 0), rate = act.filter(q => q.status === 'running').reduce((a, q) => a + q.rate, 0);
  return `${act.length} active · ${fmtEta(left / rate)}`;
}
function queueView(flyout) {
  const hasDone = S.queue.some(q => ['done', 'cancelled'].includes(q.status));
  return `<div class="${flyout ? 'flyout' : 'queue'}">
    <div class="queue-head">Transfers <span class="faint">${esc(queueSummary())}</span><span class="grow"></span>
      ${hasDone ? `<button class="btn ghost" data-act="q-clear" style="height:28px">Clear finished</button>` : ''}
      <button class="iconbtn" data-act="queue" title="Close"><i class="i">${flyout ? G.close : G.chevD}</i></button></div>
    <div class="qlist" data-scroll="q">${queueRows(flyout)}</div>
  </div>`;
}

function statusView() {
  const s = srv(), act = S.queue.filter(q => q.status === 'running' || q.status === 'queued');
  const pct = act.length ? act.reduce((a, q) => a + q.done, 0) / act.reduce((a, q) => a + q.size, 0) : 0;
  const failed = S.queue.some(q => q.status === 'failed');
  return `<div class="statusbar">
    <span class="conn"><span class="dot"></span>Connected to ${esc(s.name)} as ${esc(s.user)}</span><span class="faint">${esc(s.host)} · ${esc(s.via)} · ${s.latency} ms</span>
    <span class="grow"></span>
    <button data-act="queue">${failed ? `<i class="i" style="color:var(--danger);font-size:12px">${G.warn}</i>` : `<i class="i" style="font-size:12px">${G.sync}</i>`}${act.length ? `${esc(queueSummary())}<span class="bar mini"><i style="width:${(pct * 100).toFixed(1)}%"></i></span>` : failed ? 'Transfers need attention' : 'Transfers'}</button>
    <button data-act="run" data-arg="settings"><i class="i" style="font-size:12px">${G.settings}</i>Settings</button>
  </div>`;
}

function browserView() {
  const L = T.layout, shown = panesShown();
  if (!shown.includes(S.active)) S.active = 'remote';
  const narrow = L !== 'A' && S.inspector && shown.length > 1;
  const panes = `<div class="panes" style="grid-template-columns:${shown.map(() => 'minmax(0,1fr)').join(' ')}">${shown.map(id => paneView(S.panes[id], narrow && id === 'remote')).join('')}</div>`;
  const insp = L !== 'A' && S.inspector ? inspectorView() : '';
  if (L === 'A') {
    return `<div class="app-inner" style="display:grid;grid-template-rows:auto minmax(0,1fr) ${S.queueOpen ? '224px ' : ''}auto;min-height:0">${cmdbarView()}${panes}${S.queueOpen ? queueView(false) : ''}${statusView()}</div>`;
  }
  const cols = [L === 'B' ? '232px' : null, 'minmax(0,1fr)', insp ? '296px' : null].filter(Boolean).join(' ');
  return `<div class="app-inner" style="display:grid;grid-template-rows:auto minmax(0,1fr) auto;min-height:0">${cmdbarView()}
    <div class="body" style="grid-template-columns:${cols}">${L === 'B' ? navView() : ''}${panes}${insp}</div>${statusView()}</div>${S.queueOpen ? queueView(true) : ''}`;
}

function dialogView() {
  const d = S.dialog; if (!d) return '';
  let inner = '';
  if (d.kind === 'delete') {
    const p = S.panes[d.pane], remote = d.pane === 'remote';
    const nodes = walk(p.fs, p.path).children.filter(c => d.names.includes(c.name));
    const files = nodes.reduce((a, n) => a + countFiles(n), 0);
    inner = `<div class="content"><h3>${remote ? `Delete ${d.names.length} item${d.names.length > 1 ? 's' : ''} from ${esc(srv().name)}?` : `Move ${d.names.length} item${d.names.length > 1 ? 's' : ''} to the Recycle Bin?`}</h3>
      <p>${remote ? `They are removed from <span class="mono">${esc(pathLabel(p))}</span> for good${nodes.some(n => n.type === 'dir') ? `, including ${files} file${files === 1 ? '' : 's'} inside the folders` : ''}. The server has no recycle bin, so this can’t be undone.` : 'You can restore them from the Recycle Bin.'}</p>
      <div class="items">${nodes.map(n => `<div><i class="i" style="${n.type === 'dir' ? 'color:var(--folder)' : ''}">${iconFor(n)[1]}</i>${esc(n.name)}<span class="faint" style="margin-left:auto">${n.type === 'dir' ? `${countFiles(n)} files` : fmtSize(n.size)}</span></div>`).join('')}</div></div>
      <div class="foot"><button class="btn" data-act="dlg-close">Cancel</button><button class="btn ${remote ? 'danger' : 'primary'}" data-act="del-confirm">${remote ? 'Delete' : 'Move to Recycle Bin'}</button></div>`;
  } else if (d.kind === 'conflict') {
    const where = d.up ? srv().name : 'This PC';
    inner = `<div class="content"><h3>“${esc(d.src.name)}” is already on ${esc(where)}</h3>
      <div class="compare"><div><b>${d.up ? 'Yours on This PC' : `On ${esc(srv().name)}`}</b>${d.src.type === 'dir' ? 'Folder' : fmtSize(d.src.size)} · ${fmtDate(d.src.mtime)}</div><div><b>Already ${d.up ? `on ${esc(srv().name)}` : 'on This PC'}</b>${d.dst.type === 'dir' ? 'Folder' : fmtSize(d.dst.size)} · ${fmtDate(d.dst.mtime)}</div></div>
      <div class="choice">
        <button data-act="conflict" data-arg="replace"><i class="i">${G.sync}</i><span>Replace it<small>The ${d.up ? 'server’s' : 'local'} copy is overwritten.</small></span></button>
        <button data-act="conflict" data-arg="keep"><i class="i">${G.copy}</i><span>Keep both<small>The new copy is saved as “${esc(uniqueName(walk(d.up ? S.panes.remote.fs : S.panes.local.fs, d.up ? S.panes.remote.path : S.panes.local.path), d.src.name))}”.</small></span></button>
        <button data-act="conflict" data-arg="skip"><i class="i">${G.fwd}</i><span>Skip this one<small>Nothing changes on ${esc(where)}.</small></span></button>
      </div></div>
      <div class="foot"><label class="left">${d.remaining > 0 ? `<input type="checkbox" class="check" data-act="conflict-all" ${d.all ? 'checked' : ''}>Do the same for the next ${d.remaining} conflict${d.remaining > 1 ? 's' : ''}` : ''}</label><button class="btn" data-act="conflict" data-arg="cancel">Stop the transfer</button></div>`;
  } else if (d.kind === 'perm') {
    const who = ['Owner', 'Group', 'Everyone'], what = ['Read', 'Write', 'Run'];
    const perm = 'rwxrwxrwx'.split('').map((ch, i) => d.bits[i] ? ch : '-').join('');
    inner = `<div class="content"><h3>Permissions for ${d.count > 1 ? `${d.count} items` : `“${esc(selNodes(S.panes.remote)[0].name)}”`}</h3>
      <div class="row2" style="grid-template-columns:1fr 1fr;margin-bottom:16px">
        <div class="field"><label>Owner</label><select data-perm="owner">${['root', 'nobody', 'oss'].map(o => `<option ${o === d.owner ? 'selected' : ''}>${o}</option>`).join('')}</select></div>
        <div class="field"><label>Group</label><select data-perm="group">${['root', 'users', 'oss'].map(o => `<option ${o === d.group ? 'selected' : ''}>${o}</option>`).join('')}</select></div>
      </div>
      <div class="perm"><span></span>${what.map(w => `<span class="h">${w}</span>`).join('')}
        ${who.map((w, r) => `<span>${w}</span>${what.map((_, c) => `<label><input type="checkbox" class="check" data-act="perm-bit" data-arg="${r * 3 + c}" ${d.bits[r * 3 + c] ? 'checked' : ''} aria-label="${w} can ${what[c].toLowerCase()}"></label>`).join('')}`).join('')}</div>
      <div class="octal">Same as <span class="mono">chmod ${octal(perm)}</span><span class="mono">${perm}</span></div>
      ${d.hasDir ? `<label class="octal" style="gap:8px"><input type="checkbox" class="check" data-act="perm-rec" ${d.recursive ? 'checked' : ''}>Apply to everything inside the selected folders</label>` : ''}
      </div>
      <div class="foot"><button class="btn" data-act="dlg-close">Cancel</button><button class="btn primary" data-act="perm-apply">Apply</button></div>`;
  } else if (d.kind === 'hostkey') {
    inner = `<div class="content"><h3>Is this really ${esc(d.server.host)}?</h3>
      <p>This is the first time Portal connects to this server. Compare the fingerprint with the one the server shows for <span class="mono">ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub</span>.</p>
      <div class="fp">ED25519 · SHA256:prototype-fingerprint-shown-here</div>
      <p style="font-size:12px">Portal saves it to C:\\Users\\Oss\\.ssh\\known_hosts and warns you if it ever changes.</p></div>
      <div class="foot"><button class="btn" data-act="hostkey-cancel">Cancel</button><button class="btn primary" data-act="hostkey-trust">Trust and connect</button></div>`;
  } else if (d.kind === 'settings') {
    const st = S.settings;
    inner = `<div class="content" style="max-height:520px;overflow:auto"><h3>Settings</h3>
      <div class="form">
        <div class="field"><label>Theme</label><div class="seg">${[['system', 'Use Windows setting'], ['light', 'Light'], ['dark', 'Dark']].map(([k, l]) => `<button class="${(k === T.theme) || (k === 'system' && !['light', 'dark'].includes(T.theme)) ? 'on' : ''}" data-act="set-theme" data-arg="${k}">${l}</button>`).join('')}</div></div>
        <div class="field"><label>Transfers at the same time</label><select data-setting="parallel">${[1, 2, 3, 4, 6, 8].map(n => `<option ${n === st.parallel ? 'selected' : ''}>${n}</option>`).join('')}</select><span class="hint">More helps with many small files; one is fastest for a single big file.</span></div>
        <div class="field"><label>When a file already exists</label><select data-setting="conflict">${[['ask', 'Ask me'], ['replace', 'Replace it'], ['skip', 'Skip it'], ['keep', 'Keep both']].map(([k, l]) => `<option value="${k}" ${k === st.conflict ? 'selected' : ''}>${l}</option>`).join('')}</select></div>
        <label class="octal" style="gap:8px;margin:0"><input type="checkbox" class="check" data-act="set-toggle" data-arg="reconnect" ${st.reconnect ? 'checked' : ''}>Reconnect to the last server when Portal starts</label>
        <div class="section" style="border-top:1px solid var(--stroke);padding-top:16px"><div class="field"><label>Updates</label><div style="display:flex;align-items:center;gap:12px">Portal 1.0.0 · up to date<button class="btn" data-act="check-update">Check now</button></div></div>
        <label class="octal" style="gap:8px"><input type="checkbox" class="check" data-act="set-toggle" data-arg="updates" ${st.updates ? 'checked' : ''}>Check for updates when Portal starts</label></div>
      </div><p style="font-size:12px;margin-top:16px">Changes apply right away.</p></div>
      <div class="foot"><button class="btn primary" data-act="dlg-close">Done</button></div>`;
  }
  return `<div class="scrim" data-act="scrim"><div class="dialog" role="dialog" aria-modal="true">${inner}</div></div>`;
}

function menuView() {
  const m = S.menu; if (!m) return '';
  return `<div class="menu" style="left:${m.x}px;top:${m.y}px">${m.items.map((it, i) => it === '-' ? '<hr>' : `<button data-act="menu-run" data-arg="${i}" class="${it.danger ? 'danger' : ''}" ${it.on === false ? 'disabled' : ''}><i class="i">${it.icon || ''}</i>${esc(it.label)}<kbd>${it.kbd || ''}</kbd></button>`).join('')}</div>`;
}
function paletteItems() {
  const q = S.palette.q.toLowerCase();
  const items = cmds().filter(c => c.on).map(c => ({ ...c, kind: 'Command' }));
  const p = S.panes.remote;
  if (p) {
    const all = [];
    const scan = (n, path) => { (n.children || []).forEach(c => { all.push({ id: 'goto:' + JSON.stringify([...path, c.name]) + ':' + c.type, label: c.name, icon: iconFor(c)[1], kind: '/' + path.join('/') }); if (path.length < 5) scan(c, [...path, c.name]); }); };
    scan(p.fs, []);
    if (q) items.push(...all);
  }
  return items.filter(c => !q || c.label.toLowerCase().includes(q)).slice(0, 12);
}
function paletteView() {
  if (!S.palette) return '';
  const items = paletteItems(), idx = Math.min(S.palette.idx, items.length - 1);
  return `<div class="palette"><input id="palette-input" placeholder="Type a command, or the name of a file on ${esc(srv()?.name || 'the server')}" value="${esc(S.palette.q)}">
    <div class="opts">${items.map((c, i) => `<div class="opt ${i === idx ? 'on' : ''}" data-act="palette-run" data-arg="${i}"><i class="i">${c.icon}</i><span>${esc(c.label)}</span><small>${esc(c.kbd || c.kind)}</small></div>`).join('') || '<div class="opt"><span></span><span class="faint">No matches</span></div>'}</div></div>`;
}
function runPalette(i) {
  const it = paletteItems()[i]; S.palette = null;
  if (!it) return render();
  if (it.id.startsWith('goto:')) {
    const [, json, type] = it.id.match(/^goto:(.*):(dir|file)$/);
    const path = JSON.parse(json), p = S.panes.remote;
    if (type === 'dir') navigate(p, path); else { navigate(p, path.slice(0, -1)); p.sel = new Set([path[path.length - 1]]); p.focus = path[path.length - 1]; }
    return render();
  }
  run(it.id); render();
}
function toastsView() {
  return `<div class="toasts">${S.toasts.map(t => `<div class="toast ${t.kind}" role="status"><i class="i">${t.kind === 'ok' ? G.ok : t.kind === 'err' ? G.error : G.info}</i><div><b>${esc(t.title)}</b><small>${esc(t.body || '')}</small>${t.link ? `<div><button class="link" data-act="toast-link" data-arg="${t.id}">${esc(t.link.label)}</button></div>` : ''}</div><button class="iconbtn" data-act="toast-close" data-arg="${t.id}" style="width:24px;height:24px"><i class="i" style="font-size:10px">${G.close}</i></button></div>`).join('')}</div>`;
}
function tweaksView() {
  const grp = (k, label, opts) => `<div class="grp"><span>${label}</span><div class="opts">${opts.map(([v, l]) => `<button class="${T[k] === v ? 'on' : ''}" data-tw="${k}" data-v="${v}">${l}</button>`).join('')}</div></div>`;
  return `<h6>Tweaks <button data-tw="min" style="background:none;border:0;color:#fff">${T.min ? 'Show' : 'Hide'}</button></h6>
    ${grp('layout', 'Layout', [['A', 'A · Twin'], ['B', 'B · Server'], ['C', 'C · Workspace']])}
    <p>${esc(LAYOUTS[T.layout][1])}</p>
    ${grp('theme', 'Theme', [['dark', 'Dark'], ['light', 'Light']])}
    ${grp('density', 'Rows', [['comfortable', 'Comfortable'], ['compact', 'Compact']])}
    ${grp('accent', 'Accent', [['blue', 'Portal blue'], ['teal', 'Portal teal']])}`;
}

function render() {
  const fe = document.activeElement;
  const keep = fe && fe.id ? { id: fe.id, s: fe.selectionStart, e: fe.selectionEnd } : null;
  const scroll = {}; document.querySelectorAll('[data-scroll]').forEach(el => { scroll[el.dataset.scroll] = el.scrollTop; });
  const root = document.documentElement;
  root.dataset.theme = T.theme === 'system' ? (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark') : T.theme;
  root.dataset.density = T.density; root.dataset.accent = T.accent;
  $('#titlebar').innerHTML = titlebar();
  $('#app').innerHTML = (S.screen === 'connect' || !S.server ? connectView() : browserView()) + dialogView() + menuView() + paletteView() + toastsView();
  const tw = $('#tweaks'); tw.innerHTML = tweaksView(); tw.classList.toggle('min', !!T.min);
  document.querySelectorAll('[data-scroll]').forEach(el => { if (scroll[el.dataset.scroll] != null) el.scrollTop = scroll[el.dataset.scroll]; });
  if (keep) { const el = document.getElementById(keep.id); if (el) { el.focus(); try { el.setSelectionRange(keep.s, keep.e); } catch { } } }
  else if (S.palette) focusEl('palette-input');
  const ri = document.getElementById('rename-input');
  if (ri && fe !== ri && !(keep && keep.id === 'rename-input')) { ri.focus(); const dot = S.renameValue.lastIndexOf('.'); ri.setSelectionRange(0, dot > 0 ? dot : S.renameValue.length); }
}

/* Events */
const win = $('#win');
win.addEventListener('click', e => {
  const t = e.target.closest('[data-act]');
  if (S.menu && !(t && t.dataset.act === 'menu-run')) { S.menu = null; if (!t) return render(); }
  if (!t) {
    const row = e.target.closest('.row[data-row]'), list = e.target.closest('[data-list]');
    if (row) return clickRow(S.panes[row.closest('[data-pane]').dataset.pane], row.dataset.row, e);
    if (list) { const p = S.panes[list.dataset.list]; p.sel.clear(); S.active = p.id; return render(); }
    const pane = e.target.closest('[data-pane]'); if (pane && S.panes[pane.dataset.pane]) { S.active = pane.dataset.pane; render(); }
    return;
  }
  const a = t.dataset.act, arg = t.dataset.arg, p = t.dataset.pane ? S.panes[t.dataset.pane] : null;
  if (a === 'scrim') { if (e.target === t && S.dialog && S.dialog.kind !== 'conflict' && S.dialog.kind !== 'hostkey') { S.dialog = null; render(); } return; }
  e.stopPropagation();
  act(a, arg, p, t, e);
});
function act(a, arg, p, t, e) {
  switch (a) {
    case 'connect': return connect(arg);
    case 'cancel-connect': S.connecting = null; return render();
    case 'forget': { const i = servers.findIndex(s => s.id === arg); if (i >= 0 && servers[i].unknown) servers.splice(i, 1); S.connecting = null; return render(); }
    case 'cfg': S.form.host = arg === 'nas' ? 'nas.local' : arg; S.form.user = arg === 'nas' ? 'admin' : 'root'; return render();
    case 'auth': S.form.auth = arg; return render();
    case 'add-server': {
      const h = S.form.host.trim(); if (!h) return;
      const known = servers.find(s => s.host === h || s.id === h || (h === 'hetzner' && s.id === 'hetzner'));
      if (known) return connect(known.id);
      const s = { id: 'x' + Date.now(), name: h, user: S.form.user, host: h, via: 'Direct', latency: 0, known: false, unknown: true, last: 'never', fs: D('/'), home: [], favs: [], storage: [] };
      servers.push(s); S.form.host = ''; return connect(s.id);
    }
    case 'run': return run(arg);
    case 'back': if (p.back.length) { p.fwd.push(p.path); navigate(p, p.back.pop(), false); } return render();
    case 'fwd': if (p.fwd.length) { p.back.push(p.path); navigate(p, p.fwd.pop(), false); } return render();
    case 'up': if (p.path.length) navigate(p, p.path.slice(0, -1)); return render();
    case 'crumb': { const d = +t.dataset.depth; if (d !== p.path.length) navigate(p, p.path.slice(0, d)); return render(); }
    case 'edit-path': S.active = p.id; p.editPath = true; S.pathDraft = pathLabel(p); render(); return focusEl(`path-${p.id}`, true);
    case 'sort': { const k = t.dataset.key; p.sort = { k, d: p.sort.k === k ? -p.sort.d : 1 }; return render(); }
    case 'clear-filter': p.filter = ''; return render();
    case 'split': S.split = !S.split; S.active = S.split ? 'local' : 'remote'; return render();
    case 'inspector': S.inspector = !S.inspector; return render();
    case 'queue': S.queueOpen = !S.queueOpen; return render();
    case 'palette': S.palette = { q: '', idx: 0 }; return render();
    case 'palette-run': return runPalette(+arg);
    case 'nav-remote': navigate(S.panes.remote, JSON.parse(t.dataset.path)); return render();
    case 'nav-local': S.split = true; navigate(S.panes.local, JSON.parse(t.dataset.path)); return render();
    case 'nav-server': return arg === S.server ? render() : connect(arg);
    case 'tab': return S.tabs.includes(arg) ? (switchServer(arg), render()) : undefined;
    case 'tab-close': disconnect(arg); return render();
    case 'tab-new': S.screen = 'connect'; return render();
    case 'calc': S.sizes[arg] = 'busy'; render(); return later(700, () => { const p2 = S.panes[S.active]; const n = selNodes(p2)[0]; S.sizes[arg] = n ? dirSize(n) : 0; });
    case 'open-file': toast('info', `Opening ${arg}`, `Downloaded to a temporary folder. Save in the app and Portal uploads the change to ${srv().name}.`); return render();
    case 'q-pause': find(arg).status = 'paused'; return render();
    case 'q-resume': find(arg).status = 'queued'; return render();
    case 'q-cancel': { const q = find(arg); q.status = 'cancelled'; q.batch.left--; return render(); }
    case 'q-retry': { const q = find(arg); if (q) q.status = 'queued'; S.toasts = S.toasts.filter(x => !(x.link && x.link.arg === arg)); return render(); }
    case 'q-show': { const q = find(arg); const pane = q.up ? S.panes.remote : S.panes.local; if (!q.up && T.layout === 'B') S.split = true; navigate(pane, q.batch.dest); pane.sel = new Set([q.name]); S.queueOpen = T.layout === 'A' ? S.queueOpen : false; return render(); }
    case 'q-clear': S.queue = S.queue.filter(q => !['done', 'cancelled'].includes(q.status)); return render();
    case 'dlg-close': S.dialog = null; return render();
    case 'del-confirm': { const d = S.dialog; S.dialog = null; doDelete(S.panes[d.pane], d.names); return render(); }
    case 'undo-delete': { const b = S.lastBin; if (b) { b.dir.children.push(...b.removed); S.lastBin = null; } S.toasts = S.toasts.filter(x => !(x.link && x.link.act === 'undo-delete')); return render(); }
    case 'conflict': { const d = S.dialog; S.dialog = null; d.resolve({ choice: arg, all: d.all }); return; }
    case 'conflict-all': S.dialog.all = t.checked; return;
    case 'perm-bit': S.dialog.bits[+arg] = t.checked; return render();
    case 'perm-rec': S.dialog.recursive = t.checked; return;
    case 'perm-apply': applyPerm(); return render();
    case 'hostkey-trust': { const s = S.dialog.server; s.known = true; S.dialog = null; S.connecting.step = 2; render(); return later(420, () => { S.connecting.step = 3; later(420, () => finishConnect(s)); }); }
    case 'hostkey-cancel': S.dialog = null; S.connecting = null; return render();
    case 'set-theme': T.theme = arg; saveTweaks(); return render();
    case 'set-toggle': S.settings[arg] = t.checked; return;
    case 'check-update': toast('ok', 'Portal is up to date', 'Version 1.0.0 is the newest release on git.ossalali.com.'); return render();
    case 'menu-run': { const it = S.menu.items[+arg]; S.menu = null; it.fn(); return render(); }
    case 'toast-close': S.toasts = S.toasts.filter(x => x.id !== arg); return render();
    case 'toast-link': { const x = S.toasts.find(v => v.id === arg); S.toasts = S.toasts.filter(v => v !== x); if (x.link.act === 'queue') { S.queueOpen = true; return render(); } return act(x.link.act, x.link.arg, null, t, e); }
  }
}
const find = id => S.queue.find(q => q.id === id);

function clickRow(p, name, e) {
  S.active = p.id;
  if (S.renaming && S.renaming.name === name) return;
  const { items } = listAll(p), names = items.map(n => n.name);
  if (e.shiftKey && p.anchor && names.includes(p.anchor)) {
    const [a, b] = [names.indexOf(p.anchor), names.indexOf(name)].sort((x, y) => x - y);
    const range = names.slice(a, b + 1);
    p.sel = new Set(e.ctrlKey ? [...p.sel, ...range] : range);
  } else if (e.ctrlKey || e.metaKey) {
    p.sel.has(name) ? p.sel.delete(name) : p.sel.add(name); p.anchor = name;
  } else { p.sel = new Set([name]); p.anchor = name; }
  p.focus = name;
  render();
  const list = document.querySelector(`[data-list="${p.id}"]`); if (list) list.focus({ preventScroll: true });
}
function openItem(p, name) {
  const n = walk(p.fs, p.path).children.find(c => c.name === name); if (!n) return;
  if (n.type === 'dir') navigate(p, [...p.path, name]);
  else if (p.id === 'remote') act('open-file', name);
  else toast('info', `Opening ${name}`, 'Opens with its usual Windows app.');
  render();
}

win.addEventListener('dblclick', e => {
  const row = e.target.closest('.row[data-row]'); if (!row) return;
  openItem(S.panes[row.closest('[data-pane]').dataset.pane], row.dataset.row);
});

win.addEventListener('contextmenu', e => {
  const paneEl = e.target.closest('[data-pane]'); if (!paneEl || !S.panes[paneEl.dataset.pane]) return;
  e.preventDefault();
  const p = S.panes[paneEl.dataset.pane]; S.active = p.id;
  const row = e.target.closest('.row[data-row]');
  const c = Object.fromEntries(cmds().map(x => [x.id, x]));
  const item = (id, extra = {}) => ({ ...c[id], fn: () => run(id), ...extra });
  let items;
  if (row) {
    if (!p.sel.has(row.dataset.row)) { p.sel = new Set([row.dataset.row]); p.anchor = row.dataset.row; p.focus = row.dataset.row; }
    const one = p.sel.size === 1, n = selNodes(p)[0];
    items = [
      { label: n.type === 'dir' ? 'Open' : p.id === 'remote' ? 'Open (edit on the server)' : 'Open', icon: G.open, kbd: 'Enter', on: one, fn: () => openItem(p, n.name) },
      item('xfer'), '-', item('rename'), ...(p.id === 'remote' ? [item('perm')] : []), item('copypath'), '-', item('delete'),
    ];
  } else {
    p.sel.clear();
    items = [item('newfolder'), item('refresh'), item('hidden'), ...(p.id === 'remote' ? ['-', item('term')] : []), item('copypath')];
  }
  const r = win.getBoundingClientRect();
  S.menu = { x: Math.min(e.clientX - r.left, r.width - 240), y: Math.min(e.clientY - r.top, r.height - items.length * 34 - 16), items };
  render();
});

win.addEventListener('input', e => {
  const t = e.target;
  if (t.dataset.form) { S.form[t.dataset.form] = t.value; const btn = $('[data-act="add-server"]'); if (btn) btn.setAttribute('aria-disabled', t.dataset.form === 'host' && !t.value.trim() ? 'true' : 'false'); return; }
  if (t.dataset.filter) { S.panes[t.dataset.filter].filter = t.value; return render(); }
  if (t.id === 'rename-input') { S.renameValue = t.value; return; }
  if (t.dataset.path) { S.pathDraft = t.value; return; }
  if (t.id === 'palette-input') { S.palette.q = t.value; S.palette.idx = 0; return render(); }
});
win.addEventListener('change', e => {
  const t = e.target;
  if (t.dataset.perm) { S.dialog[t.dataset.perm] = t.value; return; }
  if (t.dataset.setting) { S.settings[t.dataset.setting] = t.dataset.setting === 'parallel' ? +t.value : t.value; return; }
});
win.addEventListener('focusout', e => {
  if (e.target.id === 'rename-input' && S.renaming) setTimeout(() => { if (document.activeElement?.id !== 'rename-input') commitRename(); }, 0);
  if (e.target.dataset.path) setTimeout(() => { const p = S.panes[e.target.dataset.path]; if (p && p.editPath && !document.activeElement?.dataset?.path) { p.editPath = false; render(); } }, 0);
});

document.addEventListener('keydown', e => {
  const t = e.target, ctrl = e.ctrlKey || e.metaKey;
  if (S.palette) {
    const n = paletteItems().length;
    if (e.key === 'Escape') { S.palette = null; return render(); }
    if (e.key === 'ArrowDown') { S.palette.idx = (S.palette.idx + 1) % Math.max(n, 1); e.preventDefault(); return render(); }
    if (e.key === 'ArrowUp') { S.palette.idx = (S.palette.idx - 1 + n) % Math.max(n, 1); e.preventDefault(); return render(); }
    if (e.key === 'Enter') { e.preventDefault(); return runPalette(S.palette.idx); }
    return;
  }
  if (S.dialog) {
    if (e.key === 'Escape') { if (S.dialog.kind === 'conflict') return act('conflict', 'cancel'); if (S.dialog.kind === 'hostkey') return act('hostkey-cancel'); S.dialog = null; return render(); }
    if (e.key === 'Enter' && S.dialog.kind === 'delete') return act('del-confirm');
    return;
  }
  if (S.menu && e.key === 'Escape') { S.menu = null; return render(); }
  if (t.id === 'rename-input') {
    if (e.key === 'Enter') { e.preventDefault(); return commitRename(); }
    if (e.key === 'Escape') { S.renaming = null; return render(); }
    return;
  }
  if (t.dataset && t.dataset.path) {
    const p = S.panes[t.dataset.path];
    if (e.key === 'Escape') { p.editPath = false; return render(); }
    if (e.key === 'Enter') {
      const raw = S.pathDraft.trim();
      const parts = p.id === 'local' ? raw.replace(/\\+$/, '').split('\\').filter(Boolean) : raw.split('/').filter(Boolean);
      if (walk(p.fs, parts) && walk(p.fs, parts).type === 'dir') navigate(p, parts); else { toast('err', 'Folder not found', `${raw} doesn’t exist on ${p.id === 'local' ? 'This PC' : srv().name}.`); p.editPath = false; }
      return render();
    }
    return;
  }
  if (t.dataset && t.dataset.filter) { if (e.key === 'Escape') { S.panes[t.dataset.filter].filter = ''; t.blur(); render(); } if (e.key !== 'ArrowDown') return; t.blur(); }
  if (t.dataset && t.dataset.form) { if (e.key === 'Enter') act('add-server'); return; }
  if (S.screen !== 'browser' || !S.server) return;
  const p = S.panes[S.active];
  if (ctrl && e.key.toLowerCase() === 'k') { e.preventDefault(); S.palette = { q: '', idx: 0 }; return render(); }
  if (ctrl && e.key.toLowerCase() === 'l') { e.preventDefault(); return run('goto'); }
  if (ctrl && e.key.toLowerCase() === 'f') { e.preventDefault(); return focusEl(`filter-${p.id}`, true); }
  if (ctrl && e.key.toLowerCase() === 'h') { e.preventDefault(); return run('hidden'); }
  if (ctrl && e.key === ',') { e.preventDefault(); return run('settings'); }
  if (ctrl && e.shiftKey && e.key.toLowerCase() === 'n') { e.preventDefault(); return run('newfolder'); }
  if (ctrl && e.key === 'Enter') { e.preventDefault(); return run('xfer'); }
  if (ctrl && e.key.toLowerCase() === 'a') { e.preventDefault(); p.sel = new Set(listAll(p).items.map(n => n.name)); return render(); }
  if (e.key === 'F5') { e.preventDefault(); return run('refresh'); }
  if (e.key === 'F2') { e.preventDefault(); return run('rename'); }
  if (e.key === 'Delete') { e.preventDefault(); return run('delete'); }
  if (e.key === 'Tab' && panesShown().length > 1) { e.preventDefault(); S.active = S.active === 'local' ? 'remote' : 'local'; render(); return document.querySelector(`[data-list="${S.active}"]`)?.focus(); }
  if (e.altKey && e.key === 'ArrowLeft') return act('back', null, p);
  if (e.altKey && e.key === 'ArrowRight') return act('fwd', null, p);
  if (e.key === 'Backspace' || (e.altKey && e.key === 'ArrowUp')) { e.preventDefault(); return act('up', null, p); }
  if (e.key === 'Escape') { p.sel.clear(); return render(); }
  if (e.key === 'Enter' && p.focus) return openItem(p, p.focus);
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Home' || e.key === 'End') {
    e.preventDefault();
    const names = listAll(p).items.map(n => n.name); if (!names.length) return;
    let i = names.indexOf(p.focus);
    i = e.key === 'Home' ? 0 : e.key === 'End' ? names.length - 1 : i < 0 ? 0 : Math.max(0, Math.min(names.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)));
    const name = names[i];
    if (e.shiftKey && p.anchor) { const [a, b] = [names.indexOf(p.anchor), i].sort((x, y) => x - y); p.sel = new Set(names.slice(a, b + 1)); }
    else if (!ctrl) { p.sel = new Set([name]); p.anchor = name; }
    p.focus = name; render();
    const el = document.querySelector(`[data-list="${p.id}"] .row.focus`); if (el) el.scrollIntoView({ block: 'nearest' });
    document.querySelector(`[data-list="${p.id}"]`)?.focus({ preventScroll: true });
  }
});

/* Drag and drop between panes */
win.addEventListener('dragstart', e => {
  const row = e.target.closest('.row[data-row]'); if (!row) return;
  const p = S.panes[row.closest('[data-pane]').dataset.pane];
  if (!p.sel.has(row.dataset.row)) { p.sel = new Set([row.dataset.row]); p.anchor = row.dataset.row; }
  S.drag = { from: p.id, names: [...p.sel] };
  e.dataTransfer.effectAllowed = 'copy';
  e.dataTransfer.setData('text/plain', S.drag.names.join('\n'));
  document.querySelectorAll(`[data-pane="${p.id}"] .row`).forEach(r => { if (p.sel.has(r.dataset.row)) r.classList.add('sel'); else r.classList.remove('sel'); });
});
win.addEventListener('dragover', e => {
  if (!S.drag) return;
  const pane = e.target.closest('section[data-pane]');
  document.querySelectorAll('.pane.drop-target').forEach(x => { if (x !== pane) x.classList.remove('drop-target'); });
  if (!pane || pane.dataset.pane === S.drag.from) return;
  e.preventDefault(); e.dataTransfer.dropEffect = 'copy';
  const to = S.panes[pane.dataset.pane];
  pane.dataset.drop = `${to.id === 'remote' ? 'Upload to' : 'Download to'} ${shortPath(to)}`;
  pane.classList.add('drop-target');
});
win.addEventListener('drop', e => {
  if (!S.drag) return;
  const pane = e.target.closest('section[data-pane]');
  document.querySelectorAll('.pane.drop-target').forEach(x => x.classList.remove('drop-target'));
  const d = S.drag; S.drag = null;
  if (!pane || pane.dataset.pane === d.from) return render();
  e.preventDefault();
  transfer(S.panes[d.from], d.names, S.panes[pane.dataset.pane]);
});
win.addEventListener('dragend', () => { document.querySelectorAll('.pane.drop-target').forEach(x => x.classList.remove('drop-target')); if (S.drag) { S.drag = null; render(); } });

$('#tweaks').addEventListener('click', e => {
  const b = e.target.closest('[data-tw]'); if (!b) return;
  if (b.dataset.tw === 'min') T.min = !T.min; else T[b.dataset.tw] = b.dataset.v;
  if (b.dataset.tw === 'layout') { S.queueOpen = false; S.menu = null; }
  saveTweaks(); render();
});

setInterval(() => { if (tick() && !S.drag && !S.renaming) render(); }, 250);
render();
