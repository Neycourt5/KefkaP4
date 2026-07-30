namespace KefkaP4Trainer.Core;

/// <summary>
/// Stable plugin-owned PCG32 stream. Waju uses Godot's unseeded global RNG,
/// so this is intentionally a new replay format rather than a claimed Godot
/// seed format.
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong Multiplier = 6364136223846793005UL;
    private ulong state;
    private readonly ulong increment;

    public DeterministicRandom(long seed)
    {
        var bits = unchecked((ulong)seed);
        increment = (bits << 1) | 1UL;
        state = 0;
        _ = NextUInt();
        state += bits ^ 0x9E3779B97F4A7C15UL;
        _ = NextUInt();
    }

    public uint NextUInt()
    {
        var oldState = state;
        state = unchecked((oldState * Multiplier) + increment);
        var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
    }

    public bool NextBool() => (NextUInt() & 1U) == 0U;

    public int NextInclusive(int minimum, int maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum cannot exceed maximum.");
        }

        var range = (uint)((long)maximum - minimum + 1L);
        var threshold = unchecked((uint)(-range)) % range;
        uint value;
        do
        {
            value = NextUInt();
        }
        while (value < threshold);

        return minimum + (int)(value % range);
    }

    public void Shuffle<T>(Span<T> values)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var other = NextInclusive(0, index);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }
}

