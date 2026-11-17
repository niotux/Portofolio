using System.Runtime.InteropServices;
using QuantiX.Engine.Audio;

namespace QuantiX.Engine.Tests.FPS;

/// <summary>
/// Procedural looping synthwave music. Generates a dark electronic beat
/// with bass, kick, hi-hat, arpeggio, and pad layers.
/// </summary>
public class MusicSource : IAudioSource
{
  private readonly AudioFormat _format;
  private AudioState _state = AudioState.Paused;
  private long _sample;

  // BPM & timing
  private const double Bpm = 128.0;
  private const double BeatSec = 60.0 / Bpm;
  private const int BarBeats = 4;
  private const int PatternBars = 4;

  // Notes (Hz) - dark minor key (A minor / C minor)
  private static readonly double[] BassNotes = [55.0, 55.0, 65.41, 61.74]; // A1, A1, C2, B1
  private static readonly double[] ArpNotes = [220, 261.63, 329.63, 392, 329.63, 261.63]; // Am arpeggio
  private static readonly double[] PadNotes = [220, 261.63, 329.63]; // Am chord

  public MusicSource(AudioFormat format)
  {
    _format = format;
  }

  public AudioFormat Format => _format;
  public AudioState State => _state;
  public void Play() => _state = AudioState.Playing;
  public void Pause() => _state = AudioState.Paused;
  public void Reset() { _sample = 0; _state = AudioState.Paused; }

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
      double t = (_sample + i) / sr;
      double patternTime = t % (PatternBars * BarBeats * BeatSec);
      double beatPos = patternTime / BeatSec;
      int beatIndex = (int)beatPos;
      double beatFrac = beatPos - beatIndex;

      double mix = 0;

      // ── Kick drum (4-on-the-floor) ──
      if (beatFrac < 0.15)
      {
        double kickT = beatFrac / 0.15;
        double kickFreq = 150 * (1.0 - kickT * 0.7); // pitch sweep down
        mix += Math.Sin(2 * Math.PI * kickFreq * beatFrac) * (1.0 - kickT) * 0.35;
      }

      // ── Hi-hat (offbeats) ──
      double halfBeatFrac = (beatFrac * 2) % 1.0;
      if (halfBeatFrac < 0.03)
      {
        // Noise burst
        uint h = (uint)((_sample + i) * 1103515245 + 12345);
        double noise = ((h >> 16) & 0xFFFF) / 65536.0 * 2.0 - 1.0;
        mix += noise * (1.0 - halfBeatFrac / 0.03) * 0.08;
      }

      // ── Bass synth (sawtooth, one note per bar) ──
      int barIndex = beatIndex / BarBeats;
      double bassFreq = BassNotes[barIndex % BassNotes.Length];
      double bassPhase = (t * bassFreq) % 1.0;
      double bassSaw = bassPhase * 2.0 - 1.0;
      // Simple low-pass: average with previous
      double bassFiltered = bassSaw * 0.6;
      // ADSR envelope per beat
      double bassEnv = beatFrac < 0.05 ? beatFrac / 0.05 : Math.Max(0, 1.0 - (beatFrac - 0.05) * 1.5);
      mix += bassFiltered * bassEnv * 0.2;

      // ── Arpeggio (16th notes, square wave) ──
      double sixteenthPos = beatFrac * 4.0;
      int sixteenthIndex = (int)sixteenthPos;
      double sixteenthFrac = sixteenthPos - sixteenthIndex;
      int arpIndex = (beatIndex * 4 + sixteenthIndex) % ArpNotes.Length;
      double arpFreq = ArpNotes[arpIndex] * 2; // octave up
      double arpPhase = (t * arpFreq) % 1.0;
      double arpSquare = arpPhase < 0.5 ? 1.0 : -1.0;
      double arpEnv = sixteenthFrac < 0.1 ? sixteenthFrac / 0.1 : Math.Max(0, 1.0 - (sixteenthFrac - 0.1) * 2.0);
      mix += arpSquare * arpEnv * 0.08;

      // ── Pad (slow sine chord, fades in over bars) ──
      double padMix = 0;
      for (int n = 0; n < PadNotes.Length; n++)
      {
        padMix += Math.Sin(2 * Math.PI * PadNotes[n] * t + n * 0.3);
      }
      double padVol = Math.Min(patternTime / (2 * BeatSec), 1.0) * 0.06;
      mix += padMix * padVol;

      // ── Clamp and write ──
      mix = Math.Clamp(mix, -0.9, 0.9);
      short s = (short)(mix * 16000);

      for (int ch = 0; ch < _format.Channels; ch++)
        samples[i * _format.Channels + ch] = s;
    }

    _sample += frames;
    return buffer.Length;
  }
}
