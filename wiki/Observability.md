# Observability

Logging, correlation IDs, metrics, and optional OpenTelemetry integration.

---

## Structured logging

- **MCP request** — Each request is logged with a scope containing **CorrelationId**, **JsonRpcId**, and **Method**. Completion is logged with **Method** and **DurationMs**; warnings for method not found or invalid params; error for unhandled exception.
- **Tool invocation** — Each tool call is logged with **ToolName**, **StatusCode**, **IsError**, **DurationMs**, and **CorrelationId** (Debug on success, Warning on error).

---

## Correlation ID

- **Header** — Name is set by **CorrelationIdHeader** (default `"X-Correlation-ID"`). If the client sends this header, the value is used; otherwise a new GUID is generated.
- **Propagation** — The value is stored in **HttpContext.Items** and echoed in the response (via **OnStarting**). It is also copied to the **synthetic request** used for dispatch (`TraceIdentifier` and `Items`) so your action and link generation see the same ID.
- **Logging** — The correlation ID is included in log scope and in tool invocation logs.
- **Disable** — Set **CorrelationIdHeader** to `null` or empty to disable reading/echoing.

---

## Execution timing

- Request duration is measured around the MCP method switch and logged on completion and on error.
- Per-tool duration is measured around **DispatchAsync** and included in tool logs and metrics.

---

## Metrics sink

Implement **IMcpMetricsSink** and register it after **AddZeroMCP()** to record tool invocations:

```csharp
public interface IMcpMetricsSink
{
    void RecordToolInvocation(string toolName, int statusCode, bool isError, double durationMs, string? correlationId);
}
```

The default registration is a **no-op**. Replace with your own implementation to push to Prometheus, Application Insights, etc.

---

## OpenTelemetry

Set **EnableOpenTelemetryEnrichment = true** to tag the current **Activity** (when present) with:

- `mcp.tool`
- `mcp.status_code`
- `mcp.is_error`
- `mcp.duration_ms`
- `mcp.correlation_id`

Useful when you use OpenTelemetry tracing and want MCP tool calls to appear as spans with these attributes.

---

## See also

- [Configuration](Configuration) — CorrelationIdHeader, EnableOpenTelemetryEnrichment
- [Dispatch and Pipeline](Dispatch-and-Pipeline) — How the synthetic request is built
