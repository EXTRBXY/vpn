using System.Text;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Application.Tests;

public sealed class DpapiJsonFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "NothingVpn.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_ReplacesFileAtomicallyAndKeepsPreviousVersionAsBackup()
    {
        var path = GetPath();
        DpapiJsonFile.Save(path, new TestPayload { Value = "first" }, Protect);
        DpapiJsonFile.Save(path, new TestPayload { Value = "second" }, Protect);
        DpapiJsonFile.Save(path, new TestPayload { Value = "third" }, Protect);

        var current = Load(path);
        var previous = Load(path + ".bak");

        Assert.Equal("third", current.Value);
        Assert.Equal("second", previous.Value);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Load_RestoresBackupWhenPrimaryFileIsCorrupted()
    {
        var path = GetPath();
        DpapiJsonFile.Save(path, new TestPayload { Value = "recover-me" }, Protect);
        DpapiJsonFile.Save(path, new TestPayload { Value = "new-value" }, Protect);
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

        var recovered = Load(path);
        var recoveredAgain = Load(path);

        Assert.Equal("recover-me", recovered.Value);
        Assert.Equal("recover-me", recoveredAgain.Value);
    }

    [Fact]
    public void Load_MigratesLegacyPlaintextToEncryptedFormat()
    {
        var path = GetPath();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{\"value\":\"legacy\"}", Encoding.UTF8);

        var loaded = Load(path);

        Assert.Equal("legacy", loaded.Value);
        Assert.Equal("NV1", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 3));
    }

    private string GetPath() => Path.Combine(_directory, "data.json");

    private static TestPayload Load(string path) =>
        DpapiJsonFile.LoadOrDefault(path, () => new TestPayload(), Protect, Unprotect);

    private static byte[] Protect(byte[] data) => data.Reverse().ToArray();

    private static byte[] Unprotect(byte[] data) => data.Reverse().ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestPayload
    {
        public string Value { get; set; } = string.Empty;
    }
}
