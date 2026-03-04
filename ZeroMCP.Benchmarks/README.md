# ZeroMCP.Benchmarks

BenchmarkDotNet-based performance benchmarks for ZeroMCP endpoints using the Sample app.

## Prerequisites

- .NET 10 SDK (or the framework the Sample targets)
- ZeroMCP.Sample and ZeroMCP built

## Run all benchmarks

From the repository root:

```bash
dotnet run -c Release --project ZeroMCP.Benchmarks/ZeroMCP.Benchmarks.csproj
```

Or from this folder:

```bash
dotnet run -c Release
```

## Run a specific benchmark class

```bash
dotnet run -c Release --project ZeroMCP.Benchmarks/ZeroMCP.Benchmarks.csproj -- --filter "*McpEndpointBenchmarks*"
```

## Benchmarks

| Benchmark | Description |
|-----------|-------------|
| **GET /mcp/tools (inspector)** | Full HTTP round-trip for the tool inspector endpoint |
| **POST /mcp tools/list** | JSON-RPC `tools/list` request and response |
| **POST /mcp tools/call list_orders** | JSON-RPC `tools/call` for `list_orders` (no arguments) |
| **POST /mcp tools/call get_order** | JSON-RPC `tools/call` for `get_order` with `id: 1` |

Results include timing (mean, error) and memory allocation. Run in **Release** for meaningful numbers.

## See also

- [wiki/Performance](https://github.com/ZeroMCP/ZeroMCP.net/wiki/Performance) — Baseline numbers and how to reproduce
