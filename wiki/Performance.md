# Performance

Baseline performance of ZeroMCP endpoints and how to reproduce numbers.

---

## Running the benchmarks

From the repository root, run the BenchmarkDotNet project in **Release**:

```bash
dotnet run -c Release --project ZeroMCP.Benchmarks/ZeroMCP.Benchmarks.csproj
```

To run only the MCP endpoint benchmarks:

```bash
dotnet run -c Release --project ZeroMCP.Benchmarks/ZeroMCP.Benchmarks.csproj -- --filter "*McpEndpoint*"
```

The benchmarks use **WebApplicationFactory** with the **ZeroMCP.Sample** app (multiple controllers and minimal APIs, ~15 tools). Each benchmark measures the full HTTP round-trip.

---

## What is benchmarked

| Benchmark | Description |
|-----------|-------------|
| **GET /mcp/tools (inspector)** | Time and allocation for the tool inspector JSON response |
| **POST /mcp tools/list** | JSON-RPC `tools/list` (discovery + schema serialization) |
| **POST /mcp tools/call list_orders** | Full dispatch for `list_orders` (no arguments) |
| **POST /mcp tools/call get_order** | Full dispatch for `get_order` with one route argument |

---

## Baseline numbers (reference)

Run the benchmarks on your machine to get local results. Typical orders of magnitude on a development machine (Release, .NET 10, Sample app with ~15 tools):

- **GET /mcp/tools** — Usually in the low milliseconds (single-digit ms mean); allocation depends on tool count and schema size.
- **POST tools/list** — Similar to inspector (same discovery path, different response shape).
- **POST tools/call** — Depends on the action (e.g. in-memory list vs. no-op); often sub-ms to a few ms for simple actions.

Exact numbers depend on hardware, OS, and load. Use the benchmark project to establish baselines after changes (e.g. schema generation, dispatch, or serialization).

---

## Reproducibility

- Use **Release** configuration.
- Close other heavy applications to reduce variance.
- For comparison across commits, run the same filter and note the reported **Mean** and **Error** (and **Allocated** if using MemoryDiagnoser).

---

## See also

- [ZeroMCP.Benchmarks/README.md](../ZeroMCP.Benchmarks/README.md) — How to run and filter benchmarks
- [Configuration](Configuration) — Options that can affect performance (e.g. IncludeInputSchemas, EnableStreamingToolResults)
