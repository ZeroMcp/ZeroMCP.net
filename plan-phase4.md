# Phase 4 Implementation Plan

**Timeline:** Month 6  
**Focus:** Enterprise Features, Tool Versioning, Security Hardening  
**Outcome:** Safe at scale, long-lived agent stability, enterprise-grade security

This document breaks Phase 4 from [plan.md](plan.md) into concrete tasks, options, and implementation notes.

---

## 1. Enterprise Features: Rate Limiting & Quotas

### Goals

- **Rate limiting** — Limit request rate per key (per-user, per-tool, per-agent) to protect the MCP endpoint and individual tools.
- **Quota enforcement** — Optional daily/hourly caps per key (e.g. per API key or per user).

### 1.1 Rate limiting (Option A implemented)

| Task | Description | Notes |
|------|--------------|--------|
| **Options** | Add `ZeroMCPOptions` for rate limiting: enable flag, policy name (or inline policy), partition key strategy. | Integrate with ASP.NET Core `Microsoft.AspNetCore.RateLimiting` (RateLimiterOptions) so apps can use the same policies elsewhere. |
| **Partition key** | Support partition by: **User** (ClaimsPrincipal), **Tool** (for tools/call), **Agent** (e.g. header `X-Agent-Id` or `X-Client-Id`), **Endpoint** (MCP vs inspector). | Key resolver: `Func<HttpContext, ValueTask<string?>>` or enum + header name. Default: per-user (if authenticated) else per-IP or per-request (degraded). |
| **Hook point** | Apply rate limit **before** JSON-RPC dispatch (in `McpHttpEndpointHandler` or middleware). For per-tool limits, apply after method is known (tools/call) and tool name is parsed. | Per-tool requires reading the body to get `method` and `params.name`; consider middleware that only rate-limits by endpoint + user first, then optional second check inside handler for tools/call by (user, tool). |
| **Response** | On rate limit exceeded: return JSON-RPC error (e.g. `-32029` or application-defined code) with message "Rate limit exceeded" and optional `Retry-After`. HTTP 429. | Align with existing error shape (McpInvalidParamsException, etc.). |
| **Docs** | Document in [Configuration](wiki/Configuration.md), [Enterprise Usage](wiki/Enterprise-Usage.md). Update [Security Model](wiki/Security-Model.md) attack surfaces. | Remove or update "Phase 4 may add built-in options" in Enterprise-Usage. |

**Design choices**

- **Option A — Use ASP.NET Core rate limiting only:** Document that apps should add `UseRateLimiter()` and a policy that applies to the MCP route; ZeroMCP does not add its own limiter. **Pros:** No new dependencies, flexible. **Cons:** No per-tool built-in story; app must partition by path/query.
- **Option B — ZeroMCP-specific options that register a policy:** e.g. `EnableRateLimiting`, `RateLimitPolicyName`, `RateLimitPartitionKey` (User | Tool | Agent | Custom). ZeroMCP registers a policy and applies it to the mapped MCP endpoint. **Pros:** One place to configure MCP rate limits, per-tool possible. **Cons:** More code, must integrate with `IEndpointRateLimiterPolicy` or similar.
- **Recommendation:** Start with **Option A** (docs + example in examples/WithRateLimiting) and a single **Option B**-style option: `RateLimitPolicyName` (string?). When set, ZeroMCP applies the named policy to the MCP endpoint (so app defines the policy and partition). No per-tool in v1 unless we add a second policy for tools/call.

**✅ Part 1 Option A completed:** Documentation and example added. See [progress.md](progress.md).

### 1.2 Quota enforcement

| Task | Description | Notes |
|------|--------------|--------|
| **Scope** | Optional quota (e.g. 10,000 tool calls per day per user). | Can be implemented as a rate limit policy (e.g. sliding window with large window and limit) or a separate quota store. |
| **Defer or minimal** | Phase 4 can document "use rate limiting with a daily window" or add a simple `QuotaEnforcement` delegate that returns (allowed, remaining, resetAt). Default: no quota. | Full quota with persistence (e.g. Redis) is likely app-specific; provide interface only or skip. |

**Recommendation:** Document quota as "use rate limiting with a long window" or "implement custom middleware with your store." Add optional `IMcpQuotaStore` in a later patch if needed.

---

## 2. Tool Versioning

### Goals

- **Versioned tool names** — Support naming that includes version (e.g. `get_order_v2`) or version in metadata.
- **Deprecation** — Mark tools as deprecated so clients and UIs can warn; optionally hide from default tools/list.
- **Backwards compatibility** — Clear strategy for evolving tools without breaking existing agents.

### 2.1 Version in metadata

