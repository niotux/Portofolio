// Helpers for pages built with lcars.css. An ES module: import what a page needs.

// Builds an element. props: class, style (custom properties allowed), on<event> listeners, other attributes.
// Children are nodes or text; text is never parsed as HTML.
export function h(tag, props, ...children)
{
  const node = document.createElement(tag);
  if (props)
  {
    for (const [key, value] of Object.entries(props))
    {
      if (value == null || value === false)
      {
        continue;
      }
      if (key === 'class')
      {
        node.className = value;
      }
      else if (key === 'style')
      {
        for (const [property, propertyValue] of Object.entries(value))
        {
          node.style.setProperty(property, propertyValue);
        }
      }
      else if (key.startsWith('on'))
      {
        node.addEventListener(key.slice(2), value);
      }
      else
      {
        node.setAttribute(key, value === true ? '' : value);
      }
    }
  }
  for (const child of children.flat())
  {
    if (child != null && child !== false)
    {
      node.append(child instanceof Node ? child : String(child));
    }
  }
  return node;
}

// Copies text to the clipboard. The clipboard API needs a secure context (localhost is one, a LAN address is not), so
// a hidden text area is the fallback. Resolves to whether the copy happened.
export async function copyText(text)
{
  if (navigator.clipboard && window.isSecureContext)
  {
    try
    {
      await navigator.clipboard.writeText(text);
      return true;
    }
    catch
    {
    }
  }
  const area = h('textarea', { readonly: true, class: 'lcars-sr' });
  area.value = text;
  document.body.append(area);
  area.select();
  let copied = false;
  try
  {
    copied = document.execCommand('copy');
  }
  catch
  {
  }
  area.remove();
  return copied;
}

// Fetches JSON from url now and then every intervalMs, passing each result to onData and each failure to onError.
// A hidden tab is refreshed as soon as it is shown again. Returns a function that stops polling.
export function poll(url, intervalMs, onData, onError)
{
  let timer = 0;
  let stopped = false;

  async function tick()
  {
    clearTimeout(timer);
    try
    {
      const response = await fetch(url, { cache: 'no-store' });
      if (!response.ok)
      {
        throw new Error(`${response.status} ${response.statusText}`);
      }
      onData(await response.json());
    }
    catch (error)
    {
      if (onError)
      {
        onError(error);
      }
    }
    if (!stopped)
    {
      timer = setTimeout(tick, intervalMs);
    }
  }

  function onVisible()
  {
    if (document.visibilityState === 'visible' && !stopped)
    {
      tick();
    }
  }

  document.addEventListener('visibilitychange', onVisible);
  tick();
  return () =>
  {
    stopped = true;
    clearTimeout(timer);
    document.removeEventListener('visibilitychange', onVisible);
  };
}

// The date as year.day-of-year with local time, in the console's own style: 2026.312 14:05:09.
export function stardate(date = new Date())
{
  const start = new Date(date.getFullYear(), 0, 1);
  const day = Math.floor((date - start) / 86400000) + 1;
  const time = date.toLocaleTimeString('en-GB', { hour12: false });
  return `${date.getFullYear()}.${String(day).padStart(3, '0')} ${time}`;
}

// Keeps node's text set to the current stardate.
export function startClock(node)
{
  function update()
  {
    node.textContent = stardate();
  }

  update();
  return setInterval(update, 1000);
}

