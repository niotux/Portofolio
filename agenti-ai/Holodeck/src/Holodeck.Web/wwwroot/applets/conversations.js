import { downloadFile, copyWithNotice } from './viewer.js';
import { createActivity } from './activity.js';
import { watchProject } from './project-events.js';
import { api, json, el, button, field, ask, confirm, notify, listen, showOverlay, listBack } from './shared.js';

const HISTORY_PAGE = 100;

export const conversationsApplet = { id: 'conversations', title: 'Channels', icon: '☷', description: 'Messages, shared files, and conversation history.', projectScoped: true, size: { width: 960, height: 672 }, mount };

async function mount(ctx) {
  const projectId = ctx.state.projectId;
  if (!projectId) { ctx.body.replaceChildren(el('div', 'empty', 'Open a project from the main menu to see its channels.')); return; }
  const root = `api/projects/${projectId}/room`;
  const listing = await api('api/filesystem');
  if (ctx.isClosed()) { return; }
  const project = listing.projects.find(item => item.id === projectId);
  if (!project) { ctx.body.replaceChildren(el('div', 'empty', 'This project no longer exists.')); return; }
  let view = { members: [], channels: [] };
  // Older saved windows remember a wireId; migrated channels kept the wire's id.
  let channelId = ctx.state.channelId || ctx.state.wireId || null;
  let selectedKey = '';
  let sideKey = '';
  let headerKey = '';
  // The selected channel's timeline: its messages and agents' live work, drawn in one order. The token changes whenever
  // another channel is selected, so a load that finishes late never draws into the wrong channel.
  let token = 0;
  let packets = new Map();
  let runs = [];
  let lastId = 0;
  const bubbles = new Map();
  // The feed follows new content only while the reader is at its end; scrolling up stops that until they return.
  let stick = true;
  let sending = Promise.resolve();
  let filter = '';
  const drafts = new Map();
  // Phone: the chosen channel replaces the list until its back pill is pressed.
  let detailShown = false;
  const host = el('div', 'room conversations-app');
  const side = el('aside', 'conversation-sidebar'); side.setAttribute('aria-label', 'Channels');
  const main = el('section', 'room-main'); main.setAttribute('aria-label', 'Selected channel');
  const header = el('header', 'chat-header');
  const feed = el('div', 'feed'); feed.setAttribute('role', 'log'); feed.setAttribute('aria-live', 'polite');
  const composerSlot = el('div');
  host.append(side, main); ctx.body.replaceChildren(host);

  const member = id => view.members.find(item => item.id === id);
  const you = () => view.members.find(item => item.isYou);
  const nameOf = id => { const item = member(id); return item ? `${item.name}${item.isRemoved ? ' (removed)' : ''}` : 'Unknown'; };
  const channel = () => view.channels.find(item => item.id === channelId);
  const includes = (item, memberId) => Boolean(item && memberId && item.memberIds.includes(memberId));
  const isMine = item => includes(item, you()?.id);
  const activity = createActivity({ projectId, root, nameOf, onChange: () => syncRuns() });
  ctx.dispose(() => activity.dispose());
  feed.addEventListener('scroll', () => { stick = atBottom(); });
  function saveState() { ctx.setState({ channelId, wireId: null }); }
  function openTeam(state = {}) { ctx.launch('room', { projectId, ...state }); }
  function select(id) {
    channelId = id; detailShown = Boolean(id);
    saveState(); renderSide(); renderMain();
  }
  // Each kind of data has one loader. Asking while it runs makes it run once more afterwards, never twice at once.
  const refreshView = coalesce(async () => {
    view = await api(root);
    if (ctx.isClosed()) { return; }
    if (channelId && !channel()) { channelId = null; }
    saveState(); renderSide(); renderMain();
  });
  const syncPackets = coalesce(async () => {
    const current = channel(); if (!current || ctx.isClosed()) { return; }
    const own = token;
    while (true) {
      const page = await api(`${root}/channels/${current.id}/packets?after=${lastId}`);
      if (own !== token || ctx.isClosed()) { return; }
      for (const packet of page) { lastId = Math.max(lastId, packet.id); packets.set(packet.id, packet); }
      if (page.length < HISTORY_PAGE) { break; }
    }
    renderFeed();
  });
  const syncRuns = coalesce(async () => {
    const current = channel(); if (!current || ctx.isClosed()) { return; }
    const own = token;
    const loaded = await activity.load(current.id);
    if (own !== token || ctx.isClosed()) { return; }
    runs = loaded; renderFeed(); activity.showStatus(runs);
  });
  function refresh() { const loading = refreshView(); syncPackets(); return loading; }
  async function change(path, options) { await api(`${root}/channels${path}`, options); await refreshView(); syncPackets(); }
  function atBottom() { return feed.scrollHeight - feed.scrollTop - feed.clientHeight < 80; }
  // Messages in the order they were sent; an agent's work right after the message it answers, or else at the moment it
  // began. The feed's children are put in exactly that order, moving only the nodes that are out of place.
  function renderFeed() {
    const items = [];
    for (const packet of packets.values()) { items.push({ key: [Date.parse(packet.sentAt), 0, packet.id, 0, 0], node: packetNode(packet) }); }
    for (const run of runs) {
      const anchor = run.packetId == null ? null : packets.get(run.packetId);
      const started = Date.parse(run.startedAt);
      const key = anchor ? [Date.parse(anchor.sentAt), 0, anchor.id, 1, started] : [started, 1, 0, 1, started];
      items.push({ key, node: activity.card(run) });
    }
    items.sort((a, b) => compareKeys(a.key, b.key));
    let cursor = feed.firstChild;
    for (const item of items) {
      if (item.node === cursor) { cursor = cursor.nextSibling; } else { feed.insertBefore(item.node, cursor); }
    }
    while (cursor) { const next = cursor.nextSibling; cursor.remove(); cursor = next; }
    if (stick) { feed.scrollTop = feed.scrollHeight; }
  }
  function packetNode(packet) {
    let node = bubbles.get(packet.id);
    if (!node) { node = bubble(packet); bubbles.set(packet.id, node); }
    return node;
  }
  function renderSide() {
    const key = JSON.stringify(view);
    if (sideKey === key) { markSelection(); return; }
    sideKey = key;
    const scroll = side.querySelector('.conversation-list')?.scrollTop || 0;
    const hadSearchFocus = side.querySelector('input') === document.activeElement;
    const searchStart = side.querySelector('input')?.selectionStart;
    const heading = el('div', 'conversation-side-heading');
    const create = button('New', () => openCreate(), 'small primary channel-new'); create.setAttribute('aria-label', 'New channel');
    heading.append(el('h3', '', 'Open channels'), create);
    const search = el('input', 'conversation-search'); search.type = 'search'; search.placeholder = 'Find a channel'; search.setAttribute('aria-label', 'Find a channel'); search.value = filter;
    search.addEventListener('input', () => { filter = search.value; filterRows(); });
    const list = el('div', 'conversation-list');
    for (const current of view.channels) {
      const entry = button('', () => select(current.id), 'conversation-entry'); entry.dataset.channelId = current.id; entry.dataset.name = current.name.toLowerCase();
      entry.append(el('strong', '', current.name), el('small', 'muted', lastLine(current)));
      if (current.isPaused) { entry.append(el('small', 'conversation-paused', 'Paused')); }
      list.append(entry);
    }
    if (!view.channels.length) { list.append(el('p', 'muted', 'No channels yet. Create one and add the team.')); }
    side.replaceChildren(heading, search, list); list.scrollTop = scroll;
    filterRows(); markSelection();
    if (hadSearchFocus) { search.focus(); if (searchStart != null) { search.setSelectionRange(searchStart, searchStart); } }
  }
  function filterRows() {
    for (const entry of side.querySelectorAll('.conversation-entry')) { entry.hidden = !entry.dataset.name.includes(filter.toLowerCase()); }
  }
  function markSelection() {
    for (const entry of side.querySelectorAll('.conversation-entry')) {
      entry.setAttribute('aria-pressed', String(entry.dataset.channelId === channelId));
    }
  }
  function renderMain() {
    const current = channel();
    host.classList.toggle('has-selection', detailShown && Boolean(current));
    if (!current) {
      if (selectedKey !== 'empty') {
        selectedKey = 'empty'; headerKey = ''; clearTimeline();
        const empty = el('div', 'feed-empty');
        empty.append(el('h2', '', 'Channels'), el('p', '', 'Choose a channel to read it, or create one for the team.'), button('+ Channel', () => openCreate(), 'primary'));
        main.replaceChildren(empty);
      }
      return;
    }
    const switched = selectedKey !== current.id;
    if (switched) {
      selectedKey = current.id; headerKey = ''; clearTimeline();
      main.replaceChildren(header, feed, activity.status, composerSlot);
    }
    renderHeader(); renderComposer();
    if (switched) { syncPackets(); syncRuns(); }
  }
  function clearTimeline() {
    token++; packets = new Map(); runs = []; lastId = 0; bubbles.clear(); stick = true;
    activity.forget(); feed.replaceChildren();
  }
  function renderHeader() {
    const current = channel();
    const key = JSON.stringify([current, view.members]);
    if (headerKey === key) { return; }
    headerKey = key;
    const heading = el('div', 'chat-heading');
    heading.append(el('h3', 'chat-title', current.name));
    const status = current.isPaused ? 'Paused: agents cannot post here' : isMine(current) ? 'Messages and files' : 'Observing';
    heading.append(el('small', 'muted', status));
    const tools = el('div', 'conversation-header-actions');
    const pause = button(current.isPaused ? 'Resume' : 'Pause', () => change(`/${current.id}`, json('PUT', { isPaused: !current.isPaused })), 'small');
    tools.append(pause, button('Rename', async () => {
      const name = await ask(ctx, 'Rename channel', 'Channel name', current.name); if (!name) { return; }
      await change(`/${current.id}`, json('PUT', { name }));
    }, 'small'), button('Delete', async () => {
      if (!await confirm(ctx, `Delete ${current.name}?`, 'Its messages are deleted. Files already on agents\u2019 tables stay.')) { return; }
      await change(`/${current.id}`, { method: 'DELETE' });
    }, 'small danger'));
    const members = el('div', 'channel-members'); members.setAttribute('aria-label', 'Members');
    for (const id of current.memberIds) {
      const person = member(id); if (!person) { continue; }
      const chip = el('span', 'channel-member');
      if (person.isYou) { chip.append(el('span', '', 'You')); }
      else { const open = button(person.name, () => openTeam({ memberId: id, agentTab: 'overview' }), 'small channel-member-name'); open.setAttribute('aria-label', `Open agent ${person.name}`); chip.append(open); }
      const remove = button('×', () => change(`/${current.id}/members/${id}`, { method: 'DELETE' }), 'small'); remove.setAttribute('aria-label', `Remove ${person.name} from ${current.name}`);
      chip.append(remove); members.append(chip);
    }
    const outside = view.members.filter(item => !item.isRemoved && !current.memberIds.includes(item.id));
    if (outside.length) {
      const picker = el('select', 'channel-add'); picker.setAttribute('aria-label', 'Add member');
      picker.append(new Option('Add member…', ''));
      for (const item of outside) { picker.append(new Option(item.isYou ? 'You' : item.name, item.id)); }
      picker.addEventListener('change', () => { const id = picker.value; if (id) { change(`/${current.id}/members`, json('POST', { memberId: id })).catch(error => notify(error.message, true)); } });
      members.append(picker);
    }
    if (!isMine(current)) { tools.prepend(button('Join', () => change(`/${current.id}/members`, json('POST', { memberId: you().id })), 'small primary')); }
    header.replaceChildren(listBack('Channels', () => { detailShown = false; host.classList.remove('has-selection'); }), heading, tools, members);
  }
  function openCreate() {
    const overlay = el('div', 'window-overlay'); const form = el('form', 'dialog-form lcars-dialog channel-form'); form.setAttribute('aria-label', 'New channel');
    const name = el('input'); name.required = true; name.maxLength = 80;
    const choices = el('fieldset', 'channel-choices'); choices.append(el('legend', '', 'Members'));
    for (const item of view.members.filter(entry => !entry.isRemoved)) {
      const label = el('label', 'check-row'); const box = el('input'); box.type = 'checkbox'; box.value = item.id; box.checked = item.isYou;
      label.append(box, el('span', '', item.isYou ? 'You' : item.name)); choices.append(label);
    }
    const actions = el('div', 'actions'); const submit = el('button', 'primary', 'Create'); submit.type = 'submit';
    const close = () => overlay.remove();
    actions.append(button('Cancel', close), submit);
    form.append(el('h3', '', 'New channel'), field('Channel name', name), choices, actions); overlay.append(form); showOverlay(overlay); name.focus();
    form.addEventListener('submit', async event => {
      event.preventDefault();
      const memberIds = [...choices.querySelectorAll('input:checked')].map(box => box.value);
      try {
        const created = await api(`${root}/channels`, json('POST', { name: name.value.trim(), memberIds }));
        close(); await refreshView(); select(created.id);
      } catch (error) { notify(error.message, true); }
    });
    overlay.addEventListener('keydown', event => { if (event.key === 'Escape') { close(); } });
    ctx.dispose(close);
  }
  function bubble(packet) {
    const mine = packet.senderId === you()?.id;
    const node = el('article', `bubble${mine ? ' mine' : ''}`); node.dataset.packetId = packet.id;
    node.style.setProperty('--tag', tagColor(packet.senderId));
    const meta = el('div', 'bubble-meta'); meta.append(el('strong', '', nameOf(packet.senderId)), el('time', 'muted', shortTime(packet.sentAt)));
    node.append(meta);
    if (packet.note) { node.append(el('div', 'bubble-text', packet.note)); }
    if (packet.files.length) {
      const files = el('div', 'bubble-files');
      for (const file of packet.files) {
        const href = `${root}/packets/${packet.id}/files/${encodeURIComponent(file.name)}`;
        const open = () => ctx.launch('viewer', { url: href, name: file.name, mediaType: file.mediaType });
        const item = el('div', 'file-item');
        if (file.mediaType.startsWith('image/')) {
          const preview = button('', open, 'bubble-image'); preview.setAttribute('aria-label', `Open ${file.name}`);
          const image = el('img'); image.src = href; image.alt = file.name; image.loading = 'lazy'; preview.append(image); item.append(preview);
        }
        const chip = button(file.name, open, 'file-chip'); chip.title = `Open ${file.name}`;
        const actions = el('span', 'file-actions');
        actions.append(button('Download', () => downloadFile(href, file.name), 'small file-download'), button('Copy', () => copyWithNotice(href, file.mediaType), 'small file-copy'));
        item.append(chip, el('small', 'muted file-size', size(file.size)), actions);
        files.append(item);
      }
      node.append(files);
    }
    return node;
  }

  function renderComposer() {
    const current = channel();
    if (!isMine(current)) { composerSlot.replaceChildren(); return; }
    if (composerSlot.firstChild?.dataset.channelId === current.id) { return; }
    let draft = drafts.get(current.id);
    if (!draft) { draft = { note: '', files: [] }; drafts.set(current.id, draft); }
    const pending = draft.files;
    const form = el('form', 'composer'); form.dataset.channelId = current.id;
    const text = el('textarea'); text.rows = 2; text.placeholder = `Message ${current.name}`; text.setAttribute('aria-label', 'Message');
    text.value = draft.note;
    text.addEventListener('input', () => { draft.note = text.value; });
    const attached = el('div', 'attachments');
    const picker = el('input'); picker.type = 'file'; picker.multiple = true; picker.hidden = true;
    const attach = button('Attach', () => picker.click(), 'small'); attach.setAttribute('aria-label', 'Attach files');
    const send = el('button', 'primary', 'Send'); send.type = 'submit';
    const actions = el('div', 'composer-actions'); actions.append(attach, send);
    form.append(text, attached, actions, picker);
    function showAttachments() {
      attached.replaceChildren();
      pending.forEach((file, index) => {
        const chip = el('span', 'attachment', file.name);
        const remove = button('×', () => { pending.splice(index, 1); showAttachments(); }, 'small'); remove.setAttribute('aria-label', `Remove ${file.name}`);
        chip.append(remove); attached.append(chip);
      });
    }
    picker.addEventListener('change', () => { pending.push(...picker.files); picker.value = ''; showAttachments(); });
    function submit() {
      const note = text.value.trim(); const files = pending.splice(0);
      if (!note && !files.length) { return; }
      text.value = ''; draft.note = ''; showAttachments();
      const target = current.id;
      // Sends go out one after another so quick messages keep their order.
      sending = sending.then(async () => {
        const body = new FormData(); body.append('note', note);
        for (const file of files) { body.append('files', file, file.name); }
        await api(`${root}/channels/${target}/packets`, { method: 'POST', body });
        if (channelId === target) { stick = true; }
        await syncPackets(); refreshView();
      }).catch(error => {
        notify(error.message, true);
        if (!draft.note) { draft.note = note; }
        pending.unshift(...files);
        if (text.isConnected) { text.value = draft.note; showAttachments(); }
      });
    }
    form.addEventListener('submit', event => { event.preventDefault(); submit(); });
    text.addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); submit(); } });
    showAttachments(); composerSlot.replaceChildren(form);
  }


  ctx.dispose(watchProject(projectId, 'room', () => refresh()));
  listen(ctx, event => {
    if (event.detail.deletedProjectId === projectId) { ctx.close(); return; }
    if (event.detail.projectId === projectId) { refresh(); }
  });
  ctx.onNavigate(async state => {
    if (state.channelId) { channelId = state.channelId; }
    await refreshView();
    if (state.channelId && channel()) { select(state.channelId); }
  });
  await refreshView();
}

