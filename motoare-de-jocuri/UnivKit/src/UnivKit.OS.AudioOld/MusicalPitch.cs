using System.Reflection.Metadata;

namespace QuantiX.Engine.Audio;

public static class MusicalPitch
{
  public static double Frequency(MusicalNote note, int octave = 4)
  {
    int midiNumber = (octave + 1) * 12 + (byte)note;
    return 440.0 * double.Pow(2.0, (double)(midiNumber - 69) / 12.0);
  }



  // Octave 0
  public const double C0 = 16.35160;
  public const double CSharp0 = 17.32391;
  public const double D0 = 18.35405;
  public const double DSharp0 = 19.44544;
  public const double E0 = 20.60172;
  public const double F0 = 21.82676;
  public const double FSharp0 = 23.12465;
  public const double G0 = 24.49971;
  public const double GSharp0 = 25.95654;
  public const double A0 = 27.50000;
  public const double ASharp0 = 29.13524;
  public const double B0 = 30.86771;

  // Octave 1
  public const double C1 = 32.7032;
  public const double CSharp1 = 34.6478;
  public const double D1 = 36.7081;
  public const double DSharp1 = 38.8909;
  public const double E1 = 41.2034;
  public const double F1 = 43.6535;
  public const double FSharp1 = 46.2493;
  public const double G1 = 48.9994;
  public const double GSharp1 = 51.9131;
  public const double A1 = 55.0000;
  public const double ASharp1 = 58.2705;
  public const double B1 = 61.7354;

  // Octave 2
  public const double C2 = 65.4064;
  public const double CSharp2 = 69.2957;
  public const double D2 = 73.4162;
  public const double DSharp2 = 77.7817;
  public const double E2 = 82.4069;
  public const double F2 = 87.3071;
  public const double FSharp2 = 92.4986;
  public const double G2 = 97.9989;
  public const double GSharp2 = 103.8262;
  public const double A2 = 110.0000;
  public const double ASharp2 = 116.5409;
  public const double B2 = 123.4708;

  // Octave 3
  public const double C3 = 130.8128;
  public const double CSharp3 = 138.5913;
  public const double D3 = 146.8324;
  public const double DSharp3 = 155.5635;
  public const double E3 = 164.8138;
  public const double F3 = 174.6141;
  public const double FSharp3 = 184.9972;
  public const double G3 = 195.9977;
  public const double GSharp3 = 207.6523;
  public const double A3 = 220.0000;
  public const double ASharp3 = 233.0819;
  public const double B3 = 246.9417;

  // Octave 4 (Middle octave)
  public const double C4 = 261.6256;
  public const double CSharp4 = 277.1826;
  public const double D4 = 293.6648;
  public const double DSharp4 = 311.1270;
  public const double E4 = 329.6276;
  public const double F4 = 349.2282;
  public const double FSharp4 = 369.9944;
  public const double G4 = 391.9954;
  public const double GSharp4 = 415.3047;
  public const double A4 = 440.0000;
  public const double ASharp4 = 466.1638;
  public const double B4 = 493.8833;

  // Octave 5
  public const double C5 = 523.2511;
  public const double CSharp5 = 554.3653;
  public const double D5 = 587.3295;
  public const double DSharp5 = 622.2540;
  public const double E5 = 659.2551;
  public const double F5 = 698.4565;
  public const double FSharp5 = 739.9888;
  public const double G5 = 783.9909;
  public const double GSharp5 = 830.6094;
  public const double A5 = 880.0000;
  public const double ASharp5 = 932.3275;
  public const double B5 = 987.7666;

  // Octave 6
  public const double C6 = 1046.5023;
  public const double CSharp6 = 1108.7305;
  public const double D6 = 1174.6591;
  public const double DSharp6 = 1244.5079;
  public const double E6 = 1318.5102;
  public const double F6 = 1396.9129;
  public const double FSharp6 = 1479.9777;
  public const double G6 = 1567.9817;
  public const double GSharp6 = 1661.2188;
  public const double A6 = 1760.0000;
  public const double ASharp6 = 1864.6550;
  public const double B6 = 1975.5332;

  // Octave 7
  public const double C7 = 2093.0045;
  public const double CSharp7 = 2217.4610;
  public const double D7 = 2349.3181;
  public const double DSharp7 = 2489.0159;
  public const double E7 = 2637.0205;
  public const double F7 = 2793.8259;
  public const double FSharp7 = 2959.9554;
  public const double G7 = 3135.9635;
  public const double GSharp7 = 3322.4376;
  public const double A7 = 3520.0000;
  public const double ASharp7 = 3729.3101;
  public const double B7 = 3951.0664;

  // Octave 8
  public const double C8 = 4186.0090;
  public const double CSharp8 = 4434.9221;
  public const double D8 = 4698.6363;
  public const double DSharp8 = 4978.0317;
  public const double E8 = 5274.0409;
  public const double F8 = 5587.6517;
  public const double FSharp8 = 5919.9108;
  public const double G8 = 6271.9270;
  public const double GSharp8 = 6644.8752;
  public const double A8 = 7040.0000;
  public const double ASharp8 = 7458.6202;
  public const double B8 = 7902.1328;
}