| Task | Description | Notes |
|------|--------------|--------|
| **Attribute** | Add `[Mcp(..., Version = "2.0", Deprecated = true)]` (or `ToolVersion` and `IsDeprecated`). Same on `.AsMcp(..., version: "2.0", deprecated: true)`. | Store in `McpToolDescriptor` / metadata; expose in tools/list and GET /mcp/tools. |
| **tools/list** | Include optional `version` and `deprecated` in each tool in the MCP response. Schema: `version?: string`, `deprecated?: boolean`. | LLMs and clients can avoid deprecated tools or prefer newer versions. |
| **Inspector** | GET /mcp/tools and UI show version and deprecated badge. | Small UI change in McpInspectorUiHtml and payload in GetInspectorPayload. |
| **Docs** | [The [Mcp] Attribute](wiki/The-Mcp-Attribute), [Versioning](wiki/Versioning.md), new subsection in [Governance](wiki/Governance-and-Security.md) or [Limitations](wiki/Limitations.md) for "evolving tools over time." | |

### 2.2 Versioned tool names and aliases

| Task | Description | Notes |
|------|--------------|--------|
| **Naming convention** | Document that tool names can include version suffix (e.g. `get_order_v2`). No code change required; just convention. | |
| **Aliases (optional)** | Allow registering an alias: e.g. `get_order` → invokes same handler as `get_order_v2` for backwards compatibility. Option: `[Mcp("get_order_v2", AliasFor = "get_order")]` or a separate API to register aliases. | Low priority; can be Phase 4.1. |
| **Deprecation behavior** | When `Deprecated = true`: include in tools/list by default with `deprecated: true`. Option to hide deprecated tools from default list (e.g. `IncludeDeprecatedTools` default true). | |

### 2.3 Implementation order

1. Add `Version` and `Deprecated` to attribute and descriptor; expose in tools/list and inspector.
2. Update UI and docs.
3. Add optional alias/redirect if time permits.

### 2.4 Tool versioning via versioned MCP endpoints (completed)

Tool versioning was implemented via **versioned MCP endpoints** (see separate plan: versioned `/mcp/v1`, `/mcp/v2`, etc.). Summary:

- **Attribute:** `[Mcp(..., Version = 1)]` (integer; 0 or omitted = unversioned). Same on `.AsMcp(..., version: 1)`.
- **Routing:** When any versioned tools exist, routes `/mcp/v{n}`, `/mcp/v{n}/tools`, `/mcp/v{n}/ui` are registered per version; unversioned `/mcp` resolves to highest (or **DefaultVersion** in options).
- **Discovery:** Tools grouped into version buckets; unversioned tools appear on all version endpoints; duplicate names allowed across versions, not within a version.
- **Inspector:** JSON includes `version` and `availableVersions`; UI has version selector and version badges. Docs: [wiki/Tool-Versioning](wiki/Tool-Versioning.md).

Deprecation and string versions remain as future work.

---

## 3. Security Hardening

### Goals

- **Audit logging** — Optional structured audit trail of MCP requests and tool invocations.
- **Payload limits** — Configurable max request body size for POST /mcp.
- **Strict schema validation** — Optional strict validation of tools/call arguments against the tool’s JSON Schema.
- **Optional signature validation** — (Stretch) Verify request body signature (e.g. HMAC) for webhook-style callers.

### 3.1 Audit logging

| Task | Description | Notes |
|------|--------------|--------|
| **Interface** | Define `IMcpAuditSink` with a method such as `Task AuditAsync(McpAuditEvent event, CancellationToken ct)`. Event: Timestamp, CorrelationId, Method (initialize | tools/list | tools/call), UserId?, ToolName? (for tools/call), StatusCode?, IsError?, DurationMs?, optional serialized params/result (redacted or hashed). | Keep payload minimal; avoid logging full arguments by default (PII). |
| **Registration** | Optional: register sink in DI; ZeroMCP calls it from `McpHttpEndpointHandler` and/or `McpToolHandler` after each request or tool call. | Default: no-op sink. |
| **Options** | `EnableAuditLogging` (default false), optional `AuditSink` or use DI `IMcpAuditSink`. Include/exclude params/result (never / on error / always, with size limit). | |
| **Docs** | [Observability](wiki/Observability), [Security Model](wiki/Security-Model), [Enterprise Usage](wiki/Enterprise-Usage). | |

### 3.2 Payload limits