// Runs an async loader so that calls while it runs ask for exactly one more run afterwards.
function coalesce(load) {
  let running = null;
  let again = false;
  return function run() {
    if (running) { again = true; return running; }
    running = (async () => {
      do { again = false; await load(); } while (again);
    })().catch(error => notify(error.message, true)).finally(() => { running = null; });
    return running;
  };
}
function compareKeys(a, b) {
  for (let index = 0; index < a.length; index++) {
    if (a[index] !== b[index]) { return a[index] < b[index] ? -1 : 1; }
  }
  return 0;
}

function lastLine(item) {
  if (!item.lastNote) { return item.packetCount ? 'Sent a file' : 'No messages yet'; }
  const line = item.lastNote.split('\n')[0];
  return line.length > 80 ? `${line.slice(0, 80)}…` : line;
}
function tagColor(id) {
  let hash = 0;
  for (const character of id) { hash = (hash * 31 + character.charCodeAt(0)) % 360; }
  return `hsl(${hash} 55% 55%)`;
}
function shortTime(value) {
  const date = new Date(value); const today = new Date();
  const sameDay = date.toDateString() === today.toDateString();
  return sameDay ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : date.toLocaleDateString([], { month: 'short', day: 'numeric' });
}
function size(bytes) {
  if (bytes < 1024) { return `${bytes} B`; }
  if (bytes < 1024 * 1024) { return `${Math.round(bytes / 1024)} KB`; }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
