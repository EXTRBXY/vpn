using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Desktop.Wpf;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.TunApps;
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
        vm.SelectedTunApp = vm.TunApps[index];
        vm.RemoveSelectedTunApp();
        var expected = Paths.Where((_, i) => i != index).ToArray();
        Assert.Equal(expected, vm.TunApps.Select(x => x.Path));
        Assert.Equal(Paths, h.Store.State.TunAppProcessPaths);
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, h.Store.SaveCalls);
        Assert.Equal(expected, h.Create().TunApps.Select(x => x.Path));
    }

    [Fact]
    public void RemoveAll_Save_Reopen_RemainsEmpty()
    {
        var h = new Harness();
        var vm = h.Create();
        while (vm.TunApps.Count > 0)
        {
            vm.SelectedTunApp = vm.TunApps[0];
            vm.RemoveSelectedTunApp();
        }
        vm.SaveCommand.Execute(null);
        Assert.Empty(h.Create().TunApps);
    }

    [Fact]
    public void Add_RejectsDuplicatesAndInvalidPaths_WithoutLosingExistingApps()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.AddTunApp(Paths[0].ToUpperInvariant());
        Assert.Contains("уже", vm.Message);
        vm.AddTunApp("not-an-executable.txt");
        Assert.Equal(Paths, vm.TunApps.Select(x => x.Path));
        vm.AddTunApp(@"C:\Apps\four.exe");
        vm.SaveCommand.Execute(null);
        Assert.Equal(4, h.Create().TunApps.Count);
    }

    [Fact]
    public void EditingDns_UpdatesPreset_AndAllowsReapplyingIt()
    {
        var vm = new Harness().Create();
        Assert.Equal("google", vm.DnsPreset);
        vm.DohServer = "8.8.4.4";
        Assert.Equal("custom", vm.DnsPreset);
        vm.DnsPreset = "google";
        Assert.Equal("8.8.8.8", vm.DohServer);
        Assert.Equal("google", vm.DnsPreset);
    }

    [Fact]
    public void Navigation_PreservesDraft_AndRefreshesDnsModeConstraint()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.InterfaceName = "Edited";
        vm.MtuText = "1400";
        vm.AddTunApp(@"C:\Apps\four.exe");
        vm.BuiltinRuleSets[0].Action = "block";
        Assert.Equal("direct", h.Store.State.UserRuleSets[0].Action);
        h.Settings.UpdateMode("tun_apps");
        vm.DnsDetour = "proxy";
        vm.Reload();
        Assert.Equal("Edited", vm.InterfaceName);
        Assert.Equal("1400", vm.MtuText);
        Assert.Equal(4, vm.TunApps.Count);
        Assert.Equal("block", vm.BuiltinRuleSets[0].Action);
        Assert.False(vm.IsDnsDetourEditable);
        Assert.Equal("direct", vm.DnsDetour);
    }

    [Fact]
    public void SaveAll_WritesOnce_AndPreservesBackgroundAndConnectionFields()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.InterfaceName = "Edited";
        vm.MtuText = "1400";
        vm.CloseBehavior = "exit";
        vm.LogLevel = "debug";
        vm.BuiltinRuleSets[0].Action = "block";
        vm.AddTunApp(@"C:\Apps\four.exe");
        h.Settings.UpdateState(state =>
        {
            state.UpdateDismissedModalForTag = "v9.9.9";
            state.ActiveProfileId = "changed-in-background";
            state.ProxyWasEnabledByUs = true;
        });
        var before = h.Store.SaveCalls;
        vm.SaveCommand.Execute(null);
        Assert.Equal(before + 1, h.Store.SaveCalls);
        var state = h.Store.State;
        Assert.Equal("Edited", state.TunInterfaceName);
        Assert.Equal(1400, state.TunMtu);
        Assert.Equal("exit", state.CloseBehavior);
        Assert.Equal("debug", state.SingBoxLogLevel);
        Assert.Equal("block", state.UserRuleSets[0].Action);
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
        vm.SelectedBuiltinRuleSet = vm.BuiltinRuleSets[0];
        vm.RemoveSelectedBuiltin();
        vm.MtuText = mtu;
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
        vm.DohServer = "";
        vm.CloseBehavior = "exit";
        vm.SelectedTunApp = vm.TunApps[0];
        vm.RemoveSelectedTunApp();
        vm.SaveCommand.Execute(null);
        Assert.Equal(0, h.Store.SaveCalls);
        Assert.Equal(Paths, h.Store.State.TunAppProcessPaths);
        Assert.Equal("tray", h.Store.State.CloseBehavior);
        Assert.Equal(2, vm.TunApps.Count);
        Assert.Contains("DoH", vm.Message);
    }

    [Fact]
    public void FailedWrite_KeepsDraftAndFiles_ForRetry()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.SelectedUserRuleSet = vm.UserRuleSets[0];
        vm.RemoveSelectedRuleSet();
        h.Store.FailWrites = true;
        vm.SaveCommand.Execute(null);
        Assert.Empty(h.Files.Deleted);
        Assert.Empty(vm.UserRuleSets);
        Assert.Contains("write failed", vm.Message);
        h.Store.FailWrites = false;
        vm.SaveCommand.Execute(null);
        Assert.Single(h.Files.Deleted);
        Assert.Empty(h.Create().UserRuleSets);
    }

    [Fact]
    public async Task Download_MissingFile_DoesNotUseCachedEtag()
    {
        var h = new Harness();
        h.Files.FileExists = false;
        var vm = h.Create();
        vm.SelectedBuiltinRuleSet = vm.BuiltinRuleSets[0];
        vm.SelectedBuiltinRuleSet.RemoteEtag = "old";
        var task = vm.DownloadSelectedBuiltinAsync();
        Assert.False(h.Files.ConditionalRequest);
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, false, null, null));
        await task;
        Assert.Null(vm.BuiltinRuleSets[0].RemoteEtag);
    }

    [Fact]
    public async Task Download_CapturesOriginalSelection_AndOnlyEditsDraft()
    {
        var h = new Harness();
        var vm = h.Create();
        vm.SelectedBuiltinRuleSet = vm.BuiltinRuleSets[0];
        var download = vm.DownloadSelectedBuiltinAsync();
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SelectedBuiltinRuleSet = vm.BuiltinRuleSets[1];
        vm.RemoveSelectedBuiltin();
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, false, "new-etag", null));
        await download;
        Assert.Equal("new-etag", vm.BuiltinRuleSets[0].RemoteEtag);
        Assert.Null(vm.BuiltinRuleSets[1].RemoteEtag);
        Assert.Equal(0, h.Store.SaveCalls);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Equal("new-etag", h.Create().BuiltinRuleSets[0].RemoteEtag);
        Assert.Empty(h.Files.Deleted);
    }

    internal sealed class Harness
    {
        public MemoryStore Store { get; } = new();
        public FakeFiles Files { get; } = new();
        public SettingsService Settings { get; }
        public Harness() => Settings = new SettingsService(Store, new PathPolicyPort());
        public SettingsViewModel Create() => new(
            new ConnectionSettingsController(Settings), new TunAppsController(new PathPolicyPort(), Settings),
            new RuleSetManagementController(Settings), Files, new TunAppsSelectionService(new EmptyApps(), new EmptyApps()),
            Settings, Settings.GetState(), new UpdateViewModel(null!, null!, null!, Settings, Settings.GetState(), () => { }));
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
        public bool Exists(UserRuleSetModel ruleSet) => FileExists;
        public RuleSetImportResult Import(string sourcePath) => new("Imported", "imported.srs");
        public void Delete(UserRuleSetModel ruleSet) => Deleted.Add(ruleSet.FileName);
        public Task<RuleSetDownloadResult> DownloadBuiltinAsync(UserRuleSetModel ruleSet, bool useConditionalRequest, CancellationToken cancellationToken = default)
        {
            ConditionalRequest = useConditionalRequest;
            return Download.Task;
        }
    }

    private sealed class EmptyApps : IInstalledAppsProvider, IRunningAppsProvider
    {
        public Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AppCandidate>>([]);
        public Task<IReadOnlyList<AppCandidate>> GetRunningAppsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AppCandidate>>([]);
    }
}
