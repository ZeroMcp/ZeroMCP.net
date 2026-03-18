# ZeroMCP Wiki

**ZeroMCP** exposes your existing ASP.NET Core API as an [MCP](https://modelcontextprotocol.io/) (Model Context Protocol) server with a single attribute and two lines of setup. No separate process. No code duplication.

Read [The ZeroMCP Story](TheStory) to understand why ZeroMCP exists

---

## Wiki pages

| Page | Description |
|------|-------------|
| [Quick Start](Quick-Start) | Install, register, map endpoint, tag actions |
| [Configuration](Configuration) | Options, route prefix, tool filters, observability, Phase 2 enrichment/streaming, Phase 3 inspector/UI, EnableXMLDocAnalysis |
| [Tool Inspector UI](Tool-Inspector-UI) | GET /mcp/ui: browse tools (by category), view schemas, invoke tools/call from the browser; auth and production |
| [Tool Versioning](Tool-Versioning) | Versioned endpoints /mcp/v1, /mcp/v2; Version on [Mcp] and .AsMcp; DefaultVersion; inspector version selector |
| [The [Mcp] Attribute](The-Mcp-Attribute) | Attribute usage, name, description, tags, category, examples, hints, roles, policy |
| [Resources and Prompts](Resources-and-Prompts) | `[McpResource]`, `[McpTemplate]`, `[McpPrompt]`; resources/list, resources/read, prompts/list, prompts/get |
| [Parameters and Schemas](Parameters-and-Schemas) | How route/query/body map to MCP input schema |
| [Controllers and Minimal APIs](Controllers-and-Minimal-APIs) | Using both together, minimal API `.AsMcp` |
| [Governance and Security](Governance-and-Security) | Roles, policy, per-request visibility, auth |
| [Enterprise Usage](Enterprise-Usage) | Production checklist, recommended options, health monitoring |
| [Security Model](Security-Model) | Auth flow, synthetic dispatch, header forwarding, attack surfaces |
| [Migration Guide](Migration-Guide) | Upgrading between phases, breaking changes, VERSIONING |
| [Observability](Observability) | Logging, correlation ID, metrics, OpenTelemetry |
| [Dispatch and Pipeline](Dispatch-and-Pipeline) | In-process dispatch, result enrichment, chunked responses |
| [Connecting MCP Clients](Connecting-Clients) | Claude Desktop, Claude.ai, production auth |
| [Versioning](Versioning) | SemVer, breaking-change policy, MCP protocol version |
| [Project Structure](Project-Structure) | Repo layout, build, test commands |
| [Performance](Performance) | Benchmarks, baseline numbers, how to reproduce |
| [Limitations](Limitations) | Known limitations and workarounds |
| [Contributing](Contributing) | How to contribute |

---

## Repository docs

- **README.md** (repo root) — Full documentation; same content expanded in this wiki.
- **VERSIONING.md** — Versioning and breaking-change policy (summary in [Versioning](Versioning)).
- **ZeroMCP/README.md** — NuGet package README (shipped inside the package).
- **progress.md** — Change log for development sessions.
