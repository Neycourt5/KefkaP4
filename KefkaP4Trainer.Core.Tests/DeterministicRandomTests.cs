using KefkaP4Trainer.Core;

namespace KefkaP4Trainer.Core.Tests;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void FixedSeedProducesIdenticalStream()
    {
        var first = new DeterministicRandom(0x1234_5678);
        var second = new DeterministicRandom(0x1234_5678);

        var firstValues = Enumerable.Range(0, 128).Select(_ => first.NextUInt()).ToArray();
        var secondValues = Enumerable.Range(0, 128).Select(_ => second.NextUInt()).ToArray();

        Assert.Equal(firstValues, secondValues);
    }

    [Fact]
    public void InclusiveRangeStaysWithinBounds()
    {
        var random = new DeterministicRandom(-99123);
        var values = Enumerable.Range(0, 1000)
            .Select(_ => random.NextInclusive(3, 7))
            .ToArray();

        Assert.All(values, value => Assert.InRange(value, 3, 7));
        Assert.Equal([3, 4, 5, 6, 7], values.Distinct().Order().ToArray());
    }

    [Fact]
    public void ShuffleIsDeterministicAndPreservesMembers()
    {
        var first = Enumerable.Range(0, 32).ToArray();
        var second = Enumerable.Range(0, 32).ToArray();
        new DeterministicRandom(42).Shuffle(first);
        new DeterministicRandom(42).Shuffle(second);

        Assert.Equal(first, second);
        Assert.Equal(Enumerable.Range(0, 32), first.Order());
        Assert.NotEqual(Enumerable.Range(0, 32), first);
    }
}