// Number display: rows of individually aligned four-digit cells filling the container. Values change and cells blink
// in discrete steps, and one line is always selected, moving to another line at random. Reduced motion stops both.
// The row count comes from the container's --lcars-number-rows when set (so a page can show fewer on small screens),
// otherwise options.rows (default 6). options.cellWidth in px (default 50), options.intervalMs (default 850).
// Returns { setHeld(held), reset(), stop() }.
export function mountNumberDisplay(container, options = {})
{
  let rows = 0;
  const cellWidth = options.cellWidth ?? 50;
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  let cells = [];
  let columns = 0;
  let tick = 0;
  let held = false;
  let selected = 0;
  let nextSelect = 0;

  function valueAt(index)
  {
    const value = Math.imul(index + 38, 2654435761) + Math.imul(tick + 1, 1013904223);
    return String((value >>> 0) % 10000).padStart(4, '0');
  }

  function select(row)
  {
    selected = row;
    for (let index = 0; index < cells.length; index++)
    {
      cells[index].classList.toggle('is-selected', Math.floor(index / columns) === row);
    }
  }

  function scheduleSelect()
  {
    nextSelect = tick + 3 + Math.floor(Math.random() * 6);
  }

  // Cells are rebuilt only when the column count changes; values then change in place.
  function size()
  {
    const styledRows = parseInt(getComputedStyle(container).getPropertyValue('--lcars-number-rows'), 10);
    const nextRows = styledRows > 0 ? styledRows : options.rows ?? 6;
    const next = Math.max(4, Math.floor((container.clientWidth + 6) / cellWidth));
    if (next === columns && nextRows === rows)
    {
      return;
    }
    columns = next;
    rows = nextRows;
    selected %= rows;
    container.style.setProperty('--lcars-number-columns', String(columns));
    cells = [];
    for (let index = 0; index < columns * rows; index++)
    {
      cells.push(h('span', {}, valueAt(index)));
    }
    container.replaceChildren(...cells);
    select(selected);
  }

  function step()
  {
    if (held || reducedMotion.matches || document.hidden)
    {
      return;
    }
    tick++;
    for (let index = 0; index < cells.length; index++)
    {
      const cell = cells[index];
      cell.classList.toggle('is-off', (index + tick * 3) % 29 === 0 && Math.floor(index / columns) !== selected);
      if ((index + tick) % 7 === 0)
      {
        cell.textContent = valueAt(index);
      }
    }
    if (tick >= nextSelect)
    {
      select((selected + 1 + Math.floor(Math.random() * (rows - 1))) % rows);
      scheduleSelect();
    }
  }

  function setHeld(value)
  {
    held = Boolean(value);
    if (held)
    {
      for (const cell of cells)
      {
        cell.classList.remove('is-off');
      }
    }
  }

  function reset()
  {
    tick = 0;
    for (let index = 0; index < cells.length; index++)
    {
      cells[index].classList.remove('is-off');
      cells[index].textContent = valueAt(index);
    }
    select(0);
    scheduleSelect();
  }

  container.classList.add('lcars-numbers');
  container.setAttribute('aria-hidden', 'true');
  const observer = new ResizeObserver(size);
  observer.observe(container);
  size();
  scheduleSelect();
  const timer = setInterval(step, options.intervalMs ?? 850);
  return {
    setHeld,
    reset,
    stop()
    {
      clearInterval(timer);
      observer.disconnect();
    }
  };
}

// Console sounds: short synthesized chirps, no recorded audio. A cue is a list of [frequency in Hz, seconds]; a zero
// frequency is a rest. Browsers start audio only after a user gesture, so the first cue should follow a click or key.
const SOUND_CUES =
{
  press: [[1568, 0.035], [2093, 0.045]],
  open: [[1047, 0.04], [1397, 0.04], [1865, 0.06]],
  close: [[1865, 0.04], [1397, 0.04], [1047, 0.06]],
  menu: [[1319, 0.05], [1760, 0.07]],
  alert: [[880, 0.11], [0, 0.04], [880, 0.11], [0, 0.04], [659, 0.16]],
  deny: [[392, 0.09], [330, 0.14]]
};
const SOUND_GAIN = 0.22;
const SOUND_EDGE = 0.006;
// Extra wait before leaving a page so the audio device's output latency does not cut off the cue's end.
const NAVIGATION_SOUND_MARGIN_MS = 60;

