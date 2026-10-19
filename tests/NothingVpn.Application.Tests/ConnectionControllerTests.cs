using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
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
        var controller = new ConnectionController(vpn, new FakeElevationPort());

        var outcome = await controller.StartAsync("profile-1");

        Assert.True(outcome.Connected);
        Assert.Equal("profile-1", vpn.LastProfileId);
    }

    [Fact]
    public async Task StartAsync_ConnectionFailure_RollsBackAndPreservesOriginalError()
    {
        var expected = new InvalidOperationException("connect failed");
        var vpn = new FakeVpnConnectionService { ConnectError = expected };
        var controller = new ConnectionController(vpn, new FakeElevationPort());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.StartAsync("profile-1"));

        Assert.Same(expected, actual);
        Assert.Equal(1, vpn.DisconnectCalls);
    }

    [Fact]
    public async Task StopAsync_DelegatesToVpnService()
    {
        var vpn = new FakeVpnConnectionService();
        var controller = new ConnectionController(vpn, new FakeElevationPort());

        await controller.StopAsync();

        Assert.Equal(1, vpn.DisconnectCalls);
    }

    [Fact]
    public void IsAdministrator_ComesFromElevationPort()
    {
        var controller = new ConnectionController(new FakeVpnConnectionService(), new FakeElevationPort { IsAdmin = true });

        Assert.True(controller.IsAdministrator);
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

    private sealed class FakeElevationPort : IElevationPort
    {
        public bool IsAdmin { get; init; }
        public bool IsAdministrator() => IsAdmin;
    }
}
