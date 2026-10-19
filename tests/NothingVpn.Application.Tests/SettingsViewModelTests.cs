using System.Text.Json;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Desktop.Wpf;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class SettingsViewModelTests
{
    private static readonly string[] Paths = [@"C:\Apps\one.exe", @"C:\Apps\two.exe", @"C:\Apps\three.exe"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Remove_Save_Reopen_PreservesOtherApps(int index)
    {
        var h = new Harness();
        var vm = h.Create();
        vm.Apps.SelectedTunApp = vm.Apps.TunApps[index];
        vm.Apps.RemoveSelectedTunApp();
        var expected = Paths.Where((_, i) => i != index).ToArray();
        Assert.Equal(expected, vm.Apps.TunApps.Select(x => x.Path));
        Assert.Equal(Paths, h.Store.State.TunAppProcessPaths);
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, h.Store.SaveCalls);
        Assert.Equal(expected, h.Create().Apps.TunApps.Select(x => x.Path));
    }

    [Fact]
    public void RemoveAll_Save_Reopen_RemainsEmpty()
    {
        var h = new Harness();
        var vm = h.Create();
        while (vm.Apps.TunApps.Count > 0)
        {
            vm.Apps.SelectedTunApp = vm.Apps.TunApps[0];
            vm.Apps.RemoveSelectedTunApp();
        }
        vm.SaveCommand.Execute(null);
        Assert.Empty(h.Create().Apps.TunApps);
    }

    [Fact]
    public void Add_RejectsDuplicatesAndInvalidPaths_WithoutLosingExistingApps()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.Apps.AddTunApp(Paths[0].ToUpperInvariant());
        Assert.Contains("уже", vm.Message);
        vm.Apps.AddTunApp("not-an-executable.txt");
        Assert.Equal(Paths, vm.Apps.TunApps.Select(x => x.Path));
        vm.Apps.AddTunApp(@"C:\Apps\four.exe");
        vm.SaveCommand.Execute(null);
        Assert.Equal(4, h.Create().Apps.TunApps.Count);
    }

    [Fact]
    public void EditingDns_UpdatesPreset_AndAllowsReapplyingIt()
    {
        var vm = new Harness().Create();
        Assert.Equal(DnsPreset.Google, vm.Dns.DnsPreset);
        vm.Dns.DohServer = "8.8.4.4";
        Assert.Equal(DnsPreset.Custom, vm.Dns.DnsPreset);
        vm.Dns.DnsPreset = DnsPreset.Google;
        Assert.Equal("8.8.8.8", vm.Dns.DohServer);
        Assert.Equal(DnsPreset.Google, vm.Dns.DnsPreset);
    }

    [Fact]
    public void Navigation_PreservesDraft_AndRefreshesDnsModeConstraint()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.Tun.InterfaceName = "Edited";
        vm.Tun.MtuText = "1400";
        vm.Apps.AddTunApp(@"C:\Apps\four.exe");
        vm.RuleSets.BuiltinRuleSets[0].Action = RuleSetAction.Block;
        Assert.Equal("direct", h.Store.State.UserRuleSets[0].Action.ToValue());
        h.Settings.UpdateMode(ConnectionMode.TunApps);
        vm.Dns.DnsDetour = DnsDetour.Proxy;
        vm.Reload();
        Assert.Equal("Edited", vm.Tun.InterfaceName);
        Assert.Equal("1400", vm.Tun.MtuText);
        Assert.Equal(4, vm.Apps.TunApps.Count);
        Assert.Equal("block", vm.RuleSets.BuiltinRuleSets[0].Action.ToValue());
        Assert.False(vm.Dns.IsDnsDetourEditable);
        Assert.Equal("direct", vm.Dns.DnsDetour.ToValue());
    }

    [Fact]
    public void SaveAll_WritesOnce_AndPreservesBackgroundAndConnectionFields()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.Tun.InterfaceName = "Edited";
        vm.Tun.MtuText = "1400";
        vm.Behavior.CloseBehavior = AppCloseBehavior.Exit;
        vm.Behavior.LogLevel = LogLevel.Debug;
        vm.RuleSets.BuiltinRuleSets[0].Action = RuleSetAction.Block;
        vm.Apps.AddTunApp(@"C:\Apps\four.exe");
        h.Settings.UpdateState(state =>
        {
            state.UpdateDismissedModalForTag = "v9.9.9";
            state.ActiveProfileId = "changed-in-background";
        });
        // Runtime-флаг пишет только VpnConnectionService, в обход SettingsService.
        var connected = h.Store.Load();
        connected.ProxyWasEnabledByUs = true;
        h.Store.Save(connected);
        var before = h.Store.SaveCalls;
        vm.SaveCommand.Execute(null);
        Assert.Equal(before + 1, h.Store.SaveCalls);
        var state = h.Store.State;
        Assert.Equal("Edited", state.TunInterfaceName);
        Assert.Equal(1400, state.TunMtu);
        Assert.Equal("exit", state.CloseBehavior.ToValue());
        Assert.Equal("debug", state.SingBoxLogLevel.ToValue());
        Assert.Equal("block", state.UserRuleSets[0].Action.ToValue());
        Assert.Equal(4, state.TunAppProcessPaths.Count);
        Assert.Equal("v9.9.9", state.UpdateDismissedModalForTag);
        Assert.Equal("changed-in-background", state.ActiveProfileId);
        Assert.True(state.ProxyWasEnabledByUs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("575")]
    [InlineData("9001")]
    public void InvalidMtu_DoesNotSaveOrDeleteFiles(string mtu)
    {
        var h = new Harness();
        var vm = h.Create();
        vm.RuleSets.SelectedBuiltinRuleSet = vm.RuleSets.BuiltinRuleSets[0];
        vm.RuleSets.RemoveSelectedBuiltin();
        vm.Tun.MtuText = mtu;
        vm.SaveCommand.Execute(null);
        Assert.Equal(0, h.Store.SaveCalls);
        Assert.Empty(h.Files.Deleted);
        Assert.Contains("MTU", vm.Message);
    }

    [Fact]
    public void InvalidDns_DoesNotPartiallySave_AndKeepsDraft()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.Dns.DohServer = "";
        vm.Behavior.CloseBehavior = AppCloseBehavior.Exit;
        vm.Apps.SelectedTunApp = vm.Apps.TunApps[0];
        vm.Apps.RemoveSelectedTunApp();
        vm.SaveCommand.Execute(null);
        Assert.Equal(0, h.Store.SaveCalls);
        Assert.Equal(Paths, h.Store.State.TunAppProcessPaths);
        Assert.Equal("tray", h.Store.State.CloseBehavior.ToValue());
        Assert.Equal(2, vm.Apps.TunApps.Count);
        Assert.Contains("DoH", vm.Message);
    }

    [Fact]
    public void FailedWrite_KeepsDraftAndFiles_ForRetry()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.RuleSets.SelectedUserRuleSet = vm.RuleSets.UserRuleSets[0];
        vm.RuleSets.RemoveSelectedRuleSet();
        h.Store.FailWrites = true;
        vm.SaveCommand.Execute(null);
        Assert.Empty(h.Files.Deleted);
        Assert.Empty(vm.RuleSets.UserRuleSets);
        Assert.Contains("write failed", vm.Message);
        h.Store.FailWrites = false;
        vm.SaveCommand.Execute(null);
        Assert.Single(h.Files.Deleted);
        Assert.Empty(h.Create().RuleSets.UserRuleSets);
    }

    [Fact]
    public async Task Download_MissingFile_DoesNotUseCachedEtag()
    {
        var h = new Harness();
        h.Files.FileExists = false;
        var vm = h.Create();
        vm.RuleSets.SelectedBuiltinRuleSet = vm.RuleSets.BuiltinRuleSets[0];
        vm.RuleSets.SelectedBuiltinRuleSet.RemoteEtag = "old";
        var task = vm.RuleSets.DownloadSelectedBuiltinAsync();
        Assert.False(h.Files.ConditionalRequest);
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, false, null, null));
        await task;
        Assert.Null(vm.RuleSets.BuiltinRuleSets[0].RemoteEtag);
    }

    [Fact]
    public async Task Download_CapturesOriginalSelection_AndOnlyEditsDraft()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.RuleSets.SelectedBuiltinRuleSet = vm.RuleSets.BuiltinRuleSets[0];
        var download = vm.RuleSets.DownloadSelectedBuiltinAsync();
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.RuleSets.SelectedBuiltinRuleSet = vm.RuleSets.BuiltinRuleSets[1];
        vm.RuleSets.RemoveSelectedBuiltin();
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, false, "new-etag", null));
        await download;
        Assert.Equal("new-etag", vm.RuleSets.BuiltinRuleSets[0].RemoteEtag);
        Assert.Null(vm.RuleSets.BuiltinRuleSets[1].RemoteEtag);
        Assert.Equal(0, h.Store.SaveCalls);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Equal("new-etag", h.Create().RuleSets.BuiltinRuleSets[0].RemoteEtag);
        Assert.Empty(h.Files.Deleted);
    }

    [Theory]
    [InlineData("sing-geosite:category-ru")]
    [InlineData("sing-geosite:category-ru-ads")]
    [InlineData("sing-geoip:ru")]
    public async Task EnableMissingBuiltin_DownloadsBeforeEnabling_AndPersistsOnSave(string builtinId)
    {
        var h = new Harness();
        h.Files.FileExists = false;
        h.Store.State.UserRuleSets[0].BuiltinId = builtinId;
        h.Store.State.UserRuleSets[0].Enabled = false;
        var vm = h.Create();
        var rule = vm.RuleSets.BuiltinRuleSets[0];
        rule.RemoteEtag = "stale-etag";
        var download = vm.RuleSets.SetBuiltinEnabledAsync(rule, true);
        Assert.Same(rule, Assert.Single(h.Files.Downloads));
        Assert.False(h.Files.ConditionalRequest);
        Assert.False(rule.Enabled);
        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.RuleSets.SetBuiltinEnabledAsync(rule, true);
        Assert.Single(h.Files.Downloads);
        h.Files.FileExists = true;
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, false, "new-etag", null));
        await download;
        Assert.True(vm.RuleSets.BuiltinRuleSets[0].Enabled);
        Assert.NotNull(vm.RuleSets.BuiltinRuleSets[0].LastDownloadedUtc);
        Assert.False(h.Store.State.UserRuleSets[0].Enabled);
        vm.SaveCommand.Execute(null);
        var saved = h.Create().RuleSets.BuiltinRuleSets[0];
        Assert.True(saved.Enabled);
        Assert.Equal("new-etag", saved.RemoteEtag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnableMissingBuiltin_FailureLeavesDisabled_AndAllowsRetry(bool throws)
    {
        var h = new Harness();
        h.Files.FileExists = false;
        h.Store.State.UserRuleSets[0].Enabled = false;
        var vm = h.Create();
        var download = vm.RuleSets.SetBuiltinEnabledAsync(vm.RuleSets.BuiltinRuleSets[0], true);
        if (throws) h.Files.Download.SetException(new IOException("download failed"));
        else h.Files.Download.SetResult(new RuleSetDownloadResult(false, false, null, "download failed"));
        await download;
        Assert.False(vm.RuleSets.BuiltinRuleSets[0].Enabled);
        Assert.Null(vm.RuleSets.BuiltinRuleSets[0].LastDownloadedUtc);
        Assert.True(vm.RuleSets.CanUseBuiltinRuleSet);
        Assert.Contains("download failed", vm.Message);
        vm.SaveCommand.Execute(null);
        Assert.False(h.Create().RuleSets.BuiltinRuleSets[0].Enabled);
    }

    [Fact]
    public async Task ReenableExistingBuiltin_CancelsPendingDeletion_WithoutDownloading()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.RuleSets.SelectedBuiltinRuleSet = vm.RuleSets.BuiltinRuleSets[0];
        vm.RuleSets.RemoveSelectedBuiltin();
        await vm.RuleSets.SetBuiltinEnabledAsync(vm.RuleSets.BuiltinRuleSets[0], true);
        vm.SaveCommand.Execute(null);
        Assert.True(h.Create().RuleSets.BuiltinRuleSets[0].Enabled);
        Assert.Empty(h.Files.Downloads);
        Assert.Empty(h.Files.Deleted);
        await vm.RuleSets.SetBuiltinEnabledAsync(vm.RuleSets.BuiltinRuleSets[0], false);
        Assert.False(vm.RuleSets.BuiltinRuleSets[0].Enabled);
        Assert.Empty(h.Files.Downloads);
    }

    [Fact]
    public async Task UpdateBuiltin_NotModified_PreservesEtagAndSavesCheckTime()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.RuleSets.SelectedBuiltinRuleSet = vm.RuleSets.BuiltinRuleSets[0];
        vm.RuleSets.SelectedBuiltinRuleSet.RemoteEtag = "existing-etag";
        var download = vm.RuleSets.DownloadSelectedBuiltinAsync();
        Assert.True(h.Files.ConditionalRequest);
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, true, null, null));
        await download;
        Assert.Contains("актуален", vm.Message);
        vm.SaveCommand.Execute(null);
        Assert.Equal("existing-etag", h.Create().RuleSets.BuiltinRuleSets[0].RemoteEtag);
        Assert.NotNull(h.Create().RuleSets.BuiltinRuleSets[0].LastDownloadedUtc);
    }

    [Fact]
    public async Task FindTunApps_MergesInstalledAndRunning_WithoutDuplicates_AndExcludesSelected()
    {
        var discovery = new ListedApps(
            installed: [new("Zeta", @"C:\Apps\zeta.exe", AppCandidateSource.Installed), new("Alpha", @"C:\Apps\alpha.exe", AppCandidateSource.Installed)],
            running: [new("Alpha (running)", @"C:\APPS\ALPHA.EXE", AppCandidateSource.Running), new("Beta", @"C:\Apps\beta.exe", AppCandidateSource.Running)]);
        var h = new Harness { Discovery = discovery };
        var vm = h.Create();

        var found = await vm.Apps.FindTunAppsAsync();

        Assert.Equal(["Alpha", "Beta", "Zeta"], found.Select(x => x.DisplayName));
        Assert.Equal(Paths, discovery.SelectedPaths);
    }

    internal sealed class Harness
    {
        public MemoryStore Store { get; } = new();
        public FakeFiles Files { get; } = new();
        public IAppDiscoveryPort Discovery { get; set; } = new EmptyApps();
        public SettingsService Settings { get; }
        public Harness() => Settings = new SettingsService(new AppStateStore(Store), new PathPolicyPort());
        public SettingsViewModel Create() => new(
            new ConnectionSettingsController(Settings), new TunAppsController(new PathPolicyPort(), Settings),
            new RuleSetManagementController(Settings), Files, Discovery,
            Settings, new UpdateViewModel(null!, null!, null!, Settings, Settings.GetState(), () => { }));
    }

    internal sealed class MemoryStore : IStateStorePort
    {
        public AppStateModel State { get; private set; } = new()
        {
            TunAppProcessPaths = Paths.ToList(),
            UserRuleSets = [
                new() { Tag = "builtin-one", BuiltinId = "one", FileName = "one.srs", Name = "One" },
                new() { Tag = "builtin-two", BuiltinId = "two", FileName = "two.srs", Name = "Two" },
                new() { Tag = "user-one", FileName = "user.srs", Name = "User" }]
        };
        public bool FailWrites { get; set; }
        public bool SaveMissingBuiltinRuleSets() => false;
        public int SaveCalls { get; private set; }
        public AppStateModel Load() => Clone(State);
        public void Save(AppStateModel state)
        {
            if (FailWrites) throw new IOException("write failed");
            SaveCalls++;
            State = Clone(state);
        }
        private static AppStateModel Clone(AppStateModel state) => JsonSerializer.Deserialize<AppStateModel>(JsonSerializer.Serialize(state))!;
    }

    internal sealed class FakeFiles : IRuleSetFileService
    {
        public string CatalogUrl => "https://example.com";
        public List<string> Deleted { get; } = [];
        public TaskCompletionSource<RuleSetDownloadResult> Download { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FileExists { get; set; } = true;
        public bool ConditionalRequest { get; private set; }
        public List<UserRuleSetModel> Downloads { get; } = [];
        public bool Exists(UserRuleSetModel ruleSet) => FileExists;
        public RuleSetImportResult Import(string sourcePath) => new("Imported", "imported.srs");
        public void Delete(UserRuleSetModel ruleSet) => Deleted.Add(ruleSet.FileName);
        public Task<RuleSetDownloadResult> DownloadBuiltinAsync(UserRuleSetModel ruleSet, bool useConditionalRequest, CancellationToken cancellationToken = default)
        {
            Downloads.Add(ruleSet);
            ConditionalRequest = useConditionalRequest;
            return Download.Task;
        }
    }

    private sealed class ListedApps(IReadOnlyList<AppCandidate> installed, IReadOnlyList<AppCandidate> running) : IAppDiscoveryPort
    {
        public IReadOnlyList<string> SelectedPaths { get; private set; } = [];
        public Task<IReadOnlyList<AppCandidate>> GetInstalledCandidatesAsync(IEnumerable<string>? alreadySelectedPaths, CancellationToken cancellationToken)
        {
            SelectedPaths = alreadySelectedPaths?.ToArray() ?? [];
            return Task.FromResult(installed);
        }
        public Task<IReadOnlyList<AppCandidate>> GetRunningCandidatesAsync(IEnumerable<string>? alreadySelectedPaths, CancellationToken cancellationToken) => Task.FromResult(running);
    }

    private sealed class EmptyApps : IAppDiscoveryPort
    {
        public Task<IReadOnlyList<AppCandidate>> GetInstalledCandidatesAsync(IEnumerable<string>? alreadySelectedPaths, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AppCandidate>>([]);
        public Task<IReadOnlyList<AppCandidate>> GetRunningCandidatesAsync(IEnumerable<string>? alreadySelectedPaths, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AppCandidate>>([]);
    }
}