const SOUND_KEY = 'lcars.sound.v1';
const LEGACY_SOUND_KEY = 'astudio.sound.v1';
const SOUND_STATE = Symbol.for('lcars.sound.v1');
const sound = window[SOUND_STATE] ??= createSoundState();

function readSoundSetting()
{
  let saved = null;
  try
  {
    saved = JSON.parse(localStorage.getItem(SOUND_KEY) ?? localStorage.getItem(LEGACY_SOUND_KEY));
  }
  catch
  {
  }
  const volume = typeof saved?.volume === 'number' && Number.isFinite(saved.volume) ? Math.max(0, Math.min(1, saved.volume)) : 0.5;
  return { volume, muted: saved?.muted === true };
}

function createSoundState()
{
  const state = { setting: readSoundSetting(), context: null, output: null, listeners: new Set(), controls: new WeakMap(), gesture: false };
  document.addEventListener('pointerdown', event => { state.gesture ||= event.isTrusted; }, { capture: true });
  document.addEventListener('keydown', event => { state.gesture ||= event.isTrusted; }, { capture: true });
  window.addEventListener('storage', event =>
  {
    if (event.key === SOUND_KEY)
    {
      state.setting = readSoundSetting();
      updateSound(state);
    }
  });
  return state;
}

function updateSound(state = sound)
{
  if (state.output)
  {
    state.output.gain.value = state.setting.muted ? 0 : state.setting.volume * SOUND_GAIN;
  }
  for (const listener of state.listeners)
  {
    listener();
  }
}

function saveSoundSetting()
{
  try
  {
    localStorage.setItem(SOUND_KEY, JSON.stringify(sound.setting));
  }
  catch
  {
  }
  updateSound();
}

export function getSoundSetting()
{
  return { ...sound.setting };
}

// Volume from 0 to 1. Setting a positive volume also unmutes the console.
export function setSoundVolume(value)
{
  if (typeof value !== 'number' || !Number.isFinite(value))
  {
    return;
  }
  sound.setting.volume = Math.max(0, Math.min(1, value));
  if (sound.setting.volume > 0)
  {
    sound.setting.muted = false;
  }
  saveSoundSetting();
}

export function setSoundMuted(value)
{
  sound.setting.muted = Boolean(value);
  saveSoundSetting();
}

function soundOutput()
{
  if (!sound.gesture)
  {
    return null;
  }
  try
  {
    if (!sound.context)
    {
      const Context = window.AudioContext ?? window.webkitAudioContext;
      if (!Context)
      {
        return null;
      }
      sound.context = new Context();
      sound.output = sound.context.createGain();
      sound.output.connect(sound.context.destination);
      updateSound();
    }
    if (sound.context.state === 'suspended')
    {
      sound.context.resume().catch(() => {});
    }
    return sound.output;
  }
  catch
  {
    return null;
  }
}

// Plays a named cue and returns its length in milliseconds, or 0 when nothing plays. Audio is created only after a
// gesture; unsupported audio never interrupts an action.
export function playSound(name)
{
  const notes = SOUND_CUES[name];
  if (!notes || sound.setting.muted || sound.setting.volume === 0)
  {
    return 0;
  }
  const output = soundOutput();
  if (!output)
  {
    return 0;
  }
  try
  {
    let time = sound.context.currentTime;
    for (const [frequency, seconds] of notes)
    {
      if (frequency > 0)
      {
        const tone = sound.context.createOscillator();
        tone.type = 'sine';
        tone.frequency.setValueAtTime(frequency, time);
        const envelope = sound.context.createGain();
        envelope.gain.setValueAtTime(0, time);
        envelope.gain.linearRampToValueAtTime(1, time + SOUND_EDGE);
        envelope.gain.setValueAtTime(1, time + seconds - SOUND_EDGE);
        envelope.gain.linearRampToValueAtTime(0, time + seconds);
        tone.connect(envelope).connect(output);
        tone.onended = () => { tone.disconnect(); envelope.disconnect(); };
        tone.start(time);
        tone.stop(time + seconds + SOUND_EDGE);
      }
      time += seconds;
    }
    return Math.ceil((time - sound.context.currentTime) * 1000);
  }
  catch
  {
    return 0;
  }
}

