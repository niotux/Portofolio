using System.Runtime.InteropServices;
using QuantiX.Engine.Audio;

namespace QuantiX.Engine.Tests.Rendering;

/// <summary>
/// Procedural ambient music inspired by Minecraft's calm soundtrack.
/// Generates multiple long themes (piano, pads, plucks) that rotate
/// with silence gaps between tracks, creating a peaceful exploration atmosphere.
/// </summary>
public class MusicSource : IAudioSource
{
  private readonly AudioFormat _format;
  private AudioState _state = AudioState.Paused;
  private long _sample;

  // Theme management
  private int _currentTheme;
  private long _themeSampleStart;
  private bool _inSilence;
  private long _silenceEnd;
  private readonly Random _rng = new(42);

  private const int ThemeCount = 5;

  public MusicSource(AudioFormat format)
  {
    _format = format;
    _currentTheme = 0;
    _inSilence = false;
  }

  public AudioFormat Format => _format;
  public AudioState State => _state;
  public void Play() => _state = AudioState.Playing;
  public void Pause() => _state = AudioState.Paused;
  public void Reset() { _sample = 0; _state = AudioState.Paused; }

  // ── Theme durations (in seconds) ──
  private static readonly double[] ThemeDurations = [180, 210, 160, 240, 190]; // 2.5-4 minutes each
  private const double MinSilence = 15.0;
  private const double MaxSilence = 45.0;

  // ── Musical scales (Hz) ──

  // Theme 0: "Meadow" - Calm piano in C major (like Sweden/Minecraft)
  private static readonly double[] MeadowNotes = [261.63, 293.66, 329.63, 349.23, 392.00, 440.00, 493.88, 523.25];
  private static readonly int[] MeadowMelody = [0, 2, 4, 2, 3, 4, 5, 4, 2, 0, 2, 0, -1, 0, 2, 4, 5, 4, 2, 3, 2, 0, -1, -1];
  private static readonly double[] MeadowChords = [261.63, 329.63, 392.00]; // C major

  // Theme 1: "Twilight" - Melancholic Am pads (like Wet Hands)
  private static readonly double[] TwilightNotes = [220.00, 246.94, 261.63, 293.66, 329.63, 349.23, 392.00, 440.00];
  private static readonly int[] TwilightMelody = [4, 3, 2, 0, -1, 2, 3, 4, 5, 4, 3, 2, 0, -1, -1, 0, 2, 4, 5, 7, 5, 4, 2, 0];
  private static readonly double[] TwilightChords = [220.00, 261.63, 329.63]; // Am

  // Theme 2: "Cavern" - Deep ambient drone in D minor
  private static readonly double[] CavernNotes = [146.83, 164.81, 174.61, 196.00, 220.00, 233.08, 261.63, 293.66];
  private static readonly int[] CavernMelody = [0, -1, -1, 2, -1, 3, 2, 0, -1, -1, -1, 4, 3, 2, -1, 0, -1, -1, 2, 4, 3, -1, -1, 0];
  private static readonly double[] CavernChords = [146.83, 174.61, 220.00]; // Dm

  // Theme 3: "Sunrise" - Hopeful G major arpeggios (like Haggstrom)
  private static readonly double[] SunriseNotes = [196.00, 220.00, 246.94, 261.63, 293.66, 329.63, 349.23, 392.00];
  private static readonly int[] SunriseMelody = [0, 2, 4, 7, 5, 4, 2, 0, 2, 4, 5, 4, 2, 0, -1, 0, 4, 5, 7, 5, 4, 2, 0, -1];
  private static readonly double[] SunriseChords = [196.00, 246.94, 293.66]; // G major

  // Theme 4: "Starlight" - Ethereal E minor (like Living Mice)
  private static readonly double[] StarlightNotes = [164.81, 185.00, 196.00, 220.00, 246.94, 261.63, 293.66, 329.63];
  private static readonly int[] StarlightMelody = [0, 2, 4, 5, 4, 2, 0, -1, 2, 4, 5, 7, 5, 4, 2, 0, -1, -1, 0, 4, 7, 5, 4, 2];
  private static readonly double[] StarlightChords = [164.81, 196.00, 246.94]; // Em

