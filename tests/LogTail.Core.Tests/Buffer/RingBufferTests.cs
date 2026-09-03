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
}
