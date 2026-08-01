using System.Buffers.Binary;

namespace OpenBuzz.Quiz;

/// <summary>
/// One 16-byte record from a `.rnd` question pool: eight little-endian uint16s.
/// </summary>
public readonly record struct QuestionRecord(
    ushort Id,
    ushort SongId,
    ushort QuestionTextId,
    ushort Extra,
    ushort Option0,
    ushort Option1,
    ushort Option2,
    ushort Option3)
{
    public const int Size = 16;
    public const int OptionCount = 4;

    /// <summary>
    /// The four options in stored order. Option0 is the correct answer: it is
    /// identical for every question shared between pools, the format has no
    /// correct-index field, and the engine exposes both GetRandomisedIndex and
    /// GetCorrectAnswerIndex - so display order is shuffled at runtime and the
    /// data does not need to record where the right one ended up.
    /// </summary>
    public ushort[] Options => [Option0, Option1, Option2, Option3];

    public ushort CorrectOption => Option0;

    public static QuestionRecord Read(ReadOnlySpan<byte> src)
    {
        Span<ushort> f = stackalloc ushort[8];
        for (int i = 0; i < 8; i++) f[i] = BinaryPrimitives.ReadUInt16LittleEndian(src[(i * 2)..]);
        return new QuestionRecord(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7]);
    }
}

/// A `.rnd` file: the question pool for one round type.
public sealed class QuestionPool
{
    public required string Name { get; init; }
    public required IReadOnlyList<QuestionRecord> Questions { get; init; }

    public static QuestionPool Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length % QuestionRecord.Size != 0)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} is {bytes.Length} bytes, not a whole number of {QuestionRecord.Size}-byte records.");

        var list = new List<QuestionRecord>(bytes.Length / QuestionRecord.Size);
        for (int o = 0; o < bytes.Length; o += QuestionRecord.Size)
            list.Add(QuestionRecord.Read(bytes.AsSpan(o, QuestionRecord.Size)));

        return new QuestionPool { Name = Path.GetFileNameWithoutExtension(path), Questions = list };
    }
}