  public int Read(Span<byte> buffer)
  {
    if (_state == AudioState.Paused)
    {
      buffer.Clear();
      return buffer.Length;
    }

    Span<short> samples = MemoryMarshal.Cast<byte, short>(buffer);
    int frames = samples.Length / _format.Channels;
    double sr = _format.SampleRate;

    for (int i = 0; i < frames; i++)
    {
      long globalSample = _sample + i;

      // Handle silence gaps between themes
      if (_inSilence)
      {
        if (globalSample >= _silenceEnd)
        {
          _inSilence = false;
          _currentTheme = (_currentTheme + 1) % ThemeCount;
          _themeSampleStart = globalSample;
        }
        else
        {
          for (int ch = 0; ch < _format.Channels; ch++)
            samples[i * _format.Channels + ch] = 0;
          continue;
        }
      }

      double themeTime = (globalSample - _themeSampleStart) / sr;
      double themeDuration = ThemeDurations[_currentTheme];

      // Fade in/out envelope for the entire theme
      double themeFadeIn = Math.Min(themeTime / 8.0, 1.0); // 8 second fade in
      double themeFadeOut = Math.Min((themeDuration - themeTime) / 10.0, 1.0); // 10 second fade out
      double themeEnv = Math.Max(0, themeFadeIn * themeFadeOut);

      if (themeTime >= themeDuration)
      {
        // Enter silence gap
        _inSilence = true;
        double silenceDur = MinSilence + _rng.NextDouble() * (MaxSilence - MinSilence);
        _silenceEnd = globalSample + (long)(silenceDur * sr);
        for (int ch = 0; ch < _format.Channels; ch++)
          samples[i * _format.Channels + ch] = 0;
        continue;
      }

      double mix = _currentTheme switch
      {
        0 => GenerateMeadow(themeTime, sr, globalSample),
        1 => GenerateTwilight(themeTime, sr, globalSample),
        2 => GenerateCavern(themeTime, sr, globalSample),
        3 => GenerateSunrise(themeTime, sr, globalSample),
        4 => GenerateStarlight(themeTime, sr, globalSample),
        _ => 0
      };

      mix *= themeEnv;
      mix = Math.Clamp(mix, -0.9, 0.9);
      short s = (short)(mix * 14000);

      for (int ch = 0; ch < _format.Channels; ch++)
        samples[i * _format.Channels + ch] = s;
    }

    _sample += frames;
    return buffer.Length;
  }

  // ── Theme 0: Meadow - Gentle piano with soft pads ──

  private static double GenerateMeadow(double t, double sr, long sample)
  {
    double mix = 0;
    const double bpm = 72.0;
    double beatSec = 60.0 / bpm;
    double noteLen = beatSec * 2; // half notes
    int noteIndex = (int)(t / noteLen);
    double noteFrac = (t % noteLen) / noteLen;

    // Piano melody - soft sine with harmonics
    int melIdx = MeadowMelody[noteIndex % MeadowMelody.Length];
    if (melIdx >= 0)
    {
      double freq = MeadowNotes[melIdx];
      double env = PianoEnvelope(noteFrac);
      double tone = SoftPiano(t, freq);
      mix += tone * env * 0.25;
    }

    // Sustained pad chord
    double padVol = 0.04;
    for (int n = 0; n < MeadowChords.Length; n++)
    {
      double padFreq = MeadowChords[n] * 0.5; // octave down
      mix += Math.Sin(2 * Math.PI * padFreq * t + n * 0.7) * padVol;
      // Add slow vibrato
      mix += Math.Sin(2 * Math.PI * padFreq * 1.002 * t + n * 1.1) * padVol * 0.5;
    }

    // Occasional high bell-like notes (every 8 beats)
    double bellPeriod = beatSec * 8;
    double bellFrac = (t % bellPeriod) / bellPeriod;
    if (bellFrac < 0.3)
    {
      int bellNote = (noteIndex / 4) % MeadowNotes.Length;
      double bellFreq = MeadowNotes[bellNote] * 2; // octave up
      double bellEnv = Math.Exp(-bellFrac * 12.0);
      mix += Math.Sin(2 * Math.PI * bellFreq * t) * bellEnv * 0.06;
    }

    return mix;
  }

  // ── Theme 1: Twilight - Melancholic with reverb-like delays ──

