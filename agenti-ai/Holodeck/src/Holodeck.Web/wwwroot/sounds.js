// The LCARS library owns sound preferences and the original Audio/Muted button plus native slider.
// The front server's copy comes first; Holodeck's bundled copy stands in when the front server is unreachable.
const LIBRARY_PATH = '/lcars/lcars.js?v=20261109-shared-volume';
const FALLBACK_PATH = new URL('./lcars/lcars.js?v=20261109-shared-volume', import.meta.url).href;
let library = null;
let loading = null;
let active = false;

export function soundSetting() { return library?.getSoundSetting() ?? { volume: 0.5, muted: false }; }
export function setVolume(volume) { library?.setSoundVolume(volume); }
export function setMuted(muted) { library?.setSoundMuted(muted); }

// Sound failures must never interrupt project entry, opening a window or returning to the main menu.
export function cue(name)
{
  if (!active || !library)
  {
    return;
  }
  try
  {
    library.playSound(name);
  }
  catch
  {
  }
}

export async function setSoundsActive(enabled)
{
  active = enabled;
  if (!enabled || library)
  {
    return;
  }
  if (!loading)
  {
    loading = import(`${location.protocol}//${location.hostname}${LIBRARY_PATH}`).catch(() => import(FALLBACK_PATH)).then(module =>
    {
      if (typeof module.mountSoundControl !== 'function' || typeof module.enableControlSounds !== 'function')
      {
        throw new Error('The shared LCARS sound controls are unavailable');
      }
      module.mountSoundControl(document.querySelector('.taskbar > .sound-control'), { muteId: 'sound-mute', volumeId: 'sound-volume', enabled: () => active });
      module.mountSoundControl(document.querySelector('#menu-sound-control'), { muteId: 'menu-sound-mute', volumeId: 'menu-sound-volume', enabled: () => active });
      module.enableControlSounds({ enabled: () => active, exclude: '.window-controls, #start, .task, .sound-control' });
      library = module;
    }).catch(() => { library = null; loading = null; });
  }
  await loading;
}
