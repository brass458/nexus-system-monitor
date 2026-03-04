using System.Xml.Linq;

namespace NexusMonitor.Core.Network;

/// <summary>Parses nmap XML output (-oX) into <see cref="NmapScanResult"/>.</summary>
public static class NmapXmlParser
{
    public static NmapScanResult Parse(string xml, TimeSpan elapsed)
    {
        var hosts = new List<NmapHost>();
        try
        {
            var doc = XDocument.Parse(xml);

            foreach (var hostEl in doc.Descendants("host"))
            {
                // State — only include "up" hosts
                var state = hostEl.Element("status")?.Attribute("state")?.Value ?? "unknown";
                if (!state.Equals("up", StringComparison.OrdinalIgnoreCase)) continue;

                // IP address
                var ipEl = hostEl.Elements("address")
                    .FirstOrDefault(a => a.Attribute("addrtype")?.Value == "ipv4");
                var ip = ipEl?.Attribute("addr")?.Value ?? string.Empty;

                // MAC address
                var macEl = hostEl.Elements("address")
                    .FirstOrDefault(a => a.Attribute("addrtype")?.Value == "mac");
                var mac = macEl?.Attribute("addr")?.Value ?? string.Empty;

                // Hostname
                var hostname = hostEl.Element("hostnames")?
                    .Elements("hostname")
                    .FirstOrDefault(h => h.Attribute("type")?.Value == "PTR")?
                    .Attribute("name")?.Value ?? string.Empty;

                // Latency (RTT in ms)
                double latency = 0;
                var rttEl = hostEl.Element("times");
                if (rttEl?.Attribute("srtt")?.Value is { } rttStr &&
                    double.TryParse(rttStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var rtt))
                    latency = rtt / 1000.0;

                // OS guess
                var osGuess = hostEl.Element("os")?
                    .Elements("osmatch")
                    .OrderByDescending(m => int.TryParse(m.Attribute("accuracy")?.Value, out var acc) ? acc : 0)
                    .FirstOrDefault()?
                    .Attribute("name")?.Value ?? string.Empty;

                // Ports
                var ports = new List<NmapPort>();
                foreach (var portEl in hostEl.Descendants("port"))
                {
                    var portNum   = int.TryParse(portEl.Attribute("portid")?.Value, out var pn) ? pn : 0;
                    var proto     = portEl.Attribute("protocol")?.Value ?? "tcp";
                    var portState = portEl.Element("state")?.Attribute("state")?.Value ?? "unknown";
                    var service   = portEl.Element("service")?.Attribute("name")?.Value ?? string.Empty;
                    var version   = portEl.Element("service")?.Attribute("product")?.Value ?? string.Empty;
                    var ver2      = portEl.Element("service")?.Attribute("version")?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(ver2)) version = $"{version} {ver2}".Trim();

                    ports.Add(new NmapPort(portNum, proto, portState, service, version));
                }

                hosts.Add(new NmapHost(ip, hostname, mac, osGuess ?? string.Empty, latency, ports));
            }
        }
        catch { /* malformed XML — return empty */ }

        return new NmapScanResult(hosts, elapsed, xml);
    }
}
