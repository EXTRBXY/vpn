using System.Diagnostics;
using NothingVpn.Infrastructure.Updates;

namespace NothingVpn.Application.Tests;

public sealed class InstallerLaunchServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NothingVpnLaunchTests", Guid.NewGuid().ToString("N"));

    public InstallerLaunchServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { }
    }

    [Fact]
    public void ElevatedClient_RefusesWithoutStartingInstaller()
    {
        var starts = new List<ProcessStartInfo>();
        var service = new InstallerLaunchService(() => true, starts.Add);
        var installer = CreateInstaller("NothingVpnSetup-1.2.3.exe");

        var ensure = Assert.Throws<InvalidOperationException>(service.EnsureLaunchAllowed);
        Assert.Throws<InvalidOperationException>(() => service.Prepare(installer));
        Assert.Throws<InvalidOperationException>(() => service.Launch(installer));

        Assert.Equal(InstallerLaunchService.ElevatedClientMessage, ensure.Message);
        Assert.Empty(starts);
    }

    [Fact]
    public void DefaultService_RefusesExactlyWhenProcessIsElevated()
    {
        var service = new InstallerLaunchService();

        if (Environment.IsPrivilegedProcess)
            Assert.Throws<InvalidOperationException>(service.EnsureLaunchAllowed);
        else
            service.EnsureLaunchAllowed();
    }

    [Fact]
    public void Launch_StartsInstallerDirectlyWithSilentUpgradeArguments()
    {
        var starts = new List<ProcessStartInfo>();
        var service = new InstallerLaunchService(() => false, starts.Add);
        var installer = CreateInstaller("NothingVpnSetup-1.2.3.exe");

        service.Launch(service.Prepare(installer));

        var info = Assert.Single(starts);
        Assert.Equal(Path.GetFullPath(installer), info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.True(string.IsNullOrEmpty(info.Verb));
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Equal(
            new[] { "/SILENT", "/SP-", "/SUPPRESSMSGBOXES", "/CLOSEAPPLICATIONS", "/NORESTART" },
            info.ArgumentList);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("NothingVpnSetup-1.2.3.exe.cmd")]
    [InlineData("NothingVpnSetup-1.2.3.ps1")]
    public void Launch_UnexpectedInstallerName_IsRejected(string fileName)
    {
        var starts = new List<ProcessStartInfo>();
        var service = new InstallerLaunchService(() => false, starts.Add);
        var installer = CreateInstaller(fileName);

        Assert.Throws<InvalidOperationException>(() => service.Prepare(installer));
        Assert.Throws<InvalidOperationException>(() => service.Launch(installer));
        Assert.Empty(starts);
    }

    [Fact]
    public void Launch_MissingInstaller_IsRejected()
    {
        var starts = new List<ProcessStartInfo>();
        var service = new InstallerLaunchService(() => false, starts.Add);

        Assert.Throws<FileNotFoundException>(() => service.Launch(Path.Combine(_directory, "NothingVpnSetup-9.9.9.exe")));
        Assert.Empty(starts);
    }

    [Theory]
    [InlineData("O'Brien %TEMP% & тест")]
    [InlineData("$(whoami);`literal`")]
    public void InstallerPath_IsDataNotCommandText(string folder)
    {
        var starts = new List<ProcessStartInfo>();
        var service = new InstallerLaunchService(() => false, starts.Add);
        Directory.CreateDirectory(Path.Combine(_directory, folder));
        var installer = CreateInstaller(Path.Combine(folder, "NothingVpnSetup-1.2.3.exe"));

        service.Launch(installer);

        var info = Assert.Single(starts);
        Assert.Equal(Path.GetFullPath(installer), info.FileName);
        Assert.DoesNotContain(info.ArgumentList, argument => argument.Contains(folder, StringComparison.Ordinal));
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void StartInfo_DoesNotPassClientDevelopmentVariablesToInstaller()
    {
        var name = "NOTHINGVPN_LAUNCH_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "inprocess");
        try
        {
            var info = InstallerLaunchService.BuildStartInfo(@"C:\Temp\NothingVpnSetup-1.2.3.exe");

            Assert.False(info.Environment.ContainsKey(name));
            Assert.DoesNotContain(info.Environment.Keys, key => key.StartsWith("NOTHINGVPN_", StringComparison.OrdinalIgnoreCase));
            Assert.True(info.Environment.ContainsKey("SystemRoot"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private string CreateInstaller(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        File.WriteAllBytes(path, []);
        return path;
    }
}
