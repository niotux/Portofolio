using UnivKit.Audio;

namespace UnivKit.OS.Audio;

public delegate T AudioSampleMixer<T>(T aggregate, T currentSample, IAudioSource currentAudio);
