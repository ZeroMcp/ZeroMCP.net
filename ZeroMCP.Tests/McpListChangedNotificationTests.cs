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
using ZeroMCP.Discovery;
using ZeroMCP.Notifications;
using ZeroMCP.Options;

namespace ZeroMCP.Tests;

// ---------------------------------------------------------------------------
// Factory with EnableListChangedNotifications = true
// ---------------------------------------------------------------------------
public sealed class ListChangedEnabledFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<ZeroMCPOptions>(opts =>
            {
                opts.EnableListChangedNotifications = true;
            });
        });
    }
}

// ---------------------------------------------------------------------------
// Factory with EnableListChangedNotifications = false (explicit override)
// ---------------------------------------------------------------------------
public sealed class ListChangedDisabledFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<ZeroMCPOptions>(opts =>
            {
                opts.EnableListChangedNotifications = false;
            });
        });
    }
}

// ===========================================================================
// listChanged capability, notification service, and cache invalidation tests
//
// NOTE: TestServer does not support reading streaming response body data while
// the handler is still executing (it buffers the body until HandleAsync returns).
// Therefore, end-to-end SSE delivery tests that read "data:" lines from the
// response stream are not feasible here. Instead, we verify:
//   1. The notification service correctly broadcasts to registered channels
//   2. The SSE handler registers a session with the notification service
//      (proven via the X-ZeroMCP-Notifications response header)
//   3. The capabilities advertise listChanged: true when enabled
//   4. Discovery caches can be invalidated and rebuilt
// Real SSE delivery should be validated in a live Kestrel test or manually.
// ===========================================================================

