import { api, el } from './shared.js';
import { watchProject } from './project-events.js';

// Live work is independent of channel messages. This module only loads runs and draws their cards; where a card sits
// in the feed and how the feed scrolls belong to the channel's timeline. A card is created once per run and updated in
// place, so its expanded state and inner scroll survive every refresh.
export function createActivity({ projectId, root, nameOf, onChange }) {
  const cards = new Map();
  let hasActive = false;
  let statusKey = '';
  const status = el('div', 'agent-typing');
  status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite'); status.hidden = true;

  async function load(channelId) {
    return api(`${root}/channels/${channelId}/activity`);
  }

  function forget() {
    cards.clear(); statusKey = ''; hasActive = false; status.replaceChildren(); status.hidden = true;
  }

  function card(run) {
    let current = cards.get(run.id);
    if (!current) { current = createCard(run); cards.set(run.id, current); }
    update(current, run);
    return current.root;
  }

  function update(current, run) {
    const followThought = current.body.scrollHeight - current.body.scrollTop - current.body.clientHeight < 40;
    const last = run.steps.at(-1);
    current.who.textContent = nameOf(run.memberId);
    // Only what the agent reports doing; nothing is assumed when it reports nothing.
    current.state.textContent = run.active ? run.isTyping ? 'Typing' : last?.label || '' : stateName(run.state);
    current.root.classList.toggle('active', run.active);
    for (const step of run.steps) {
      // Writing is the public answer, shown once as a normal message. This view holds only the private prose voices.
      if (step.phase === 'reply') { continue; }
      let part = current.parts.get(step.ordinal);
      if (!part) {
        const section = el('section', 'agent-thought-part');
        const title = el('h4'); const text = el('div', 'agent-thought-text');
        section.append(title, text); current.body.append(section);
        part = { section, title, text }; current.parts.set(step.ordinal, part);
      }
      if (part.title.textContent !== step.label) { part.title.textContent = step.label; }
      if (part.text.textContent !== step.text) { part.text.textContent = step.text; }
      part.section.hidden = !step.text && step.isComplete;
      part.section.classList.toggle('in-progress', !step.isComplete);
    }
    if (run.active && followThought) { current.body.scrollTop = current.body.scrollHeight; }
  }

  // The line under the feed: who is working right now, one line per member.
  function showStatus(runs) {
    const active = new Map();
    for (const run of runs) {
      if (!run.active) { continue; }
      const previous = active.get(run.memberId);
      if (!previous || run.isTyping || run.startedAt > previous.startedAt) { active.set(run.memberId, run); }
    }
    hasActive = active.size > 0;
    const lines = [];
    for (const run of active.values()) {
      const phase = run.steps.at(-1)?.label;
      lines.push(run.isTyping ? `${nameOf(run.memberId)} is typing` : phase ? `${nameOf(run.memberId)}: ${phase}` : nameOf(run.memberId));
    }
    const key = lines.join('\n');
    if (statusKey !== key) {
      statusKey = key; status.replaceChildren();
      for (const text of lines) {
        const line = el('div', 'agent-typing-line');
        const dots = el('span', 'typing-dots'); dots.setAttribute('aria-hidden', 'true'); dots.textContent = '•••';
        line.append(dots, el('span', '', text)); status.append(line);
      }
    }
    status.hidden = !hasActive;
  }

  function createCard(run) {
    const root = el('article', 'agent-thoughts'); root.dataset.activityId = run.id; root.setAttribute('aria-live', 'off');
    if (run.packetId != null) { root.dataset.sourcePacketId = run.packetId; }
    const details = el('details'); // Thoughts start collapsed; the summary shows their progress.
    const summary = el('summary');
    const who = el('strong'); const label = el('span', 'muted'); const state = el('span', 'agent-thought-state');
    summary.append(who, label, state);
    const body = el('div', 'agent-thought-body'); body.setAttribute('aria-live', 'off'); details.append(summary, body); root.append(details);
    return { root, details, who, state, body, parts: new Map() };
  }

  const stopWatching = watchProject(projectId, 'activity', onChange, () => {
    status.hidden = true;
    for (const current of cards.values()) {
      if (current.root.classList.contains('active')) { current.state.textContent = 'Reconnecting'; }
    }
  });
  // Recheck expiry even if a stopped agent sends no final update.
  const timer = setInterval(() => { if (hasActive) { onChange(); } }, 10000);
  return { status, load, card, showStatus, forget, dispose() { stopWatching(); clearInterval(timer); } };
}

function stateName(state) {
  if (state === 'completed') { return 'Finished'; }
  if (state === 'failed') { return 'Could not finish'; }
  return 'Interrupted';
}
