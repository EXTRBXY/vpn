using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class RuleSetManagementControllerTests
{
    [Fact]
    public void Load_SeparatesBuiltinAndUserRulesPreservingOrder()
    {
        var state = new AppStateModel
        {
            UserRuleSets =
            [
                Rule("builtin-1", "catalog"),
                Rule("user-1"),
                Rule("builtin-2", "catalog-2"),
                Rule("user-2")
            ]
        };
        var controller = new RuleSetManagementController(new FakeSettingsService());

        var snapshot = controller.Load(state);

        Assert.Equal(new[] { "builtin-1", "builtin-2" }, snapshot.Builtin.Select(x => x.Tag));
        Assert.Equal(new[] { "user-1", "user-2" }, snapshot.User.Select(x => x.Tag));
    }

    [Fact]
    public void Save_StoresBuiltinBeforeUserAndPersists()
    {
        var settings = new FakeSettingsService();
        var state = new AppStateModel();
        var controller = new RuleSetManagementController(settings);

        controller.Save(state, new[] { Rule("builtin", "catalog") }, new[] { Rule("user") });

        Assert.Equal(new[] { "builtin", "user" }, state.UserRuleSets.Select(x => x.Tag));
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void CreateUserRuleSet_NormalizesMetadataAndCreatesUniqueTags()
    {
        var controller = new RuleSetManagementController(new FakeSettingsService());

        var first = controller.CreateUserRuleSet("  My rules  ", @"C:\Downloads\rules.srs");
        var second = controller.CreateUserRuleSet("My rules", "rules.srs");

        Assert.Equal("My rules", first.Name);
        Assert.Equal("rules.srs", first.FileName);
        Assert.StartsWith("user-ruleset-", first.Tag);
        Assert.NotEqual(first.Tag, second.Tag);
        Assert.True(first.Enabled);
        Assert.Equal("direct", first.Action);
    }

    [Fact]
    public void MarkBuiltinFilesRemoved_DisablesAndClearsDownloadMetadata()
    {
        var settings = new FakeSettingsService();
        var state = new AppStateModel();
        var builtin = Rule("builtin", "catalog");
        builtin.Enabled = true;
        builtin.RemoteEtag = "etag";
        builtin.LastDownloadedUtc = DateTimeOffset.UtcNow;
        var controller = new RuleSetManagementController(settings);

        controller.MarkBuiltinFilesRemoved(state, new[] { builtin }, Array.Empty<UserRuleSetModel>(), new[] { builtin });

        Assert.False(builtin.Enabled);
        Assert.Null(builtin.RemoteEtag);
        Assert.Null(builtin.LastDownloadedUtc);
        Assert.Equal(1, settings.SaveCalls);
    }

    private static UserRuleSetModel Rule(string tag, string? builtinId = null) => new()
    {
        Tag = tag,
        Name = tag,
        FileName = $"{tag}.srs",
        BuiltinId = builtinId
    };

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