public sealed class McpListChangedNotificationTests
    : IClassFixture<ListChangedEnabledFactory>
{
    private readonly ListChangedEnabledFactory _factory;
    private readonly HttpClient _client;

    public McpListChangedNotificationTests(ListChangedEnabledFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // -----------------------------------------------------------------------
    // Capabilities: listChanged = true when enabled
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Initialize_WhenListChangedEnabled_ToolsListChangedIsTrue()
    {
        var response = await PostMcpAsync(new
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

        var capabilities = response["result"]!.AsObject()["capabilities"]!.AsObject();
        var tools = capabilities["tools"]!.AsObject();
        tools["listChanged"]!.GetValue<bool>().Should().BeTrue(
            "tools capability must advertise listChanged: true when EnableListChangedNotifications is on");
    }

    [Fact]
    public async Task Initialize_WhenListChangedEnabled_ResourcesListChangedIsTrue()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.ProtocolVersion,
                clientInfo = new { name = "test", version = "1.0" }
            }
        });

        var capabilities = response["result"]!.AsObject()["capabilities"]!.AsObject();
        var resources = capabilities["resources"]!.AsObject();
        resources["listChanged"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_WhenListChangedEnabled_PromptsListChangedIsTrue()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.ProtocolVersion,
                clientInfo = new { name = "test", version = "1.0" }
            }
        });

        var capabilities = response["result"]!.AsObject()["capabilities"]!.AsObject();
        var prompts = capabilities["prompts"]!.AsObject();
        prompts["listChanged"]!.GetValue<bool>().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Capabilities: listChanged = false when disabled (default)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Initialize_WhenListChangedDisabled_ListChangedIsFalse()
    {
        using var defaultFactory = new ListChangedDisabledFactory();
        using var client = defaultFactory.CreateClient();

        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 4,
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

        var capabilities = response["result"]!.AsObject()["capabilities"]!.AsObject();
        var tools = capabilities["tools"]!.AsObject();
        tools["listChanged"]!.GetValue<bool>().Should().BeFalse(
            "tools listChanged must be false when EnableListChangedNotifications is off (default)");
    }

    // -----------------------------------------------------------------------
    // SSE handler: session registration (verified via response header)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SseHandler_RegistersSessionWithNotificationService()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        // The handler sets this diagnostic header with the session ID when
        // McpNotificationService is present and the session is registered.
        response.Headers.Contains("X-ZeroMCP-Notifications").Should().BeTrue(
            "SSE handler should include the X-ZeroMCP-Notifications header");

        var headerValue = response.Headers.GetValues("X-ZeroMCP-Notifications").First();
        headerValue.Should().StartWith("enabled;session=",
            "SSE handler should register a session with the notification service");
    }

    [Fact]
    public async Task SseHandler_WhenNotificationsDisabled_HeaderShowsDisabled()
    {
        using var defaultFactory = new ListChangedDisabledFactory();
        using var client = defaultFactory.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var headerValue = response.Headers.Contains("X-ZeroMCP-Notifications")
            ? response.Headers.GetValues("X-ZeroMCP-Notifications").First()
            : "MISSING";
        headerValue.Should().Be("disabled",
            "SSE handler should show 'disabled' when notifications are off");
    }

    // -----------------------------------------------------------------------
    // McpNotificationService: direct channel broadcast
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationService_ToolsListChanged_BroadcastsToAllSessions()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var channel = Channel.CreateUnbounded<string>();
        var sessionId = svc.RegisterSession(channel.Writer);
        try
        {
            await svc.NotifyToolsListChangedAsync();

            channel.Reader.TryRead(out var msg).Should().BeTrue(
                "notification should be delivered to the registered channel");
            var parsed = JsonNode.Parse(msg!)!.AsObject();
            parsed["jsonrpc"]!.GetValue<string>().Should().Be("2.0");
            parsed["method"]!.GetValue<string>().Should().Be("notifications/tools/list_changed");
        }
        finally
        {
            svc.UnregisterSession(sessionId);
        }
    }

    [Fact]
    public async Task NotificationService_ResourcesListChanged_BroadcastsToAllSessions()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var channel = Channel.CreateUnbounded<string>();
        var sessionId = svc.RegisterSession(channel.Writer);
        try
        {
            await svc.NotifyResourcesListChangedAsync();

            channel.Reader.TryRead(out var msg).Should().BeTrue();
            var parsed = JsonNode.Parse(msg!)!.AsObject();
            parsed["method"]!.GetValue<string>().Should().Be("notifications/resources/list_changed");
        }
        finally
        {
            svc.UnregisterSession(sessionId);
        }
    }

    [Fact]
    public async Task NotificationService_PromptsListChanged_BroadcastsToAllSessions()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var channel = Channel.CreateUnbounded<string>();
        var sessionId = svc.RegisterSession(channel.Writer);
        try
        {
            await svc.NotifyPromptsListChangedAsync();

            channel.Reader.TryRead(out var msg).Should().BeTrue();
            var parsed = JsonNode.Parse(msg!)!.AsObject();
            parsed["method"]!.GetValue<string>().Should().Be("notifications/prompts/list_changed");
        }
        finally
        {
            svc.UnregisterSession(sessionId);
        }
    }

    [Fact]
    public async Task NotificationService_BroadcastsToMultipleSessions()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var ch1 = Channel.CreateUnbounded<string>();
        var ch2 = Channel.CreateUnbounded<string>();
        var id1 = svc.RegisterSession(ch1.Writer);
        var id2 = svc.RegisterSession(ch2.Writer);
        try
        {
            await svc.NotifyToolsListChangedAsync();

            ch1.Reader.TryRead(out var msg1).Should().BeTrue("session 1 should receive the broadcast");
            ch2.Reader.TryRead(out var msg2).Should().BeTrue("session 2 should receive the broadcast");
            msg1.Should().Contain("tools/list_changed");
            msg2.Should().Contain("tools/list_changed");
        }
        finally
        {
            svc.UnregisterSession(id1);
            svc.UnregisterSession(id2);
        }
    }

    [Fact]
    public void NotificationService_UnregisterSession_RemovesCleanly()
    {
        var svc = _factory.Services.GetRequiredService<McpNotificationService>();

        var ch = Channel.CreateUnbounded<string>();
        var id = svc.RegisterSession(ch.Writer);

        svc.ActiveSessionCount.Should().BeGreaterThan(0);

        svc.UnregisterSession(id);

        // Double-unregister should be safe
        svc.UnregisterSession(id);
    }

    [Fact]
    public void NotificationService_OptionIsEnabled()
    {
        var opts = _factory.Services.GetRequiredService<IOptions<ZeroMCPOptions>>().Value;
        opts.EnableListChangedNotifications.Should().BeTrue(
            "ListChangedEnabledFactory sets EnableListChangedNotifications = true");
    }

    // -----------------------------------------------------------------------
    // Discovery: InvalidateCache causes re-discovery on next access
    // -----------------------------------------------------------------------

    [Fact]
    public void ToolDiscovery_InvalidateCache_AllowsRediscovery()
    {
        var discovery = _factory.Services.GetRequiredService<McpToolDiscoveryService>();

        var beforeCount = discovery.GetRegistry().Count;
        beforeCount.Should().BeGreaterThan(0, "the sample app has registered tools");

        discovery.InvalidateCache();

        var afterCount = discovery.GetRegistry().Count;
        afterCount.Should().BeGreaterThanOrEqualTo(beforeCount,
            "after InvalidateCache + re-access, at least the same tools should be rediscovered");
    }

    [Fact]
    public void ResourceDiscovery_InvalidateCache_AllowsRediscovery()
    {
        var discovery = _factory.Services.GetRequiredService<McpResourceDiscoveryService>();

        var beforeCount = discovery.GetStaticResources().Count;
        discovery.InvalidateCache();
        var afterCount = discovery.GetStaticResources().Count;
        afterCount.Should().Be(beforeCount);
    }

    [Fact]
    public void PromptDiscovery_InvalidateCache_AllowsRediscovery()
    {
        var discovery = _factory.Services.GetRequiredService<McpPromptDiscoveryService>();

        var beforeCount = discovery.GetPrompts().Count;
        discovery.InvalidateCache();
        var afterCount = discovery.GetPrompts().Count;
        afterCount.Should().Be(beforeCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<JsonObject> PostMcpAsync(object body)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }
}
