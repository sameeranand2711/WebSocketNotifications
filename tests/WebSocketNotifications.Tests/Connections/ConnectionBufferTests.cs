using System.Diagnostics;
using System.Text;
using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class ConnectionBufferTests
{
    [Fact]
    public async Task TryEnqueue_WhenCapacityIsAvailable_PreservesOrder()
    {
        var buffer = new ConnectionBuffer(2, SlowClientPolicy.Disconnect);

        Assert.Equal(BufferWriteResult.Enqueued, buffer.TryEnqueue(Bytes("first")));
        Assert.Equal(BufferWriteResult.Enqueued, buffer.TryEnqueue(Bytes("second")));

        Assert.Equal("first", Text(await buffer.ReadAsync()));
        Assert.Equal("second", Text(await buffer.ReadAsync()));
    }

    [Fact]
    public async Task TryEnqueue_WhenFullAndPolicyIsDisconnect_RequestsDisconnect()
    {
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        buffer.TryEnqueue(Bytes("first"));

        Assert.Equal(BufferWriteResult.Disconnect, buffer.TryEnqueue(Bytes("second")));
        Assert.Equal("first", Text(await buffer.ReadAsync()));
    }

    [Fact]
    public async Task TryEnqueue_WhenFullAndPolicyIsDropCurrent_KeepsBufferedMessage()
    {
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.DropCurrent);
        buffer.TryEnqueue(Bytes("first"));

        Assert.Equal(BufferWriteResult.Dropped, buffer.TryEnqueue(Bytes("second")));
        Assert.Equal("first", Text(await buffer.ReadAsync()));
    }

    [Fact]
    public async Task TryEnqueue_WhenFullAndPolicyIsDropOldest_KeepsNewestMessagesInOrder()
    {
        var buffer = new ConnectionBuffer(2, SlowClientPolicy.DropOldest);
        buffer.TryEnqueue(Bytes("first"));
        buffer.TryEnqueue(Bytes("second"));

        Assert.Equal(BufferWriteResult.Enqueued, buffer.TryEnqueue(Bytes("third")));
        Assert.Equal("second", Text(await buffer.ReadAsync()));
        Assert.Equal("third", Text(await buffer.ReadAsync()));
    }

    [Theory]
    [InlineData(SlowClientPolicy.Disconnect)]
    [InlineData(SlowClientPolicy.DropOldest)]
    [InlineData(SlowClientPolicy.DropCurrent)]
    public void TryEnqueue_WhenBufferRemainsFull_NeverWaitsForReader(SlowClientPolicy policy)
    {
        var buffer = new ConnectionBuffer(1, policy);
        buffer.TryEnqueue(Bytes("initial"));
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 10_000; index++)
        {
            buffer.TryEnqueue(Bytes("overflow"));
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Writes took {stopwatch.Elapsed}.");
    }

    private static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(ReadOnlyMemory<byte> value) => Encoding.UTF8.GetString(value.Span);
}
