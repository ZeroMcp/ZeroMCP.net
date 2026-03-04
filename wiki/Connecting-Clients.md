# Connecting MCP Clients

Point MCP clients at your app's **/mcp** endpoint (or the route you set in **MapZeroMCP**).

---

## Claude Desktop

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
