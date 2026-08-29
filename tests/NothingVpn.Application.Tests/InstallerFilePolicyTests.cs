using NothingVpn.Domain.Updates;

namespace NothingVpn.Application.Tests;

public sealed class InstallerFilePolicyTests
{
    [Theory]
    [InlineData("NothingVpnSetup.exe")]
    [InlineData("NothingVpnSetup-1.2.3.exe")]
    [InlineData(@"C:\Temp\NothingVpnSetup-2.0.0.EXE")]
    public void IsAcceptedFileName_AcceptsReleaseAndCacheNames(string path)
    {
        Assert.True(InstallerFilePolicy.IsAcceptedFileName(path));
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("NothingVpnSetup-1.2.3.zip")]
    [InlineData("")]
    public void IsAcceptedFileName_RejectsUnexpectedNames(string path)
    {
        Assert.False(InstallerFilePolicy.IsAcceptedFileName(path));
    }

    [Fact]
    public void ValidateExistingInstaller_ReturnsAbsolutePathForValidFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nothingvpn-installer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "NothingVpnSetup-9.9.9.exe");
        File.WriteAllBytes(path, [1]);
        try
        {
            Assert.Equal(Path.GetFullPath(path), InstallerFilePolicy.ValidateExistingInstaller(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
