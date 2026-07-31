using System.Media;
using OpenBuzz.Audio;

namespace OpenBuzz.Round;

/// <summary>
/// Plays a `.vgp` song clip by decoding it to an in-memory WAV.
///
/// SoundPlayer is deliberately the simplest thing that works: one clip at a
/// time, start and stop, no mixing. A real port needs a proper mixer (speech
/// over music, ducking, the fades the scripts ask for via
/// FadeThenStopAudio/NaturalFadeAudioTimeSeconds), but none of that is needed
/// to play a round.
/// </summary>
public sealed class ClipPlayer : IDisposable
{
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    public string? CurrentClip { get; private set; }
    public bool IsPlaying { get; private set; }

    public void Play(string vgpPath, int sampleRate)
    {
        Stop();

        var bytes = File.ReadAllBytes(vgpPath);
        var pcm = VgpFile.Decode(bytes, VgpFile.LayoutFor(bytes), out int channels);

        var ms = new MemoryStream();
        WavWriter.Write(ms, pcm, channels, sampleRate);
        ms.Position = 0;

        _stream = ms;
        _player = new SoundPlayer(ms);
        _player.Play();

        CurrentClip = Path.GetFileNameWithoutExtension(vgpPath);
        IsPlaying = true;
    }

    public void Stop()
    {
        if (_player is not null)
        {
            _player.Stop();
            _player.Dispose();
            _player = null;
        }
        _stream?.Dispose();
        _stream = null;
        IsPlaying = false;
    }

    public void Dispose() => Stop();
}
