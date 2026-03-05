# Connecting MCP Clients

Point MCP clients at your app's **/mcp** endpoint (or the route you set in **MapZeroMCP**). ZeroMCP supports both **HTTP** and **stdio** transports.

---

## Claude Desktop

### Option A: stdio (recommended for local development)

Claude Desktop and Claude Code default to stdio for local MCP servers. Add to **claude_desktop_config.json**:

```json
{
  "mcpServers": {
    "my-api": {
      "command": "dotnet",
      "args": ["run", "--project", "MyApi", "--", "--mcp-stdio"]
    }
  }
}
```

Or with a published binary:

```json
{
  "mcpServers": {
    "my-api": {
      "command": "C:\\MyApi\\MyApi.exe",
      "args": ["--mcp-stdio"]
    }
  }
}
```

In `Program.cs`, add the stdio branch before `app.Run()`:

```csharp
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}
app.Run();
```

### Option B: HTTP

Add to **claude_desktop_config.json**:

```json
{
  "mcpServers": {
    "my-api": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

Use the URL of your running app (e.g. after `dotnet run` or in production your deployed base URL).

---

## Claude.ai (remote MCP)

Use your deployed API's `/mcp` URL. For production, add authentication — ZeroMCP does not add auth to the `/mcp` route; use standard ASP.NET Core middleware or endpoint auth:

```csharp
app.MapZeroMCP().RequireAuthorization("McpPolicy");
```

Configure your MCP client with the same auth (e.g. API key header) if required.

---

## Other MCP clients

Any client that supports the [MCP Streamable HTTP](https://modelcontextprotocol.io/specification/2024-11-05/specification/#streamable-http) transport can target:

- **GET /mcp** — Server info and example payload.
- **POST /mcp** — JSON-RPC 2.0 (`initialize`, `tools/list`, `tools/call`).

Ensure the client sends **Content-Type: application/json** for POST and that auth headers (if you use **ForwardHeaders**) are sent so tool dispatch receives them.

---

## See also

- [Configuration](Configuration) — RoutePrefix, ForwardHeaders
- [Governance and Security](Governance-and-Security) — Securing tools and the endpoint