| Task | Description | Notes |
|------|--------------|--------|
| **Option** | `MaxRequestPayloadSizeBytes` (int?; default null = use server default). Reject POST /mcp with body larger than limit before parsing JSON. | ASP.NET Core has `RequestSizeLimit`; we can document that or add an explicit check in the handler and return 413 with a clear message. |
| **Hook** | In `McpHttpEndpointHandler`, after checking Content-Type, read body (or use `Request.ContentLength`) and compare to limit; if over, return 413. | Respect `Request.ContentLength` when available; optional `Request.Body` read with limit. |
| **Docs** | [Configuration](wiki/Configuration), [Security Model](wiki/Security-Model). | |

### 3.3 Strict schema validation

| Task | Description | Notes |
|------|--------------|--------|
| **Option** | `EnableStrictSchemaValidation` (default false). When true, before dispatching tools/call, validate `params.arguments` against the tool’s `inputSchema` (JSON Schema). | Use a small JSON Schema validator (e.g. NJsonSchema or System.Text.Json schema) to avoid heavy dependency; validate and return a clear error (e.g. "Argument 'id' is required") on failure. |
| **Hook** | In `McpToolHandler.HandleCallAsync` (or in the handler that builds the args), after resolving the tool and before `DispatchAsync`, validate args. Return MCP error with validation details. | |
| **Docs** | [Configuration](wiki/Configuration), [Parameters and Schemas](wiki/Parameters-and-Schemas). | |

### 3.4 Optional signature validation (stretch)

| Task | Description | Notes |
|------|--------------|--------|
| **Option** | `RequireRequestSignature` (default false). When true, expect a header (e.g. `X-Signature`) with HMAC of body; verify using configured secret/key. Reject with 401 if invalid. | Useful for webhook-style or server-to-server callers. Defer to Phase 4.1 if time is short. |
| **Docs** | [Security Model](wiki/Security-Model). | |

---

## 4. Implementation Order and Dependencies

| Order | Area | Tasks | Dependency |
|-------|------|--------|-------------|
| 1 | **Payload limits** | Option + check in handler, 413 response | None |
| 2 | **Tool versioning** | Version + Deprecated on attribute and descriptor; tools/list + inspector | None |
| 3 | **Audit logging** | IMcpAuditSink, event type, call from handler, options | None |
| 4 | **Rate limiting** | Docs + optional policy name application to MCP endpoint; example | Optional: ASP.NET Core rate limiting package |
| 5 | **Strict schema validation** | Option, validator, hook in HandleCallAsync | JSON Schema lib |
| 6 | **Quota** | Document or minimal interface | After rate limiting |
| 7 | **Signature validation** | Stretch; defer if needed | None |

---

## 5. Acceptance Criteria (Summary)

- **Rate limiting:** Documented and/or optional policy applied to MCP endpoint; 429 + JSON-RPC error when exceeded.
- **Tool versioning:** `version` and `deprecated` on tools in tools/list and GET /mcp/tools; UI shows them; attribute and .AsMcp support.
- **Audit logging:** Optional IMcpAuditSink called for MCP requests/tool calls; configurable inclusion of params/result.
- **Payload limits:** Configurable max body size; 413 when exceeded.
- **Strict schema validation:** Optional validation of tools/call arguments; clear error on failure.
- **Docs:** Configuration, Enterprise Usage, Security Model, Observability, and versioning docs updated; progress.md and plan.md status updated.

---

## 6. Files to Touch (Estimated)

| Area | Files |
|------|--------|
| Options | `ZeroMcpOptions.cs` (rate limit policy name, payload limit, audit, strict validation) |
| Attributes / metadata | `McpAttribute.cs`, `McpToolEndpointMetadata.cs`, `McpToolDescriptor.cs` (version, deprecated) |
| Discovery | `McpToolDiscoveryService.cs` (pass version/deprecated), `McpHttpEndpointHandler.cs` (tools/list payload) |
| Handler | `McpHttpEndpointHandler.cs` (payload limit, audit, rate limit if applied here), `McpToolHandler.cs` (audit, strict validation) |
| Inspector | `McpInspectorUiHtml.cs`, `GetInspectorPayload` (version, deprecated) |
| New | `IMcpAuditSink.cs`, `McpAuditEvent.cs`, optional rate limit example in `examples/` |
| Docs | `wiki/Configuration.md`, `wiki/Enterprise-Usage.md`, `wiki/Security-Model.md`, `wiki/Observability.md`, `wiki/The-Mcp-Attribute.md`, `wiki/Versioning.md`, `README.md` |

---

## 7. References

- [plan.md](plan.md) — Phase 4 row (Month 6).
- [wiki/Enterprise-Usage.md](wiki/Enterprise-Usage.md) — Rate limiting mention ("Phase 4 may add built-in options").
- [wiki/Security-Model.md](wiki/Security-Model.md) — Attack surfaces and mitigations.
- ASP.NET Core: [Rate limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit), [Request size limits](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads#maximum-request-body-size).
