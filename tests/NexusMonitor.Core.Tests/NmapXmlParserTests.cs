using FluentAssertions;
using NexusMonitor.Core.Network;
using Xunit;

namespace NexusMonitor.Core.Tests;

public class NmapXmlParserTests
{
    // ── XML building blocks ────────────────────────────────────────────────

    private static string MinimalUpHost(string ip = "192.168.1.1") => $@"
<nmaprun>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""{ip}""/>
  </host>
</nmaprun>";

    private static string FullHost() => @"
<nmaprun>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""192.168.1.1""/>
    <address addrtype=""mac"" addr=""AA:BB:CC:DD:EE:FF""/>
    <hostnames>
      <hostname type=""PTR"" name=""router.local""/>
    </hostnames>
    <times srtt=""5000""/>
    <os>
      <osmatch name=""Windows 11"" accuracy=""98""/>
      <osmatch name=""Linux 5.x"" accuracy=""85""/>
    </os>
    <ports>
      <port portid=""80"" protocol=""tcp"">
        <state state=""open""/>
        <service name=""http"" product=""Apache"" version=""2.4""/>
      </port>
    </ports>
  </host>
</nmaprun>";

    // ── Basic parsing ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyXml_ReturnsEmptyHosts()
    {
        var result = NmapXmlParser.Parse("<nmaprun/>", TimeSpan.Zero);

        result.Hosts.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmptyResult()
    {
        var result = NmapXmlParser.Parse("not xml", TimeSpan.Zero);

        result.Hosts.Should().BeEmpty();
    }

    [Fact]
    public void Parse_HostDown_Excluded()
    {
        const string xml = @"
<nmaprun>
  <host>
    <status state=""down""/>
    <address addrtype=""ipv4"" addr=""192.168.1.1""/>
  </host>
</nmaprun>";

        var result = NmapXmlParser.Parse(xml, TimeSpan.Zero);

        result.Hosts.Should().BeEmpty();
    }

    [Fact]
    public void Parse_HostUp_Included()
    {
        var result = NmapXmlParser.Parse(MinimalUpHost(), TimeSpan.Zero);

        result.Hosts.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_ElapsedPassedThrough()
    {
        var elapsed = TimeSpan.FromSeconds(5);

        var result = NmapXmlParser.Parse("<nmaprun/>", elapsed);

        result.Elapsed.Should().Be(elapsed);
    }

    [Fact]
    public void Parse_RawXmlPreserved()
    {
        var xml = MinimalUpHost();

        var result = NmapXmlParser.Parse(xml, TimeSpan.Zero);

        result.RawXml.Should().Be(xml);
    }

    // ── Field extraction ───────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsIpAddress()
    {
        var result = NmapXmlParser.Parse(MinimalUpHost("10.0.0.5"), TimeSpan.Zero);

        result.Hosts[0].IpAddress.Should().Be("10.0.0.5");
    }

    [Fact]
    public void Parse_ExtractsMacAddress()
    {
        var result = NmapXmlParser.Parse(FullHost(), TimeSpan.Zero);

        result.Hosts[0].MacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void Parse_ExtractsHostname_PtrType()
    {
        const string xml = @"
<nmaprun>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""192.168.1.1""/>
    <hostnames>
      <hostname type=""user"" name=""ignored""/>
      <hostname type=""PTR"" name=""expected.local""/>
    </hostnames>
  </host>
</nmaprun>";

        var result = NmapXmlParser.Parse(xml, TimeSpan.Zero);

        result.Hosts[0].Hostname.Should().Be("expected.local");
    }

    [Fact]
    public void Parse_ExtractsLatency_DividedBy1000()
    {
        const string xml = @"
<nmaprun>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""192.168.1.1""/>
    <times srtt=""5000""/>
  </host>
</nmaprun>";

        var result = NmapXmlParser.Parse(xml, TimeSpan.Zero);

        result.Hosts[0].Latency.Should().BeApproximately(5.0, 0.001);
    }

    [Fact]
    public void Parse_ExtractsOsGuess_HighestAccuracy()
    {
        var result = NmapXmlParser.Parse(FullHost(), TimeSpan.Zero);

        // Windows 11 has accuracy 98, Linux 5.x has 85 — highest should win
        result.Hosts[0].OsGuess.Should().Be("Windows 11");
    }

    [Fact]
    public void Parse_NoOsGuess_EmptyString()
    {
        var result = NmapXmlParser.Parse(MinimalUpHost(), TimeSpan.Zero);

        result.Hosts[0].OsGuess.Should().BeEmpty();
    }

    // ── Port parsing ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsPorts_OpenTcp()
    {
        var result = NmapXmlParser.Parse(FullHost(), TimeSpan.Zero);

        result.Hosts[0].Ports.Should().HaveCount(1);
        var port = result.Hosts[0].Ports[0];
        port.Number.Should().Be(80);
        port.Protocol.Should().Be("tcp");
        port.State.Should().Be("open");
        port.Service.Should().Be("http");
    }

    [Fact]
    public void Parse_PortVersion_ProductAndVersionCombined()
    {
        var result = NmapXmlParser.Parse(FullHost(), TimeSpan.Zero);

        result.Hosts[0].Ports[0].Version.Should().Be("Apache 2.4");
    }

    [Fact]
    public void Parse_PortVersion_ProductOnlyWhenVersionEmpty()
    {
        const string xml = @"
<nmaprun>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""192.168.1.1""/>
    <ports>
      <port portid=""443"" protocol=""tcp"">
        <state state=""open""/>
        <service name=""https"" product=""Apache"" version=""""/>
      </port>
    </ports>
  </host>
</nmaprun>";

        var result = NmapXmlParser.Parse(xml, TimeSpan.Zero);

        result.Hosts[0].Ports[0].Version.Should().Be("Apache");
    }

    // ── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_MultipleHosts_AllUpIncluded()
    {
        const string xml = @"
<nmaprun>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""192.168.1.1""/>
  </host>
  <host>
    <status state=""down""/>
    <address addrtype=""ipv4"" addr=""192.168.1.2""/>
  </host>
  <host>
    <status state=""up""/>
    <address addrtype=""ipv4"" addr=""192.168.1.3""/>
  </host>
</nmaprun>";

        var result = NmapXmlParser.Parse(xml, TimeSpan.Zero);

        result.Hosts.Should().HaveCount(2);
        result.Hosts.Select(h => h.IpAddress).Should().BeEquivalentTo(["192.168.1.1", "192.168.1.3"]);
    }

    [Fact]
    public void Parse_NoHostnames_EmptyHostname()
    {
        var result = NmapXmlParser.Parse(MinimalUpHost(), TimeSpan.Zero);

        result.Hosts[0].Hostname.Should().BeEmpty();
    }
}
