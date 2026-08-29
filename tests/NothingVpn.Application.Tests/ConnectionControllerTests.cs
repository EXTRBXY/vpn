using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class ConnectionControllerTests
{
    [Fact]
    public async Task StartAsync_RegularConnection_ReturnsConnected()
    {
        var vpn = new FakeVpnConnectionService
        {
            ConnectResult = new ConnectResult { Started = true }
        };
        var controller = new ConnectionController(vpn, new FakeAppLifecycleService());

        var outcome = await controller.StartAsync("profile-1", "proxy");

        Assert.True(outcome.Connected);
        Assert.False(outcome.ExitCurrentProcess);
        Assert.Equal("profile-1", vpn.LastProfileId);
    }

    [Fact]
    public async Task StartAsync_ElevationRequired_RestartsAndRequestsCurrentProcessExit()
    {
        var vpn = new FakeVpnConnectionService
        {
            ConnectResult = new ConnectResult
            {
                RequiresElevation = true,
                ElevationArgs = "--takeover --start"
            }
        };
        var lifecycle = new FakeAppLifecycleService { RestartResult = true };
        var controller = new ConnectionController(vpn, lifecycle);

        var outcome = await controller.StartAsync("profile-1", "tun");

        Assert.False(outcome.Connected);
        Assert.True(outcome.ExitCurrentProcess);
        Assert.Equal("--takeover --start", lifecycle.RestartArguments);
    }

    [Fact]
    public async Task StartAsync_CancelledElevation_RollsBackAndThrows()
    {
        var vpn = new FakeVpnConnectionService
        {
            ConnectResult = new ConnectResult { RequiresElevation = true }
        };
        var lifecycle = new FakeAppLifecycleService { RestartResult = false };
        var controller = new ConnectionController(vpn, lifecycle);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.StartAsync("profile-1", "tun"));

        Assert.Contains("UAC", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, vpn.DisconnectCalls);
        Assert.Contains("--mode tun", lifecycle.RestartArguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_ConnectionFailure_RollsBackAndPreservesOriginalError()
    {
        var expected = new InvalidOperationException("connect failed");
        var vpn = new FakeVpnConnectionService { ConnectError = expected };
        var controller = new ConnectionController(vpn, new FakeAppLifecycleService());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.StartAsync("profile-1", "proxy"));

        Assert.Same(expected, actual);
        Assert.Equal(1, vpn.DisconnectCalls);
    }

    [Fact]
    public async Task StopAsync_DelegatesToVpnService()
    {
        var vpn = new FakeVpnConnectionService();
        var controller = new ConnectionController(vpn, new FakeAppLifecycleService());

        await controller.StopAsync();

        Assert.Equal(1, vpn.DisconnectCalls);
    }

    private sealed class FakeVpnConnectionService : IVpnConnectionService
    {
        public event EventHandler<bool>? ConnectionStateChanged
        {
            add { }
            remove { }
        }
        public ConnectResult ConnectResult { get; set; } = new();
        public Exception? ConnectError { get; set; }
        public string? LastProfileId { get; private set; }
        public int DisconnectCalls { get; private set; }
        public bool IsRunning { get; set; }

        public Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
        {
            LastProfileId = request.ProfileId;
            if (ConnectError is not null)
                return Task.FromException<ConnectResult>(ConnectError);
            return Task.FromResult(ConnectResult);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCalls++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public VpnConnectionStatus GetStatus() => new() { IsRunning = IsRunning };
        public void RecoverStaleRuntimeState() { }
    }

    private sealed class FakeAppLifecycleService : IAppLifecycleService
    {
        public bool RestartResult { get; set; }
        public string RestartArguments { get; private set; } = string.Empty;

        public bool IsAdministrator() => false;

        public bool RestartElevated(string arguments)
        {
            RestartArguments = arguments;
            return RestartResult;
        }

        public string BuildTakeoverArgs(string mode, string profileId) =>
            $"--takeover --start --mode {mode} --profile \"{profileId}\"";
    }
}
