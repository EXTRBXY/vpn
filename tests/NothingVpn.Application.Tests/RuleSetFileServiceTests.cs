using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.RuleSets;

namespace NothingVpn.Application.Tests;

public sealed class RuleSetFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nothingvpn-rules-{Guid.NewGuid():N}");

    [Fact]
    public void Import_DuplicateName_CreatesUniqueCopyThatCanBeDeleted()
    {
        Directory.CreateDirectory(_root);
        var sourceDirectory = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var source = Path.Combine(sourceDirectory, "custom.srs");
        File.WriteAllText(source, "rules");
        var service = CreateService();

        var first = service.Import(source);
        var second = service.Import(source);

        Assert.Equal("custom.srs", first.FileName);
        Assert.StartsWith("custom-", second.FileName);
        Assert.EndsWith(".srs", second.FileName);
        var firstRule = new UserRuleSetModel { FileName = first.FileName };
        Assert.True(service.Exists(firstRule));
        service.Delete(firstRule);
        Assert.False(service.Exists(firstRule));
        Assert.True(service.Exists(new UserRuleSetModel { FileName = second.FileName }));
    }

    [Fact]
    public void FileOperations_RejectPathTraversal()
    {
        var service = CreateService();
        var rule = new UserRuleSetModel { FileName = @"..\outside.srs" };

        Assert.False(service.Exists(rule));
        Assert.Throws<InvalidOperationException>(() => service.Delete(rule));
    }

    [Fact]
    public void Import_RejectsNonRuleSetFile()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "notes.txt");
        File.WriteAllText(source, "not a rule set");
        var service = CreateService();

        Assert.Throws<InvalidOperationException>(() => service.Import(source));
    }

    private RuleSetFileService CreateService() => new(new FakeAppPathsPort(_root));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeAppPathsPort(string root) : IAppPathsPort
    {
        public AppPathsModel Get() => new()
        {
            BaseDir = root,
            ConfigsDir = Path.Combine(root, "configs"),
            RuleSetsDir = Path.Combine(root, "rules"),
            LogsDir = Path.Combine(root, "logs"),
            ProfilesJsonPath = Path.Combine(root, "profiles.json"),
            SubscriptionsJsonPath = Path.Combine(root, "subscriptions.json"),
            StateJsonPath = Path.Combine(root, "state.json")
        };
    }
}
