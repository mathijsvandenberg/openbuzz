using System.Buffers.Binary;
using System.Text;

namespace OpenBuzz.Quiz;

public readonly record struct SongEntry(int Index, int Year, int Unknown, string Clip);

/// <summary>
/// `rri.dat` — the song table. An 8-byte header (question count, song count)
/// followed by one 12-byte record per song: uint16 release year, uint16 of
/// unknown purpose, then a 6-character clip basename.
///
/// All 1000 clip names resolve to real .vgp files on the disc, so the table
/// itself is certain. What is **not** verified is that a question's SongId
/// indexes this table directly. Both are 0..999 and the sizes line up exactly,
/// but the one cross-check available — questions whose correct answer is a bare
/// year — has a sample of 8 and matched twice, which settles nothing. Playing a
/// round is the practical test: if the options fit the music, the link is right.
/// </summary>
public sealed class SongTable
{
    public const int HeaderSize = 8;
    public const int RecordSize = 12;
    public const int ClipNameLength = 6;

    private readonly SongEntry[] _songs;

    private SongTable(SongEntry[] songs, int questionCount)
    {
        _songs = songs;
        DeclaredQuestionCount = questionCount;
    }

    public int Count => _songs.Length;
    public int DeclaredQuestionCount { get; }

    public SongEntry this[int index] => _songs[index];

    public bool TryGet(int index, out SongEntry entry)
    {
        if ((uint)index >= (uint)_songs.Length) { entry = default; return false; }
        entry = _songs[index];
        return true;
    }

    public static SongTable Load(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < HeaderSize) throw new InvalidDataException($"{path} is too short.");

        int questions = BinaryPrimitives.ReadInt32LittleEndian(b);
        int songs = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4));

        long expected = (long)HeaderSize + (long)songs * RecordSize;
        if (expected != b.Length)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} declares {songs} songs ({expected} bytes) but is {b.Length} bytes.");

        var entries = new SongEntry[songs];
        for (int i = 0; i < songs; i++)
        {
            int o = HeaderSize + i * RecordSize;
            var name = Encoding.ASCII.GetString(b, o + 4, ClipNameLength).TrimEnd('\0', ' ');
            entries[i] = new SongEntry(
                i,
                BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)),
                BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 2)),
                name);
        }

        return new SongTable(entries, questions);
    }
}