  private static double GenerateTwilight(double t, double sr, long sample)
  {
    double mix = 0;
    const double bpm = 60.0;
    double beatSec = 60.0 / bpm;
    double noteLen = beatSec * 1.5; // dotted quarter
    int noteIndex = (int)(t / noteLen);
    double noteFrac = (t % noteLen) / noteLen;

    // Main melody
    int melIdx = TwilightMelody[noteIndex % TwilightMelody.Length];
    if (melIdx >= 0)
    {
      double freq = TwilightNotes[melIdx];
      double env = PianoEnvelope(noteFrac);
      double tone = SoftPiano(t, freq);
      mix += tone * env * 0.22;

      // Simulated delay echo (one beat later, quieter)
      double echoTime = t - beatSec;
      if (echoTime > 0)
      {
        double echoFrac = (echoTime % noteLen) / noteLen;
        double echoEnv = PianoEnvelope(echoFrac) * 0.3;
        mix += SoftPiano(echoTime, freq) * echoEnv * 0.15;
      }
    }

    // Warm pad
    double padVol = 0.05;
    for (int n = 0; n < TwilightChords.Length; n++)
    {
      double padFreq = TwilightChords[n] * 0.5;
      double phase = 2 * Math.PI * padFreq * t;
      // Triangle wave for warmth
      double tri = 2.0 * Math.Abs(2.0 * ((padFreq * t) % 1.0) - 1.0) - 1.0;
      mix += (Math.Sin(phase) * 0.7 + tri * 0.3) * padVol;
    }

    // Slow breathing texture
    double breathCycle = Math.Sin(2 * Math.PI * t / 12.0); // 12-second cycle
    mix += breathCycle * 0.02;

    return mix;
  }

  // ── Theme 2: Cavern - Deep drones and sparse drops ──

  private static double GenerateCavern(double t, double sr, long sample)
  {
    double mix = 0;
    const double bpm = 50.0;
    double beatSec = 60.0 / bpm;
    double noteLen = beatSec * 3; // slow, spacious
    int noteIndex = (int)(t / noteLen);
    double noteFrac = (t % noteLen) / noteLen;

    // Deep drone base
    double droneFreq = CavernChords[0] * 0.25; // very low
    mix += Math.Sin(2 * Math.PI * droneFreq * t) * 0.08;
    mix += Math.Sin(2 * Math.PI * droneFreq * 1.5 * t) * 0.04; // fifth

    // Sparse melodic drops
    int melIdx = CavernMelody[noteIndex % CavernMelody.Length];
    if (melIdx >= 0)
    {
      double freq = CavernNotes[melIdx];
      // Long decay envelope
      double env = Math.Exp(-noteFrac * 3.0);
      double tone = Math.Sin(2 * Math.PI * freq * t);
      // Add slight detuned copy for depth
      tone += Math.Sin(2 * Math.PI * freq * 1.003 * t) * 0.5;
      mix += tone * env * 0.12;
    }

    // Eerie harmonics that drift in and out
    double drift = Math.Sin(2 * Math.PI * t / 20.0); // 20-second cycle
    double eerieFreq = CavernNotes[4] * (1.0 + drift * 0.01);
    mix += Math.Sin(2 * Math.PI * eerieFreq * t) * (0.5 + drift * 0.5) * 0.03;

    // Water drip sound (random-ish, using deterministic hash)
    double dripPeriod = 4.7; // slightly irregular period
    double dripPhase = (t % dripPeriod) / dripPeriod;
    if (dripPhase < 0.005)
    {
      double dripFreq = 1200 + 400 * Math.Sin(t * 0.3);
      double dripEnv = 1.0 - dripPhase / 0.005;
      mix += Math.Sin(2 * Math.PI * dripFreq * t) * dripEnv * 0.04;
    }

    return mix;
  }

  // ── Theme 3: Sunrise - Hopeful arpeggiated chords ──

  private static double GenerateSunrise(double t, double sr, long sample)
  {
    double mix = 0;
    const double bpm = 80.0;
    double beatSec = 60.0 / bpm;

    // Arpeggiated chord pattern - each chord note played in sequence
    double arpNoteLen = beatSec * 0.75; // triplet feel
    int arpIndex = (int)(t / arpNoteLen);
    double arpFrac = (t % arpNoteLen) / arpNoteLen;

    // Cycle through chord tones with extensions
    double[] arpPattern = [
      SunriseChords[0], SunriseChords[1], SunriseChords[2],
      SunriseChords[1] * 2, SunriseChords[2], SunriseChords[1],
      SunriseChords[0], SunriseChords[2] * 0.5
    ];

    double arpFreq = arpPattern[arpIndex % arpPattern.Length];
    double arpEnv = PianoEnvelope(arpFrac);
    mix += SoftPiano(t, arpFreq) * arpEnv * 0.18;

    // Melody on top (slower, half notes)
    double melNoteLen = beatSec * 2;
    int melIndex = (int)(t / melNoteLen);
    double melFrac = (t % melNoteLen) / melNoteLen;
    int melIdx = SunriseMelody[melIndex % SunriseMelody.Length];
    if (melIdx >= 0)
    {
      double freq = SunriseNotes[melIdx] * 2; // octave up
      double env = PianoEnvelope(melFrac) * 0.7;
      mix += SoftPiano(t, freq) * env * 0.12;
    }

    // Gentle bass note (whole notes)
    double bassNoteLen = beatSec * 4;
    int bassIndex = (int)(t / bassNoteLen);
    double bassFrac = (t % bassNoteLen) / bassNoteLen;
    double bassFreq = SunriseChords[bassIndex % SunriseChords.Length] * 0.5;
    double bassEnv = Math.Min(bassFrac / 0.05, 1.0) * Math.Max(0, 1.0 - bassFrac * 0.5);
    mix += Math.Sin(2 * Math.PI * bassFreq * t) * bassEnv * 0.1;

    return mix;
  }

