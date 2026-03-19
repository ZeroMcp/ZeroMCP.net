using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZeroMCP.Notifications;
using ZeroMCP.Options;

namespace ZeroMCP.Tests;

// ---------------------------------------------------------------------------
// Factory: subscriptions enabled (requires notifications + subscriptions)
// ---------------------------------------------------------------------------
public sealed class SubscriptionEnabledFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<ZeroMCPOptions>(opts =>
            {
                opts.EnableListChangedNotifications = true;
                opts.EnableResourceSubscriptions = true;
            });
        });
    }
}

// ---------------------------------------------------------------------------
// Factory: subscriptions disabled (notifications on, subscriptions off)
// ---------------------------------------------------------------------------
public sealed class SubscriptionDisabledFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<ZeroMCPOptions>(opts =>
            {
                opts.EnableListChangedNotifications = true;
                opts.EnableResourceSubscriptions = false;
            });
        });
    }
}

// ===========================================================================
// Resource subscription tests
// ===========================================================================
public sealed class McpResourceSubscriptionTests
    : IClassFixture<SubscriptionEnabledFactory>
{
    private readonly SubscriptionEnabledFactory _factory;
    private readonly HttpClient _client;

    public McpResourceSubscriptionTests(SubscriptionEnabledFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // -----------------------------------------------------------------------
    // 1. Capabilities: subscribe = true when enabled
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Initialize_WhenSubscribeEnabled_AdvertisesSubscribeTrue()
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.ProtocolVersion,
                clientInfo = new { name = "test", version = "1.0" }
            }
        });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonNode.Parse(responseJson)!.AsObject();

        var resources = response["result"]!.AsObject()["capabilities"]!.AsObject()["resources"]!.AsObject();
        resources["subscribe"]!.GetValue<bool>().Should().BeTrue(
            "resources.subscribe must be true when EnableResourceSubscriptions is on");
    }

    // -----------------------------------------------------------------------
    // 2. Capabilities: subscribe = false when disabled
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Initialize_WhenSubscribeDisabled_AdvertisesSubscribeFalse()
    {
        using var disabledFactory = new SubscriptionDisabledFactory();
        using var client = disabledFactory.CreateClient();

        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.ProtocolVersion,
                clientInfo = new { name = "test", version = "1.0" }
            }
        });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonNode.Parse(responseJson)!.AsObject();

        var resources = response["result"]!.AsObject()["capabilities"]!.AsObject()["resources"]!.AsObject();
        resources["subscribe"]!.GetValue<bool>().Should().BeFalse(
            "resources.subscribe must be false when EnableResourceSubscriptions is off");
    }

    // -----------------------------------------------------------------------
    // 3. Subscribe with valid URI returns empty result
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Subscribe_ValidUri_ReturnsEmptyResult()
    {
        var sessionId = await OpenSseAndGetSessionIdAsync();

        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "resources/subscribe",
            @params = new { uri = "catalog://products/1" }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("Mcp-Session-Id", sessionId);

        var httpResponse = await _client.SendAsync(request);
        httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonNode.Parse(responseJson)!.AsObject();
        response["result"].Should().NotBeNull();
        response["id"]!.GetValue<int>().Should().Be(10);
    }

    // -----------------------------------------------------------------------
    // 4. Unsubscribe with valid URI returns empty result
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Unsubscribe_ValidUri_ReturnsEmptyResult()
    {
        var sessionId = await OpenSseAndGetSessionIdAsync();

        var svc = _factory.Services.GetRequiredService<McpNotificationService>();
        svc.SubscribeSession(sessionId, "catalog://products/1");

        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 11,
            method = "resources/unsubscribe",
            @params = new { uri = "catalog://products/1" }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("Mcp-Session-Id", sessionId);

        var httpResponse = await _client.SendAsync(request);
        httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonNode.Parse(responseJson)!.AsObject();
        response["result"].Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // 5. Subscribe without Mcp-Session-Id returns error
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Subscribe_WithoutSessionId_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 12,
            method = "resources/subscribe",
            @params = new { uri = "catalog://products/1" }
        });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonNode.Parse(responseJson)!.AsObject();

        response["error"].Should().NotBeNull("should return error when no Mcp-Session-Id header");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32602);
    }

    // -----------------------------------------------------------------------
    // 6. Subscribe when feature disabled returns Method Not Found
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Subscribe_WhenDisabled_ReturnsMethodNotFound()
    {
        using var disabledFactory = new SubscriptionDisabledFactory();
        using var client = disabledFactory.CreateClient();

        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 13,
            method = "resources/subscribe",
            @params = new { uri = "catalog://products/1" }
        });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        var response = JsonNode.Parse(responseJson)!.AsObject();

        response["error"].Should().NotBeNull();
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32601,
            "resources/subscribe should return -32601 when feature is disabled");
    }

    // -----------------------------------------------------------------------
    // 7. NotifyResourceUpdatedAsync broadcasts to subscribed sessions only
    // -----------------------------------------------------------------------
    [Fact]
    public async Task NotifyResourceUpdated_BroadcastsToSubscribedSessionOnly()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var ch1 = Channel.CreateUnbounded<string>();
        var ch2 = Channel.CreateUnbounded<string>();
        var s1 = svc.RegisterSession(ch1.Writer);
        var s2 = svc.RegisterSession(ch2.Writer);

        svc.SubscribeSession(s1, "test://resource/a");

        await svc.NotifyResourceUpdatedAsync("test://resource/a");

        ch1.Reader.TryRead(out var msg1).Should().BeTrue("subscribed session should receive notification");
        msg1.Should().Contain("notifications/resources/updated");
        msg1.Should().Contain("test://resource/a");

        ch2.Reader.TryRead(out _).Should().BeFalse("non-subscribed session should not receive notification");

        svc.UnregisterSession(s1);
        svc.UnregisterSession(s2);
    }

    // -----------------------------------------------------------------------
    // 8. NotifyResourceUpdated does not broadcast after unsubscribe
    // -----------------------------------------------------------------------
    [Fact]
    public async Task NotifyResourceUpdated_DoesNotBroadcastAfterUnsubscribe()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var ch = Channel.CreateUnbounded<string>();
        var sid = svc.RegisterSession(ch.Writer);

        svc.SubscribeSession(sid, "test://resource/b");
        svc.UnsubscribeSession(sid, "test://resource/b");

        await svc.NotifyResourceUpdatedAsync("test://resource/b");

        ch.Reader.TryRead(out _).Should().BeFalse("unsubscribed session should not receive notification");

        svc.UnregisterSession(sid);
    }

    // -----------------------------------------------------------------------
    // 9. Multiple URIs: isolated delivery
    // -----------------------------------------------------------------------
    [Fact]
    public async Task NotifyResourceUpdated_MultipleUris_IsolatedDelivery()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var chA = Channel.CreateUnbounded<string>();
        var chB = Channel.CreateUnbounded<string>();
        var sA = svc.RegisterSession(chA.Writer);
        var sB = svc.RegisterSession(chB.Writer);

        svc.SubscribeSession(sA, "test://alpha");
        svc.SubscribeSession(sB, "test://beta");

        await svc.NotifyResourceUpdatedAsync("test://alpha");

        chA.Reader.TryRead(out var msgA).Should().BeTrue();
        msgA.Should().Contain("test://alpha");

        chB.Reader.TryRead(out _).Should().BeFalse("session subscribed to beta should not get alpha notification");

        await svc.NotifyResourceUpdatedAsync("test://beta");

        chB.Reader.TryRead(out var msgB).Should().BeTrue();
        msgB.Should().Contain("test://beta");

        chA.Reader.TryRead(out _).Should().BeFalse("session subscribed to alpha should not get beta notification");

        svc.UnregisterSession(sA);
        svc.UnregisterSession(sB);
    }

    // -----------------------------------------------------------------------
    // 10. UnregisterSession cleans up subscriptions
    // -----------------------------------------------------------------------
    [Fact]
    public async Task UnregisterSession_CleansUpSubscriptions()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var ch = Channel.CreateUnbounded<string>();
        var sid = svc.RegisterSession(ch.Writer);

        svc.SubscribeSession(sid, "test://cleanup/1");
        svc.SubscribeSession(sid, "test://cleanup/2");
        svc.GetSubscriberCount("test://cleanup/1").Should().BeGreaterThanOrEqualTo(1);

        svc.UnregisterSession(sid);

        svc.GetSubscriberCount("test://cleanup/1").Should().Be(0,
            "subscriptions should be cleaned up when session is unregistered");
        svc.GetSubscriberCount("test://cleanup/2").Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // 11. SSE response includes Mcp-Session-Id header
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SseHandler_ReturnsMcpSessionIdHeader()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Mcp-Session-Id").Should().BeTrue(
            "SSE response should include Mcp-Session-Id header");

        var sessionId = response.Headers.GetValues("Mcp-Session-Id").First();
        sessionId.Should().NotBeNullOrWhiteSpace();
        sessionId.Should().HaveLength(32, "session ID should be a GUID without dashes");
    }

    // -----------------------------------------------------------------------
    // Helper: opens an SSE connection, reads the Mcp-Session-Id from headers
    // -----------------------------------------------------------------------
    private async Task<string> OpenSseAndGetSessionIdAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var sseClient = _factory.CreateClient();
        using var response = await sseClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.Headers.Contains("Mcp-Session-Id").Should().BeTrue();
        return response.Headers.GetValues("Mcp-Session-Id").First();
    }
}
