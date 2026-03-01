namespace NexusMonitor.Core.Telemetry;

/// <summary>
/// Pre-built Grafana dashboard JSON that visualises all Nexus Monitor
/// Prometheus metrics across CPU, memory, disk, network, GPU, and alerts.
///
/// Import via:  Grafana → Dashboards → Import → Upload JSON file
///
/// Requires a Prometheus datasource in Grafana pointed at
///   http://localhost:{port}/metrics   (port configured in Settings → Telemetry)
///
/// Compatible with Grafana 10+. Tested with Prometheus 2.x datasource.
/// </summary>
public static class GrafanaDashboard
{
    public static string Json { get; } = """
        {
          "__inputs": [
            {
              "name": "DS_PROMETHEUS",
              "label": "Prometheus",
              "description": "Point this at http://localhost:9182 (or your configured Nexus port)",
              "type": "datasource",
              "pluginId": "prometheus",
              "pluginName": "Prometheus"
            }
          ],
          "__requires": [
            {"type":"grafana",    "id":"grafana",    "name":"Grafana",    "version":"10.0.0"},
            {"type":"datasource", "id":"prometheus", "name":"Prometheus", "version":"1.0.0"},
            {"type":"panel", "id":"timeseries", "name":"Time series", "version":""},
            {"type":"panel", "id":"gauge",      "name":"Gauge",       "version":""},
            {"type":"panel", "id":"bargauge",   "name":"Bar gauge",   "version":""},
            {"type":"panel", "id":"stat",       "name":"Stat",        "version":""}
          ],
          "title": "Nexus Monitor",
          "uid": "nexus-monitor-v1",
          "description": "System metrics from Nexus System Monitor (https://github.com/brass458/nexus-system-monitor). Requires Prometheus datasource pointed at http://localhost:9182/metrics.",
          "schemaVersion": 38,
          "version": 1,
          "tags": ["nexus-monitor"],
          "time": {"from": "now-1h", "to": "now"},
          "refresh": "10s",
          "panels": [

            {"id":1,"type":"row","title":"CPU","collapsed":false,"gridPos":{"h":1,"w":24,"x":0,"y":0},"panels":[]},

            {
              "id": 2, "type": "timeseries", "title": "CPU Total Usage",
              "gridPos": {"h":8,"w":12,"x":0,"y":1},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_cpu_usage_percent", "legendFormat": "CPU %"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":2,"fillOpacity":15,"drawStyle":"line","spanNulls":false},
                  "thresholds": {"mode":"absolute","steps":[
                    {"value":null,"color":"green"},{"value":70,"color":"yellow"},{"value":90,"color":"red"}]}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {
              "id": 3, "type": "timeseries", "title": "CPU Per Core",
              "gridPos": {"h":8,"w":12,"x":12,"y":1},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_cpu_core_usage_percent", "legendFormat": "Core {{core}}"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":1,"fillOpacity":8,"drawStyle":"line"}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {"id":4,"type":"row","title":"Memory","collapsed":false,"gridPos":{"h":1,"w":24,"x":0,"y":9},"panels":[]},

            {
              "id": 5, "type": "gauge", "title": "Memory Used",
              "gridPos": {"h":8,"w":6,"x":0,"y":10},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_memory_used_percent", "legendFormat": "Used %"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "thresholds": {"mode":"absolute","steps":[
                    {"value":null,"color":"green"},{"value":70,"color":"yellow"},{"value":90,"color":"red"}]}
                }
              },
              "options": {
                "reduceOptions": {"calcs":["lastNotNull"],"fields":"","values":false},
                "orientation": "auto", "textMode": "auto", "colorMode": "thresholds", "graphMode": "area"
              }
            },

            {
              "id": 6, "type": "timeseries", "title": "Memory Usage (bytes)",
              "gridPos": {"h":8,"w":18,"x":6,"y":10},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [
                {"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_memory_used_bytes", "legendFormat": "Used"},
                {"refId":"B","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_memory_available_bytes", "legendFormat": "Available"},
                {"refId":"C","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_memory_commit_total_bytes", "legendFormat": "Commit"}
              ],
              "fieldConfig": {
                "defaults": {
                  "unit": "bytes", "min": 0,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":1,"fillOpacity":10,"drawStyle":"line"}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {"id":7,"type":"row","title":"Disk","collapsed":false,"gridPos":{"h":1,"w":24,"x":0,"y":18},"panels":[]},

            {
              "id": 8, "type": "timeseries", "title": "Disk Activity (%)",
              "gridPos": {"h":8,"w":12,"x":0,"y":19},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_disk_active_percent", "legendFormat": "{{disk}}"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":1,"fillOpacity":10}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {
              "id": 9, "type": "timeseries", "title": "Disk I/O Rate",
              "gridPos": {"h":8,"w":12,"x":12,"y":19},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [
                {"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_disk_read_bytes_per_second", "legendFormat": "Read {{disk}}"},
                {"refId":"B","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_disk_write_bytes_per_second", "legendFormat": "Write {{disk}}"}
              ],
              "fieldConfig": {
                "defaults": {
                  "unit": "Bps", "min": 0,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":1,"fillOpacity":10}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {
              "id": 10, "type": "bargauge", "title": "Disk Space Used",
              "gridPos": {"h":6,"w":24,"x":0,"y":27},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_disk_used_percent", "legendFormat": "{{disk}}"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "thresholds": {"mode":"absolute","steps":[
                    {"value":null,"color":"green"},{"value":75,"color":"yellow"},{"value":90,"color":"red"}]},
                  "color": {"mode":"thresholds"}
                }
              },
              "options": {
                "reduceOptions": {"calcs":["lastNotNull"],"fields":"","values":false},
                "orientation": "horizontal", "textMode": "auto", "colorMode": "value",
                "displayMode": "gradient", "minVizWidth": 0, "minVizHeight": 10
              }
            },

            {"id":11,"type":"row","title":"Network","collapsed":false,"gridPos":{"h":1,"w":24,"x":0,"y":33},"panels":[]},

            {
              "id": 12, "type": "timeseries", "title": "Network Throughput",
              "gridPos": {"h":8,"w":24,"x":0,"y":34},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [
                {"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_network_send_bytes_per_second", "legendFormat": "↑ Send {{adapter}}"},
                {"refId":"B","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_network_recv_bytes_per_second", "legendFormat": "↓ Recv {{adapter}}"}
              ],
              "fieldConfig": {
                "defaults": {
                  "unit": "Bps", "min": 0,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":1,"fillOpacity":10}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {"id":13,"type":"row","title":"GPU","collapsed":false,"gridPos":{"h":1,"w":24,"x":0,"y":42},"panels":[]},

            {
              "id": 14, "type": "timeseries", "title": "GPU Usage (%)",
              "gridPos": {"h":8,"w":12,"x":0,"y":43},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_gpu_usage_percent", "legendFormat": "{{gpu}}"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "percent", "min": 0, "max": 100,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":2,"fillOpacity":15}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {
              "id": 15, "type": "timeseries", "title": "GPU Memory",
              "gridPos": {"h":8,"w":12,"x":12,"y":43},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [
                {"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_gpu_memory_used_bytes", "legendFormat": "Used {{gpu}}"},
                {"refId":"B","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                  "expr": "nexus_gpu_memory_total_bytes", "legendFormat": "Total {{gpu}}"}
              ],
              "fieldConfig": {
                "defaults": {
                  "unit": "bytes", "min": 0,
                  "color": {"mode":"palette-classic"},
                  "custom": {"lineWidth":1,"fillOpacity":10}
                }
              },
              "options": {
                "legend": {"displayMode":"list","placement":"bottom","showLegend":true},
                "tooltip": {"mode":"multi","sort":"none"}
              }
            },

            {"id":16,"type":"row","title":"System & Alerts","collapsed":false,"gridPos":{"h":1,"w":24,"x":0,"y":51},"panels":[]},

            {
              "id": 17, "type": "stat", "title": "Alert Events (total)",
              "gridPos": {"h":4,"w":6,"x":0,"y":52},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_alert_events_total", "legendFormat": "Alerts"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "short",
                  "color": {"mode":"thresholds"},
                  "thresholds": {"mode":"absolute","steps":[
                    {"value":null,"color":"blue"},{"value":1,"color":"orange"},{"value":10,"color":"red"}]}
                }
              },
              "options": {
                "reduceOptions": {"calcs":["lastNotNull"],"fields":"","values":false},
                "orientation": "auto", "textMode": "auto", "colorMode": "background", "graphMode": "none"
              }
            },

            {
              "id": 18, "type": "stat", "title": "Running Processes",
              "gridPos": {"h":4,"w":6,"x":6,"y":52},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_cpu_process_count", "legendFormat": "Processes"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "short",
                  "color": {"mode":"thresholds"},
                  "thresholds": {"mode":"absolute","steps":[{"value":null,"color":"blue"}]}
                }
              },
              "options": {
                "reduceOptions": {"calcs":["lastNotNull"],"fields":"","values":false},
                "orientation": "auto", "textMode": "auto", "colorMode": "background", "graphMode": "none"
              }
            },

            {
              "id": 19, "type": "stat", "title": "System Threads",
              "gridPos": {"h":4,"w":6,"x":12,"y":52},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_cpu_thread_count", "legendFormat": "Threads"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "short",
                  "color": {"mode":"thresholds"},
                  "thresholds": {"mode":"absolute","steps":[{"value":null,"color":"blue"}]}
                }
              },
              "options": {
                "reduceOptions": {"calcs":["lastNotNull"],"fields":"","values":false},
                "orientation": "auto", "textMode": "auto", "colorMode": "background", "graphMode": "none"
              }
            },

            {
              "id": 20, "type": "stat", "title": "CPU Temperature",
              "gridPos": {"h":4,"w":6,"x":18,"y":52},
              "datasource": {"type":"prometheus","uid":"${DS_PROMETHEUS}"},
              "targets": [{"refId":"A","datasource":{"type":"prometheus","uid":"${DS_PROMETHEUS}"},
                "expr": "nexus_cpu_temperature_celsius", "legendFormat": "Temp"}],
              "fieldConfig": {
                "defaults": {
                  "unit": "celsius",
                  "color": {"mode":"thresholds"},
                  "thresholds": {"mode":"absolute","steps":[
                    {"value":null,"color":"green"},{"value":75,"color":"yellow"},{"value":90,"color":"red"}]}
                }
              },
              "options": {
                "reduceOptions": {"calcs":["lastNotNull"],"fields":"","values":false},
                "orientation": "auto", "textMode": "auto", "colorMode": "background", "graphMode": "area"
              }
            }

          ]
        }
        """;
}
