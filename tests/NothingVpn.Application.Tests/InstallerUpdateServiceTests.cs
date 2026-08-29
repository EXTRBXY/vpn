using NothingVpn.Infrastructure.Updates;

namespace NothingVpn.Application.Tests;

public sealed class InstallerUpdateServiceTests
{
    [Fact]
    public void GetCachedInstallerPath_NormalizesVersionAndUsesDedicatedDirectory()
    {
        var service = new InstallerUpdateService();

        var path = service.GetCachedInstallerPath("v12.34.5");

        Assert.Equal("NothingVpnSetup-12.34.5.exe", Path.GetFileName(path));
        Assert.Equal("NothingVpn", Path.GetFileName(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void GetCachedInstallerPath_RejectsInvalidVersion()
    {
        var service = new InstallerUpdateService();

        Assert.Throws<ArgumentException>(() => service.GetCachedInstallerPath("latest"));
    }
}
