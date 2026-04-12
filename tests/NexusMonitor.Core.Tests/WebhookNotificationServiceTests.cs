using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Models;
using Xunit;

namespace NexusMonitor.Core.Tests;

public class WebhookNotificationServiceTests
{
    // ── Fake HTTP handler ──────────────────────────────────────────────────

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests   { get; } = new();
        public List<string>             Bodies     { get; } = new();
        public HttpResponseMessage      Response   { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Read body before the HttpRequestMessage (and its StringContent) gets disposed
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            Bodies.Add(body);
            return Response;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static WebhookNotificationService Create(
        AppSettings settings,
        HttpMessageHandler handler,
        QuietHoursService? quietHours = null) =>
        new(settings, NullLogger<WebhookNotificationService>.Instance, handler, quietHours);

    private static WebhookPayload MakePayload() =>
        new("Test alert", "warning", DateTimeOffset.UtcNow.ToString("O"), "test-host", null);

    private static AppSettings EnabledSettings(string url = "https://example.com/webhook", string secret = "") =>
        new() { WebhookEnabled = true, WebhookUrl = url, WebhookSecret = secret };

    // ── SendAsync tests ────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_PostsJsonToUrl()
    {
        var handler  = new FakeHandler();
        var settings = EnabledSettings("https://hooks.example.com/test");
        using var svc = Create(settings, handler);

        await svc.SendAsync(MakePayload());

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://hooks.example.com/test");

        handler.Bodies.Should().HaveCount(1);
        handler.Bodies[0].Should().Contain("Test alert");
    }

    [Fact]
    public async Task SendAsync_IncludesSecretHeader()
    {
        var handler  = new FakeHandler();
        var settings = EnabledSettings(secret: "my-secret-key");
        using var svc = Create(settings, handler);

        await svc.SendAsync(MakePayload());

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Headers.TryGetValues("X-Webhook-Secret", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("my-secret-key");
    }

    [Fact]
    public async Task SendAsync_DoesNotPost_WhenDisabled()
    {
        var handler  = new FakeHandler();
        var settings = new AppSettings { WebhookEnabled = false, WebhookUrl = "https://example.com/webhook" };
        using var svc = Create(settings, handler);

        await svc.SendAsync(MakePayload());

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_DoesNotPost_WhenUrlEmpty()
    {
        var handler  = new FakeHandler();
        var settings = new AppSettings { WebhookEnabled = true, WebhookUrl = "" };
        using var svc = Create(settings, handler);

        await svc.SendAsync(MakePayload());

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_DoesNotPost_DuringQuietHours()
    {
        var handler  = new FakeHandler();
        var settings = EnabledSettings();

        // Build QuietHoursService that is always active by using a clock within a quiet window
        var qhSettings = new AppSettings
        {
            QuietHoursEnabled = true,
            QuietHoursStart   = "00:00",
            QuietHoursEnd     = "23:59",
        };
        // Clock at 12:00 — well within the 00:00–23:59 window
        using var qh = new QuietHoursService(qhSettings, () => DateTime.Today.AddHours(12));

        using var svc = Create(settings, handler, qh);

        await svc.SendAsync(MakePayload());

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_DoesNotThrow_OnHttpFailure()
    {
        var handler = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.InternalServerError) };
        var settings = EnabledSettings();
        using var svc = Create(settings, handler);

        // Should not throw even on 500
        Func<Task> act = () => svc.SendAsync(MakePayload());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_DoesNotThrow_OnException()
    {
        // Handler that throws to simulate network error
        var handler = new ThrowingHandler();
        var settings = EnabledSettings();
        using var svc = Create(settings, handler);

        Func<Task> act = () => svc.SendAsync(MakePayload());
        await act.Should().NotThrowAsync();
    }

    // ── SendTestAsync tests ────────────────────────────────────────────────

    [Fact]
    public async Task SendTestAsync_ReturnsTrue_On200()
    {
        var handler  = new FakeHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var settings = EnabledSettings();
        using var svc = Create(settings, handler);

        var result = await svc.SendTestAsync();

        result.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendTestAsync_ReturnsFalse_OnFailure()
    {
        var handler = new ThrowingHandler();
        var settings = EnabledSettings();
        using var svc = Create(settings, handler);

        var result = await svc.SendTestAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendTestAsync_ReturnsFalse_WhenNotConfigured()
    {
        var handler  = new FakeHandler();
        var settings = new AppSettings { WebhookEnabled = false, WebhookUrl = "" };
        using var svc = Create(settings, handler);

        var result = await svc.SendTestAsync();

        result.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network failure");
    }
}