  // ── Theme 4: Starlight - Ethereal ambient with shimmer ──

  private static double GenerateStarlight(double t, double sr, long sample)
  {
    double mix = 0;
    const double bpm = 55.0;
    double beatSec = 60.0 / bpm;
    double noteLen = beatSec * 2.5;
    int noteIndex = (int)(t / noteLen);
    double noteFrac = (t % noteLen) / noteLen;

    // Ethereal pad - layered sine waves with slow modulation
    for (int n = 0; n < StarlightChords.Length; n++)
    {
      double padFreq = StarlightChords[n] * 0.5;
      double mod = Math.Sin(2 * Math.PI * t / (8.0 + n * 3.0)); // slow LFO
      double phase = 2 * Math.PI * padFreq * t;
      mix += Math.Sin(phase + mod * 0.3) * 0.05;
      // Octave up shimmer
      mix += Math.Sin(phase * 2 + mod * 0.5) * 0.02;
    }

    // Sparse melody - like distant music box
    int melIdx = StarlightMelody[noteIndex % StarlightMelody.Length];
    if (melIdx >= 0)
    {
      double freq = StarlightNotes[melIdx] * 2; // high register
      double env = Math.Exp(-noteFrac * 4.0); // quick decay like a music box
      double tone = Math.Sin(2 * Math.PI * freq * t);
      // Add octave harmonic for bell quality
      tone += Math.Sin(2 * Math.PI * freq * 2 * t) * 0.3;
      tone += Math.Sin(2 * Math.PI * freq * 3 * t) * 0.1;
      mix += tone * env * 0.1;
    }

    // Slowly evolving texture - filtered noise approximation using many sines
    double texture = 0;
    for (int h = 1; h <= 5; h++)
    {
      double texFreq = 110.0 * h + Math.Sin(t * 0.1 * h) * 2.0;
      texture += Math.Sin(2 * Math.PI * texFreq * t) * (1.0 / h);
    }
    double texEnv = (Math.Sin(2 * Math.PI * t / 16.0) + 1.0) * 0.5; // 16-second cycle
    mix += texture * texEnv * 0.012;

    // Occasional wind-like sweep
    double windCycle = 30.0; // 30-second period
    double windPhase = (t % windCycle) / windCycle;
    if (windPhase < 0.15)
    {
      double windVol = Math.Sin(windPhase / 0.15 * Math.PI) * 0.025;
      uint h = (uint)(sample * 1103515245 + 12345);
      double noise = ((h >> 16) & 0xFFFF) / 65536.0 * 2.0 - 1.0;
      // Very rough low-pass via averaging with sine
      mix += (noise * 0.3 + Math.Sin(2 * Math.PI * 80 * t) * 0.7) * windVol;
    }

    return mix;
  }

  // ── Shared synthesis helpers ──

  /// <summary>Piano-like ADSR: quick attack, medium decay, soft sustain, release at end.</summary>
  private static double PianoEnvelope(double frac)
  {
    if (frac < 0.01) return frac / 0.01; // attack
    if (frac < 0.15) return 1.0 - (frac - 0.01) * 3.5; // decay to sustain
    if (frac < 0.85) return 0.5 - (frac - 0.15) * 0.3; // slow sustain decay
    return Math.Max(0, (1.0 - frac) / 0.15 * 0.29); // release
  }

  /// <summary>Soft piano tone: fundamental + gentle harmonics with natural rolloff.</summary>
  private static double SoftPiano(double t, double freq)
  {
    double tone = Math.Sin(2 * Math.PI * freq * t);
    tone += Math.Sin(2 * Math.PI * freq * 2 * t) * 0.4;  // 2nd harmonic
    tone += Math.Sin(2 * Math.PI * freq * 3 * t) * 0.15; // 3rd harmonic
    tone += Math.Sin(2 * Math.PI * freq * 4 * t) * 0.06; // 4th harmonic
    return tone / 1.61; // normalize
  }
}
