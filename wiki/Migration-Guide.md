# Migration Guide

How to upgrade from earlier phases of ZeroMCP (e.g. Phase 1 to Phase 2) and handle breaking or notable changes.

---

## Phase 1 to Phase 2

### Naming and API changes

- **Class rename:** **McpSwaggerToolHandler** was renamed to **McpToolHandler**. If you referenced this type by name (e.g. in tests or custom code), update to **McpToolHandler**.
- **Attribute:** The attribute remains **`[Mcp]`** (not `[McpTool]`). Ensure your code and docs use **`[Mcp]`** and **`.AsMcp(...)`** for minimal APIs (not `.WithMcpTool(...)`).
- **Options class:** Configuration is in **ZeroMCPOptions** (in **ZeroMCP.Options**). No SwaggerMcp-named options.

### New optional behavior (opt-in)

- **Result enrichment** — **EnableResultEnrichment**, **EnableSuggestedFollowUps**, **ResponseHintProvider**, **SuggestedFollowUpsProvider**. Default is **false**; no change to existing behavior until you enable these.
- **Streaming** — **EnableStreamingToolResults**, **StreamingChunkSize**. Default is **false**; **tools/call** response shape is unchanged until you enable streaming.
- **Metadata on tools** — **Category**, **Examples**, **Hints** on **`[Mcp]`** and **`.AsMcp(...)`**. Optional; existing tools without these fields continue to work.

### Backward compatibility

- **tools/list** — New optional fields (**category**, **tags**, **examples**, **hints**) are added when present. Clients that ignore unknown properties are unaffected.
- **tools/call** — With enrichment disabled, the result shape is unchanged (**content**, **isError**). With enrichment enabled, **metadata**, **suggestedNextActions**, and **hints** may appear.

---

## Phase 2 to Phase 3

### New features (additive)

- **Tool Inspector** — **GET {RoutePrefix}/tools** returns the full tool registry as JSON. Controlled by **EnableToolInspector** (default **true**). Set to **false** to disable the route.
- **Examples** — The repo adds **examples/** (Minimal, WithAuth, WithEnrichment, Enterprise). No change to the library API.

### Options

- **EnableToolInspector** — New option; default **true**. Set to **false** if you do not want the inspector endpoint registered.

---

## General upgrade checklist

1. **Pull latest** — Ensure you have the target version of ZeroMCP.
2. **Update references** — Replace any **McpSwaggerToolHandler** / **SwaggerMcp** naming with **McpToolHandler** / **ZeroMCP**.
3. **Build and test** — Run **dotnet build** and your test suite.
4. **Options** — Review [Configuration](Configuration) and [VERSIONING.md](https://github.com/ZeroMCP/ZeroMCP.net/blob/main/VERSIONING.md) for new options and default values.
5. **Docs** — Check the [wiki](Home) and release notes for the version you are upgrading to.

---

## Breaking change policy

ZeroMCP follows [SemVer](https://semver.org/). See **VERSIONING.md** in the repository for:

- What we consider a breaking change
- How we version the NuGet package
- Locked MCP protocol version and compatibility tests

---

## See also

- [Configuration](Configuration) — All options
- [Versioning](Versioning) — SemVer summary
- [Limitations](Limitations) — Known limitations
