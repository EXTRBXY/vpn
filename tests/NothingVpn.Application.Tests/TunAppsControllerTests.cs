using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class TunAppsControllerTests
{
    [Fact]
    public void AddAndSave_NormalizesAndRemovesDuplicates()
    {
        var settings = new FakeSettingsService();
        var controller = new TunAppsController(new FakePathPolicy(), settings);
        var state = new AppStateModel();

        var result = controller.AddAndSave(
            state,
            new[] { @"C:\Apps\One.exe" },
            new[] { @"c:\apps\one.exe", @"C:\Apps\Two.exe", "invalid.txt" });

        Assert.Equal(new[] { @"C:\Apps\One.exe", @"C:\Apps\Two.exe" }, result);
        Assert.Equal(result, state.TunAppProcessPaths);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void RemoveAndSave_RemovesPathCaseInsensitively()
    {
        var settings = new FakeSettingsService();
        var controller = new TunAppsController(new FakePathPolicy(), settings);
        var state = new AppStateModel();

        var result = controller.RemoveAndSave(
            state,
            new[] { @"C:\Apps\One.exe", @"C:\Apps\Two.exe" },
            @"c:\apps\ONE.exe");

        Assert.Equal(new[] { @"C:\Apps\Two.exe" }, result);
        Assert.Equal(1, settings.SaveCalls);
    }

    private sealed class FakePathPolicy : IPathPolicyPort
    {
        public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths ?? Array.Empty<string>())
            {
                if (!TryNormalizeExePath(path, out var normalized) || !seen.Add(normalized))
                    continue;
                result.Add(normalized);
            }
            return result;
        }

        public bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
        {
            normalizedPath = (rawPath ?? string.Empty).Trim();
            return Path.IsPathRooted(normalizedPath) &&
                   normalizedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public event EventHandler<AppStateModel>? StateChanged
        {
            add { }
            remove { }
        }

        public int SaveCalls { get; private set; }
        public AppStateModel GetState() => throw new NotSupportedException();
        public void SaveState(AppStateModel state) => SaveCalls++;
        public void UpdateMode(string mode) => throw new NotSupportedException();
        public void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour) => throw new NotSupportedException();
        public void UpdateTunSettings(TunSettings settings) => throw new NotSupportedException();
        public void UpdateProxySettings(ProxyConnectionSettings settings) => throw new NotSupportedException();
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) => throw new NotSupportedException();
        public void UpdateTunApps(IReadOnlyCollection<string> paths) => throw new NotSupportedException();
    }
}
