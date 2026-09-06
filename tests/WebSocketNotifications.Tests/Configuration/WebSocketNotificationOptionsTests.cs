using Microsoft.Extensions.Options;
using Xunit;

namespace WebSocketNotifications.Tests.Configuration;

public sealed class WebSocketNotificationOptionsTests
{
    [Fact]
    public void Defaults_AreSafeAndOperational()
    {
        var options = new WebSocketNotificationOptions();

        Assert.Equal("/ws/notifications", options.EndpointPath);
        Assert.True(options.HeartbeatEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HeartbeatTimeout);
        Assert.False(options.CompressionEnabled);
        Assert.Equal(64 * 1024, options.MaxIncomingMessageSize);
        Assert.Equal(256 * 1024, options.MaxOutgoingMessageSize);
        Assert.Equal(128, options.OutgoingBufferCapacity);
        Assert.Equal(SlowClientPolicy.Disconnect, options.SlowClientPolicy);
        AssertValidationSucceeds(options);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notifications")]
    [InlineData("/ws/notifications?mode=test")]
    [InlineData("/ws/#notifications")]
    public void Validation_WhenEndpointPathIsInvalid_IdentifiesSetting(string endpointPath)
    {
        var options = new WebSocketNotificationOptions { EndpointPath = endpointPath };

        AssertValidationFails(options, nameof(options.EndpointPath));
    }

    [Fact]
    public void Validation_WhenHeartbeatTimingIsUnsafe_RejectsRatherThanClamps()
    {
        var options = new WebSocketNotificationOptions { HeartbeatInterval = TimeSpan.Zero };
        AssertValidationFails(options, nameof(options.HeartbeatInterval));
        Assert.Equal(TimeSpan.Zero, options.HeartbeatInterval);

        options = new WebSocketNotificationOptions { HeartbeatTimeout = TimeSpan.FromHours(2) };
        AssertValidationFails(options, nameof(options.HeartbeatTimeout));
        Assert.Equal(TimeSpan.FromHours(2), options.HeartbeatTimeout);

        options = new WebSocketNotificationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            HeartbeatTimeout = TimeSpan.FromSeconds(6),
        };
        AssertValidationFails(options, nameof(options.HeartbeatTimeout));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1048577)]
    public void Validation_WhenIncomingLimitIsOutsideHardBounds_RejectsValue(int size)
    {
        var options = new WebSocketNotificationOptions { MaxIncomingMessageSize = size };

        AssertValidationFails(options, nameof(options.MaxIncomingMessageSize));
        Assert.Equal(size, options.MaxIncomingMessageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public void Validation_WhenOutgoingLimitIsOutsideHardBounds_RejectsValue(int size)
    {
        var options = new WebSocketNotificationOptions { MaxOutgoingMessageSize = size };

        AssertValidationFails(options, nameof(options.MaxOutgoingMessageSize));
        Assert.Equal(size, options.MaxOutgoingMessageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void Validation_WhenBufferCapacityIsOutsideHardBounds_RejectsValue(int capacity)
    {
        var options = new WebSocketNotificationOptions { OutgoingBufferCapacity = capacity };

        AssertValidationFails(options, nameof(options.OutgoingBufferCapacity));
        Assert.Equal(capacity, options.OutgoingBufferCapacity);
    }

    [Fact]
    public void Validation_WhenSlowClientPolicyIsUnknown_RejectsValue()
    {
        var options = new WebSocketNotificationOptions { SlowClientPolicy = (SlowClientPolicy)99 };

        AssertValidationFails(options, nameof(options.SlowClientPolicy));
    }

    private static void AssertValidationSucceeds(WebSocketNotificationOptions options)
    {
        var result = new WebSocketNotificationOptionsValidator().Validate(Options.DefaultName, options);
        Assert.True(result.Succeeded);
    }

    private static void AssertValidationFails(WebSocketNotificationOptions options, string setting)
    {
        var result = new WebSocketNotificationOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(setting, result.FailureMessage, StringComparison.Ordinal);
    }
}
