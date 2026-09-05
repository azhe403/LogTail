using FluentAssertions;
using LogTail.Core.Buffer;
using Xunit;

namespace LogTail.Core.Tests.Buffer;

public sealed class RingBufferTests
{
    [Fact]
    public void Add_WhenCountWithinCapacity_RetainsAllItems()
    {
        var sut = new RingBuffer<int>(3);

        sut.Add(1);
        sut.Add(2);
        sut.Add(3);

        sut.Should().HaveCount(3);
        sut.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Add_WhenCountExceedsCapacity_EvictsOldestItems()
    {
        var sut = new RingBuffer<int>(3);

        sut.Add(1);
        sut.Add(2);
        sut.Add(3);
        sut.Add(4);

        sut.Should().HaveCount(3);
        sut.Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Add_WhenManyItemsAdded_EvictsOldestSequentially()
    {
        var sut = new RingBuffer<int>(3);

        for (int i = 0; i < 10; i++)
        {
            sut.Add(i);
        }

        sut.Should().HaveCount(3);
        sut.Should().Equal(7, 8, 9);
    }

    [Fact]
    public void Indexer_WhenValidIndex_ReturnsCorrectItem()
    {
        var sut = new RingBuffer<string>(2);

        sut.Add("first");
        sut.Add("second");

        sut[0].Should().Be("first");
        sut[1].Should().Be("second");
    }

    [Fact]
    public void Indexer_WhenIndexOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var sut = new RingBuffer<int>(3);

        sut.Invoking(s => s[0]).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Clear_WhenBufferHasItems_ResetsCountToZero()
    {
        var sut = new RingBuffer<int>(3);

        sut.Add(1);
        sut.Add(2);
        sut.Clear();

        sut.Should().HaveCount(0);
    }

    [Fact]
    public void Capacity_WhenInitialized_ReturnsConfiguredCapacity()
    {
        var sut = new RingBuffer<int>(42);

        sut.Capacity.Should().Be(42);
    }

    [Fact]
    public void Constructor_WhenCapacityIsZero_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => new RingBuffer<int>(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Add_WhenExecutedConcurrently_DoesNotCorruptState()
    {
        var sut = new RingBuffer<int>(1000);
        var barrier = new System.Threading.ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 4).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            barrier.Wait();
            for (int i = 0; i < 500; i++)
            {
                sut.Add(i);
            }
        })).ToArray();

        barrier.Set();
        System.Threading.Tasks.Task.WaitAll(tasks);

        sut.Count.Should().BeLessThanOrEqualTo(sut.Capacity);
    }

    [Fact]
    public void Add_WhenCountExceedsInitialCapacity_GrowsBuffer()
    {
        var sut = new RingBuffer<int>(initialCapacity: 4, maxCapacity: 16);

        for (int i = 0; i < 10; i++)
        {
            sut.Add(i);
        }

        sut.Count.Should().Be(10);
        sut.Capacity.Should().BeGreaterThan(4);
        sut.Capacity.Should().BeLessThanOrEqualTo(16);
        sut.Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void Grew_WhenBufferGrows_FiresEventWithNewCapacity()
    {
        var sut = new RingBuffer<int>(initialCapacity: 2, maxCapacity: 8);
        var capacities = new System.Collections.Generic.List<int>();
        sut.Grew += (_, cap) => capacities.Add(cap);

        for (int i = 0; i < 6; i++)
        {
            sut.Add(i);
        }

        capacities.Should().NotBeEmpty();
        capacities.Should().OnlyContain(c => c > 2);
        capacities.Last().Should().Be(sut.Capacity);
    }

    [Fact]
    public void Add_WhenAtMaxCapacity_EvictsOldestInsteadOfGrowing()
    {
        var sut = new RingBuffer<int>(initialCapacity: 2, maxCapacity: 4);
        var grewCount = 0;
        sut.Grew += (_, _) => grewCount++;

        for (int i = 0; i < 10; i++)
        {
            sut.Add(i);
        }

        sut.Capacity.Should().Be(4);
        sut.Count.Should().Be(4);
        sut.Should().Equal(6, 7, 8, 9);
        sut.IsAtMaxCapacity.Should().BeTrue();
        grewCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Indexer_AfterGrow_RetainsAllItemsInOrder()
    {
        var sut = new RingBuffer<string>(initialCapacity: 3, maxCapacity: 100);

        sut.Add("a");
        sut.Add("b");
        sut.Add("c");
        sut.Add("d"); // triggers grow
        sut.Add("e");

        sut[0].Should().Be("a");
        sut[1].Should().Be("b");
        sut[2].Should().Be("c");
        sut[3].Should().Be("d");
        sut[4].Should().Be("e");
    }

    [Fact]
    public void GetEnumerator_AfterGrow_ReturnsItemsInOrder()
    {
        var sut = new RingBuffer<int>(initialCapacity: 2, maxCapacity: 16);

        for (int i = 0; i < 5; i++)
        {
            sut.Add(i);
        }

        sut.ToList().Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void Constructor_WhenMaxLessThanInitial_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => new RingBuffer<int>(initialCapacity: 10, maxCapacity: 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WhenGrowFactorLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => new RingBuffer<int>(initialCapacity: 4, maxCapacity: 10, growFactor: 0.5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Add_WhenExecutedConcurrentlyWithGrow_PreservesAllItemsUnderMax()
    {
        var sut = new RingBuffer<int>(initialCapacity: 100, maxCapacity: 10_000);
        var barrier = new System.Threading.ManualResetEventSlim(false);
        var totalAdds = 4 * 500;

        var tasks = Enumerable.Range(0, 4).Select(threadId => System.Threading.Tasks.Task.Run(() =>
        {
            barrier.Wait();
            for (int i = 0; i < 500; i++)
            {
                sut.Add(threadId * 1000 + i);
            }
        })).ToArray();

        barrier.Set();
        System.Threading.Tasks.Task.WaitAll(tasks);

        sut.Count.Should().Be(totalAdds);
        sut.Capacity.Should().BeGreaterThanOrEqualTo(totalAdds);
    }
}
