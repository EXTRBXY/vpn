using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Profile;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

/// <summary>NothingX: VLESS + XHTTP через Xray рядом с sing-box.</summary>
public sealed class XhttpSupportTests : IDisposable
{
    private const string XhttpLink =
        "vless://8bb8fea6-8c88-4c34-98fb-1774bbd329b1@203.0.113.10:443?type=xhttp&security=reality" +
        "&pbk=su4EtqcPddsMNxnVl50CquTGw9Nu0NzTTTI56nnQdko&fp=chrome&sni=example.com&sid=f50b45d22d8d3092" +
        "&spx=%2F&path=%2Fabc&mode=auto&flow=xtls-rprx-vision#x";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nothingx-xhttp-{Guid.NewGuid():N}");

    private AppPaths Paths()
    {
        var configs = Path.Combine(_root, "configs");
        Directory.CreateDirectory(configs);
        return new AppPaths(_root, configs, Path.Combine(_root, "rulesets"), Path.Combine(_root, "logs"), "", "", "");
    }

    [Fact]
    public void Parser_AcceptsXhttp_AndDropsVisionFlow()
    {
        var p = VlessLinkParser.Parse(XhttpLink);

        Assert.Equal("xhttp", p.Type);
        Assert.Equal("/abc", p.WsPath);
        Assert.Equal("auto", p.XhttpMode);
        Assert.Null(p.Flow);
    }

    [Fact]
    public void Parser_MapsSplitHttpAlias_AndRejectsUnknownMode()
    {
        Assert.Equal("xhttp", VlessLinkParser.Parse(XhttpLink.Replace("type=xhttp", "type=splithttp")).Type);
        Assert.Throws<ArgumentException>(() => VlessLinkParser.Parse(XhttpLink.Replace("mode=auto", "mode=bogus")));
        Assert.Throws<ArgumentException>(() => VlessLinkParser.Parse(XhttpLink.Replace("#x", "&extra=not-json#x")));
    }

    [Fact]
    public void Parser_KeepsStableIdOfNonXhttpProfiles()
    {
        var tcp = XhttpLink.Replace("type=xhttp", "type=tcp");
        var withMode = VlessLinkParser.Parse(tcp);
        var withoutMode = VlessLinkParser.Parse(tcp.Replace("&mode=auto", ""));
        Assert.Equal(withoutMode.Id, withMode.Id);
    }

    [Fact]
    public void Generator_UsesLocalSocksToXray_AndExcludesXrayFromTun()
    {
        var p = VlessLinkParser.Parse(XhttpLink);
        var path = SingBoxConfigGenerator.WriteConfig(Paths(), p, new AppStateModel { Mode = ConnectionMode.Tun });

        using var singBox = JsonDocument.Parse(File.ReadAllText(path));
        var proxy = singBox.RootElement.GetProperty("outbounds")[0];
        Assert.Equal("socks", proxy.GetProperty("type").GetString());
        Assert.Equal("127.0.0.1", proxy.GetProperty("server").GetString());
        Assert.False(string.IsNullOrEmpty(proxy.GetProperty("password").GetString()));

        var rules = singBox.RootElement.GetProperty("route").GetProperty("rules").GetRawText();
        Assert.Contains(XrayConfigGenerator.ExeName, rules, StringComparison.Ordinal);
        Assert.Contains("203.0.113.10/32", rules, StringComparison.Ordinal);

        var xrayPath = XrayConfigGenerator.ConfigPathFor(path);
        using var xray = JsonDocument.Parse(File.ReadAllText(xrayPath));
        var inbound = xray.RootElement.GetProperty("inbounds")[0];
        Assert.Equal(proxy.GetProperty("server_port").GetInt32(), inbound.GetProperty("port").GetInt32());
        Assert.Equal("127.0.0.1", inbound.GetProperty("listen").GetString());
        var stream = xray.RootElement.GetProperty("outbounds")[0].GetProperty("streamSettings");
        Assert.Equal("xhttp", stream.GetProperty("network").GetString());
        Assert.Equal("/abc", stream.GetProperty("xhttpSettings").GetProperty("path").GetString());
        Assert.Equal("example.com", stream.GetProperty("realitySettings").GetProperty("serverName").GetString());
        Assert.True(stream.GetProperty("xhttpSettings").GetProperty("extra").TryGetProperty("xmux", out _));
    }

    [Fact]
    public void XrayConfig_KeepsXmuxFromLink()
    {
        var link = XhttpLink.Replace("#x", "&extra=" + Uri.EscapeDataString("{\"xmux\":{\"maxConnections\":1}}") + "#x");
        var p = VlessLinkParser.Parse(link);
        var json = XrayConfigGenerator.Build(p, new AppStateModel(), XrayConfigGenerator.CreateSidecar());

        using var doc = JsonDocument.Parse(json);
        var xmux = doc.RootElement.GetProperty("outbounds")[0].GetProperty("streamSettings")
            .GetProperty("xhttpSettings").GetProperty("extra").GetProperty("xmux");
        Assert.Equal(1, xmux.GetProperty("maxConnections").GetInt32());
        Assert.False(xmux.TryGetProperty("maxConcurrency", out _));
    }

    [Fact]
    public void Generator_RemovesStaleXrayConfig_ForTcpProfile()
    {
        var paths = Paths();
        var xhttp = VlessLinkParser.Parse(XhttpLink);
        var tcp = VlessLinkParser.Parse(XhttpLink.Replace("type=xhttp", "type=tcp"));
        tcp.Id = xhttp.Id;

        SingBoxConfigGenerator.WriteConfig(paths, xhttp, new AppStateModel { Mode = ConnectionMode.Proxy });
        var path = SingBoxConfigGenerator.WriteConfig(paths, tcp, new AppStateModel { Mode = ConnectionMode.Proxy });

        Assert.False(File.Exists(XrayConfigGenerator.ConfigPathFor(path)));
        Assert.Contains("\"vless\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
