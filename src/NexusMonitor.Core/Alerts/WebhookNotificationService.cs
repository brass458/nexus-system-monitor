using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Alerts;

/// <summary>
/// Delivers alert payloads to external services (Discord, Slack, etc.) via HTTP POST.
/// Passive service — no Start/Stop lifecycle. Called by Rx subscribers in App.axaml.cs.
/// </summary>
public sealed class WebhookNotificationService : IDisposable
{
    private readonly AppSettings                          _settings;
    private readonly ILogger<WebhookNotificationService> _logger;
    private readonly QuietHoursService?                  _quietHours;
    private readonly HttpClient                          _http;

    /// <summary>Production constructor.</summary>
    public WebhookNotificationService(AppSettings settings,
        ILogger<WebhookNotificationService> logger,
        QuietHoursService? quietHours = null)
    {
        _settings   = settings;
        _logger     = logger;
        _quietHours = quietHours;
        _http       = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>Test constructor — accepts a custom <see cref="HttpMessageHandler"/> for mocking.</summary>
    public WebhookNotificationService(AppSettings settings,
        ILogger<WebhookNotificationService> logger,
        HttpMessageHandler handler,
        QuietHoursService? quietHours = null)
    {
        _settings   = settings;
        _logger     = logger;
        _quietHours = quietHours;
        _http       = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Sends <paramref name="payload"/> to the configured webhook URL.
    /// No-ops if webhooks are disabled, URL is empty, or Quiet Hours are active.
    /// Swallows all HTTP errors — never throws.
    /// </summary>
    public async Task SendAsync(WebhookPayload payload)
    {
        if (!_settings.WebhookEnabled || string.IsNullOrWhiteSpace(_settings.WebhookUrl))
            return;

        if (_quietHours?.IsActive == true)
            return;

        try
        {
            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.WebhookUrl)
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(_settings.WebhookSecret))
                request.Headers.TryAddWithoutValidation("X-Webhook-Secret", _settings.WebhookSecret);

            var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Webhook POST returned {StatusCode}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook POST failed");
        }
    }

    /// <summary>
    /// Sends a test payload to verify the configured webhook URL is reachable.
    /// Returns <see langword="true"/> on HTTP 2xx, <see langword="false"/> on any failure.
    /// Returns <see langword="false"/> immediately if webhooks are not configured.
    /// </summary>
    public async Task<bool> SendTestAsync()
    {
        if (!_settings.WebhookEnabled || string.IsNullOrWhiteSpace(_settings.WebhookUrl))
            return false;

        try
        {
            var payload = new WebhookPayload(
                "Test notification from Nexus Monitor",
                "info",
                DateTimeOffset.UtcNow.ToString("O"),
                Environment.MachineName,
                null);

            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.WebhookUrl)
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(_settings.WebhookSecret))
                request.Headers.TryAddWithoutValidation("X-Webhook-Secret", _settings.WebhookSecret);

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook test POST failed");
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
