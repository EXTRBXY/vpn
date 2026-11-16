using System.Text.Json;
using System.Text.Json.Nodes;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Profile;
using NothingVpn.Infrastructure.Xray;

namespace NothingVpn.Application.Tests;

public sealed class AdvancedVlessLinkTests
{
    private const string Prefix = "vless://11111111-2222-3333-4444-555555555555@node.example:8443?type=xhttp&security=tls";
    // Synthetic public key; never use credentials from an imported profile in tests.
    private static string Encryption => "mlkem768x25519plus.native.0rtt." + Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void Parse_ExporterExtraModeAndPadding()
    {
        var p = VlessLinkParser.Parse(Prefix + "&mode=auto&extra=" + Uri.EscapeDataString("""{"mode":"auto","xPaddingBytes":"100-1000"}""") + "&x_padding_bytes=100-1000");
        Assert.Equal(XhttpMode.Auto, p.XhttpMode);
        Assert.Equal("100-1000", p.XhttpExtra!.XPaddingBytes);
    }

    [Fact]
    public void Parse_ExtraModeFallbackAndConflict()
    {
        var extra = "&extra=" + Uri.EscapeDataString("""{"mode":"stream-up"}""");
        Assert.Equal(XhttpMode.StreamUp, VlessLinkParser.Parse(Prefix + extra).XhttpMode);
        Assert.Throws<ArgumentException>(() => VlessLinkParser.Parse(Prefix + "&mode=auto" + extra));
    }

    [Fact]
    public void Parse_EncryptionPreservesCaseAndRoundtrips()
    {
        var p = VlessLinkParser.Parse(Prefix + "&encryption=" + Encryption);
        Assert.Equal(Encryption, p.Encryption);
        Assert.Equal(Encryption, VlessLinkParser.Parse(VlessLinkFormatter.Build(p)).Encryption);
    }

    [Theory]
    [InlineData("mode=auto&extra=%7B%22mode%22%3A1%7D")]
    [InlineData("x_padding_bytes=9-1")]
    [InlineData("x_padding_bytes=100&extra=%7B%22xPaddingBytes%22%3A%22200%22%7D")]
    [InlineData("encryption=mlkem768x25519plus.native.0rtt.bad-key")]
    [InlineData("encryption=unknown")]
    [InlineData("ech=invalid")]
    [InlineData("ech=AAH%2BDQ%3D%3D%0A")]
    public void Parse_RejectsInvalidSecurityAndConflictingExtras(string query)
    {
        Assert.Throws<ArgumentException>(() => VlessLinkParser.Parse(Prefix + "&" + query));
    }

    // ECHConfigList 0xfe0d с public_name; ключ синтетический.
    private const string AcceptableEch = "AEH+DQA9AAAgACABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fIAAEAAEAASAOcHVibGljLmV4YW1wbGUAAA==";

    [Fact]
    public void EngineSelection_RequiresXrayForEncryptionAndEch()
    {
        Assert.Equal(OutboundEngine.Xray, NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.Auto, "tcp", "tls", Encryption));
        Assert.Equal(OutboundEngine.Xray, NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.Auto, "ws", "tls", "none", AcceptableEch));
        Assert.Equal(OutboundEngine.SingBox, NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.Auto, "tcp", "tls", "none", "AAH+DQ=="));
        Assert.Equal(OutboundEngine.SingBox, NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.Auto, "tcp", "tls", "none", "ADP+DQAvAAAgACABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fIAAEAAEAAQAAAAA="));
        Assert.Throws<ArgumentException>(() => NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.SingBox, "tcp", "tls", Encryption));
        Assert.Throws<ArgumentException>(() => NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.SingBox, "tcp", "tls", "none", AcceptableEch));
        NothingVpn.Domain.Policies.OutboundEnginePolicy.Resolve(OutboundEngine.SingBox, "tcp", "tls", "none", "AAH+DQ==");
    }

    [Fact]
    public void Parse_RejectsControlCharactersInsideEncryption()
    {
        Assert.Throws<ArgumentException>(() => VlessLinkParser.Parse(Prefix + "&encryption=" + Uri.EscapeDataString(Encryption + "\n" + "." + Encryption.Split('.').Last())));
    }

    [Fact]
    public void NewEncryptionProfiles_HaveDistinctIds()
    {
        var plain = VlessLinkParser.Parse(Prefix);
        var encrypted = VlessLinkParser.Parse(Prefix + "&encryption=" + Encryption);
        Assert.NotEqual(plain.Id, encrypted.Id);
        Assert.Equal(encrypted.Id, VlessLinkParser.Parse(VlessLinkFormatter.Build(encrypted)).Id);
    }

    [Fact]
    public void Logs_RedactXrayEncryptionErrors()
    {
        var message = NothingVpn.Infrastructure.Security.LogRedactor.Redact("unsupported encryption: " + Encryption);
        Assert.DoesNotContain(Encryption, message);
        Assert.Contains("mlkem768x25519plus.***", message);
        Assert.Equal("ech=***", NothingVpn.Infrastructure.Security.LogRedactor.Redact("ech=AAH+DQ=="));
    }

    [Fact]
    public void ServiceContractAndXray_PreserveEncryptionAndEch()
    {
        var p = VlessLinkParser.Parse(Prefix);
        p.Encryption = Encryption;
        p.ExtraQuery["ech"] = AcceptableEch;
        var request = EngineRequestMapper.CreateRequest(p, new AppStateModel());
        var restored = JsonSerializer.Deserialize<EngineConnectRequest>(JsonSerializer.Serialize(request))!;
        var mapped = EngineRequestMapper.ToProfile(restored);
        var config = JsonNode.Parse(XrayConfigGenerator.Serialize(mapped, new AppStateModel(), new LocalSocksUpstream(41000, "test", "test", "xray.exe")))!;
        Assert.Equal(Encryption, (string?)config["outbounds"]![0]!["settings"]!["encryption"]);
        Assert.Equal(AcceptableEch, (string?)config["outbounds"]![0]!["streamSettings"]!["tlsSettings"]!["echConfigList"]);
        mapped.ExtraQuery["ech"] = "AAH+DQ==";
        var rejected = JsonNode.Parse(XrayConfigGenerator.Serialize(mapped, new AppStateModel(), new LocalSocksUpstream(41000, "test", "test", "xray.exe")))!;
        Assert.Null(rejected["outbounds"]![0]!["streamSettings"]!["tlsSettings"]!["echConfigList"]);
        Assert.Equal("none", new EngineProfile().Encryption);
    }
}