// A plain click on a link that leaves the page: no modifier keys, same tab, not a download, not just a fragment change.
function leavesPage(event, control)
{
  if (control.tagName !== 'A' || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
  {
    return false;
  }
  if (control.hasAttribute('download') || (control.target && control.target !== '_self'))
  {
    return false;
  }
  const target = new URL(control.href, location.href);
  return target.origin + target.pathname + target.search !== location.origin + location.pathname + location.search;
}

// One delegated listener per caller. Exclude controls whose own action plays a more specific cue.
export function enableControlSounds(options = {})
{
  function click(event)
  {
    const control = event.target.closest?.('button, [role=tab], a[href], summary');
    if (!control || control.closest('.sound-control, [data-lcars-silent]') || (options.exclude && control.closest(options.exclude)))
    {
      return;
    }
    if (options.enabled && !options.enabled())
    {
      return;
    }
    const length = playSound('press');
    if (length > 0 && leavesPage(event, control))
    {
      // Navigation waits for the cue, unless the page handles the click itself. Without sound it is not delayed.
      window.addEventListener('click', after =>
      {
        if (after === event && !event.defaultPrevented)
        {
          event.preventDefault();
          setTimeout(() => location.assign(control.href), length + NAVIGATION_SOUND_MARGIN_MS);
        }
      }, { once: true });
    }
  }
  document.addEventListener('click', click, true);
  return () => document.removeEventListener('click', click, true);
}

// Reuses Holodeck's original direct button/input markup. Existing controls and their placement stay intact.
export function mountSoundControl(container, options = {})
{
  if (!container)
  {
    return () => {};
  }
  const mounted = sound.controls.get(container);
  if (mounted)
  {
    return mounted;
  }
  container.classList.add('sound-control');
  container.setAttribute('role', 'group');
  container.setAttribute('aria-label', 'Sound');
  let mute = container.querySelector(':scope > button');
  let volume = container.querySelector(':scope > input[type=range]');
  if (!mute)
  {
    mute = h('button', { type: 'button', 'aria-pressed': 'false', title: 'Mute sounds' }, 'Audio');
    container.prepend(mute);
  }
  if (!volume)
  {
    volume = h('input', { type: 'range', min: '0', max: '100', step: '5', 'aria-label': 'Volume' });
    container.append(volume);
  }
  if (options.muteId)
  {
    mute.id = options.muteId;
  }
  if (options.volumeId)
  {
    volume.id = options.volumeId;
  }

  function render()
  {
    const percent = Math.round(sound.setting.volume * 100);
    volume.value = String(percent);
    volume.setAttribute('aria-valuetext', `${percent} percent${sound.setting.muted ? ', muted' : ''}`);
    mute.textContent = sound.setting.muted ? 'Muted' : 'Audio';
    mute.setAttribute('aria-pressed', String(sound.setting.muted));
    mute.title = sound.setting.muted ? 'Turn sounds on' : 'Mute sounds';
  }

  function cue()
  {
    if (!options.enabled || options.enabled())
    {
      playSound('press');
    }
  }

  function input()
  {
    setSoundVolume(Number(volume.value) / 100);
  }

  function toggle()
  {
    setSoundMuted(!sound.setting.muted);
    cue();
  }

  volume.addEventListener('input', input);
  volume.addEventListener('change', cue);
  mute.addEventListener('click', toggle);
  sound.listeners.add(render);
  render();
  const dispose = () =>
  {
    sound.listeners.delete(render);
    sound.controls.delete(container);
    volume.removeEventListener('input', input);
    volume.removeEventListener('change', cue);
    mute.removeEventListener('click', toggle);
  };
  sound.controls.set(container, dispose);
  return dispose;
}
