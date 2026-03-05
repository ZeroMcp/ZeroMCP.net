# WithStdio Example

ZeroMCP setup for **stdio transport** — JSON-RPC over stdin/stdout (Claude Desktop, Claude Code, VS Code extensions, etc.).

## What this demonstrates

- **`--mcp-stdio` CLI flag** — When present, the app runs `RunMcpStdioAsync()` instead of starting an HTTP server
- **Same tools as HTTP** — `health_check` (minimal API) and `echo` (controller) are available over stdio or HTTP
- **Claude Desktop** — Add this project to your `claude_desktop_config.json` to use it as a local MCP server

## Run

### stdio mode (default — for Claude Desktop)

The default launch profile uses `--mcp-stdio` and does not open a browser:

```bash
dotnet run
```

Or from the project directory:

```bash
dotnet run --project examples/WithStdio/WithStdioExample.csproj -- --mcp-stdio
```

### HTTP mode

Use the **"WithStdioExample (HTTP)"** launch profile, or run without the flag:

```bash
dotnet run --launch-profile "WithStdioExample (HTTP)"
```

- **GET /mcp** — MCP endpoint info
- **POST /mcp** — JSON-RPC (e.g. `initialize`, `tools/list`, `tools/call`)

## Claude Desktop configuration

Add to **claude_desktop_config.json**:

```json
{
  "mcpServers": {
    "stdio-example": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/mcpAPI/examples/WithStdio/WithStdioExample.csproj", "--", "--mcp-stdio"]
    }
  }
}
```

Or with a published binary:

```bash
dotnet publish examples/WithStdio -c Release -o ./publish/stdio-example
```

```json
{
  "mcpServers": {
    "stdio-example": {
      "command": "path/to/publish/stdio-example/WithStdioExample.exe",
      "args": ["--mcp-stdio"]
    }
  }
}
```

## Manual testing (stdio)

Pipe JSON-RPC messages (newline-delimited) to stdin:

```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}' | dotnet run --project examples/WithStdio/WithStdioExample.csproj --framework net10.0 -- --mcp-stdio
```

## Next steps

- See [wiki/Connecting-Clients](wiki/Connecting-Clients.md) for full Claude Desktop and HTTP setup
- See **Minimal** for a bare HTTP-only example
