# Progress

## 2026-03-18 – MCP Resources, Resource Templates, and Prompts ([McpResource], [McpTemplate], [McpPrompt])

### New MCP protocol capabilities: resources and prompts support

**Attributes (ZeroMCP/Attributes/):**
- **McpResourceAttribute** — `[McpResource(uri, name)]` applied to controller actions; exposes the action as a static MCP resource at a fixed URI (e.g. `"resource://myapp/config"`). Supports `Description` and `MimeType`.
- **McpTemplateAttribute** — `[McpTemplate(uriTemplate, name)]` applied to controller actions; exposes the action as a URI-templated resource (RFC 6570 Level 1, e.g. `"resource://myapp/users/{id}"`). Template variables must match route/query parameter names on the action.
- **McpPromptAttribute** — `[McpPrompt(name)]` applied to controller actions; exposes the action as a reusable MCP prompt template. Action parameters become prompt arguments in `prompts/list`. Action response is wrapped as MCP prompt messages.

**Descriptors (ZeroMCP/Discovery/):**
- **McpResourceDescriptor** — Holds resource/template metadata (Name, Description, MimeType, IsTemplate, ResourceUri, UriTemplate) plus an internal `DispatchDescriptor` (McpToolDescriptor) for in-process dispatch and a pre-compiled `UriPattern` (Regex) for template matching.
- **McpPromptDescriptor** — Holds prompt metadata (Name, Description, Arguments) plus a `DispatchDescriptor` for dispatch.
- **McpPromptArgumentDescriptor** — Per-argument model (Name, Description, Required) derived from action parameters.

**Discovery Services (ZeroMCP/Discovery/):**
- **McpResourceDiscoveryService** — Scans `IApiDescriptionGroupCollectionProvider` for `[McpResource]` and `[McpTemplate]` on controller actions; builds separate static and template registries; caches at startup. `FindForUri(uri)` resolves exact URI matches (static) or regex template matches, returning extracted template variables for dispatch. URI templates are compiled to named-group regex patterns with Regex.Escape on literal segments.
- **McpPromptDiscoveryService** — Scans for `[McpPrompt]` on controller actions; builds a name-indexed registry; caches at startup. Derives prompt arguments from route/query parameters.

**Transport Handlers (ZeroMCP/Transport/):**
- **McpResourceHandler** — Handles `resources/list` (all static resources), `resources/templates/list` (all URI templates), and `resources/read` (dispatches via McpToolDispatcher with extracted args, returns `{ contents: [{ uri, text, mimeType? }] }`).
- **McpPromptHandler** — Handles `prompts/list` (all prompts with arguments) and `prompts/get` (dispatches action with provided arguments, wraps response as `{ description?, messages: [{ role: "user", content: { type: "text", text } }] }`).

**Updated Files:**
- **McpHttpEndpointHandler** — Constructor accepts optional `McpResourceHandler?` and `McpPromptHandler?`. `HandleInitialize` now dynamically builds the `capabilities` object, adding `resources: { listChanged: false, subscribe: false }` and `prompts: { listChanged: false }` when those handlers are wired in. Both `HandleAsync` and `ProcessMessageAsync` (stdio/SSE) route the five new JSON-RPC methods: `resources/list`, `resources/templates/list`, `resources/read`, `prompts/list`, `prompts/get`. Returns `-32601 Method not found` when the feature is not enabled.
- **ZeroMcpOptions** — Added `EnableResources` (default `true`) and `EnablePrompts` (default `true`).
- **ServiceCollectionExtensions** — Registers `McpResourceDiscoveryService`, `McpPromptDiscoveryService`, `McpResourceHandler`, `McpPromptHandler` as singletons.
- **EndpointRouteBuilderExtensions** — Resolves `McpResourceHandler` and `McpPromptHandler` from DI (conditional on `EnableResources`/`EnablePrompts`), pre-warms discovery caches, passes handlers to all `McpHttpEndpointHandler` constructor calls (unversioned and all versioned).

**Build:** `dotnet build` — 0 errors, 0 warnings on ZeroMCP library; 0 errors (53 pre-existing xUnit warnings) across full solution.

---

## 2026-03-06 – True IAsyncEnumerable<T> Streaming for tools/call

- **McpToolDescriptor**: Added `IsStreaming` (bool) and `StreamingElementType` (Type?) for streaming tool metadata.
- **ZeroMcpOptions**: Added `MaxStreamingItems` (default 10,000) — safety limit on enumeration.
- **McpToolDiscoveryService**: `DetectAsyncEnumerable` helper unwraps `Task<T>`/`ValueTask<T>` and checks for `IAsyncEnumerable<T>` on return types; `BuildDescriptor` now sets `IsStreaming` and `StreamingElementType` on controller action descriptors.
- **DispatchStreamChunk**: New type — `Content`, `IsLast`, `IsError` — represents a single chunk from the streaming dispatch.
- **McpStreamingCaptureFormatter**: Custom `TextOutputFormatter` that intercepts `IAsyncEnumerable<T>` results when the `ZeroMCP.CaptureStreaming` flag is set on `HttpContext.Items`. Stores the raw enumerable in Items instead of serializing to the response body.
- **ServiceCollectionExtensions**: Registers `McpStreamingCaptureFormatter` as the highest-priority output formatter via `MvcOptions.OutputFormatters.Insert(0, ...)`.
- **McpToolDispatcher**: Added `DispatchStreamingAsync` — uses a `Channel<DispatchStreamChunk>` producer/consumer pattern (background task produces via `ProduceStreamingChunksAsync`, reader exposes `IAsyncEnumerable`). Invokes the action through the normal pipeline; captures the `IAsyncEnumerable` via formatter; enumerates with `MaxStreamingItems` safety, cancellation, and error handling.
- **McpToolHandler**: Added `IsStreamingTool`, `GetStreamingDescriptorAsync`, `StreamToolAsync` methods; `McpToolDefinition.IsStreaming` property set during discovery; inspector payload includes `isStreaming` per tool.
- **McpHttpEndpointHandler**: `TryHandleStreamingToolCallAsync` — detects streaming tools on `tools/call` and writes SSE events (`event: chunk`, `event: done`, `event: error`) with `_meta.streaming`, `_meta.status`, `_meta.chunkIndex`. `tools/list` includes `streaming: true` on streaming tools. Added `WriteSseEventAsync` helper. Also added `IsStreamingToolCall` and `ProcessStreamingMessageAsync` for stdio transport.
- **McpStdioHostRunner**: Detects streaming tools via `IsStreamingToolCall` and writes multiple JSON-RPC lines per chunk via `ProcessStreamingMessageAsync`.
- **Inspector UI**: Added purple "STREAMING" badge (`streaming-badge` CSS), `data-streaming` attribute on tool cards, SSE-aware `invoke` function with progressive rendering (shows `[index] content` per chunk, final "--- Done ---" line).
- **Sample**: Added `stream_orders` endpoint on `OrdersController` — `[HttpGet("stream")][Mcp("stream_orders")]` returning `async IAsyncEnumerable<Order>` with 250ms delay per item.
- **Tests (McpStreamingTests)**: 5 integration tests — `ToolsList_StreamOrdersMarkedAsStreaming`, `ToolsList_NonStreamingToolsLackStreamingFlag`, `StreamOrders_ReturnsSSEEventStream` (SSE events with chunks + done), `StreamOrders_ChunkContentIsValidOrderJson`, `InspectorTools_StreamOrdersHasStreamingFlag`. All 72 tests pass.
- **Wire protocol**: SSE events for HTTP; multi-line JSON-RPC for stdio. Each chunk wraps JSON-RPC 2.0 with `result._meta` (`streaming: true`, `status: "streaming"|"done"|"error"`, `chunkIndex`, `totalChunks`).

---

## 2026-03-05 – WithStdio example

- **examples/WithStdio/** — New example: stdio transport via `--mcp-stdio`, Claude Desktop config, `health_check` and `echo` tools. README with run instructions, claude_desktop_config.json snippets, and manual testing. Added to ZeroMcp.slnx and README examples table.
  - **launchSettings.json**: Default profile launches with `--mcp-stdio`, `launchBrowser: false`; added "WithStdioExample (HTTP)" profile for HTTP mode with browser.

---

## 2026-03-05 – Priority 4+5: CancellationToken (complete) and File Upload ([FromForm])

- **Priority 4 (CancellationToken)** — Already implemented; marked plan acceptance criteria complete.
- **Priority 5 (File Upload)** — Implemented:
  - **McpToolDescriptor**: Added `FormFileParameters` (McpFormFileDescriptor) and `FormParameters`; `McpFormFileDescriptor` (Name, ParameterName, IsCollection).
  - **McpToolDiscoveryService**: Detects `IFormFile` and `IFormFileCollection` by type; `[FromForm]` strings via Source "Form"/"FormFile".
  - **McpSchemaBuilder**: Emits base64 schema for single file (`{name}`, `{name}_filename`, `{name}_content_type`) and array schema for collections.
  - **SyntheticHttpContextFactory**: `BuildFormCollection` decodes base64, creates `FormFile`, populates `FormCollection`; sets `IFormFeature` when `FormFileParameters.Count > 0`.
  - **ZeroMcpOptions**: Added `MaxFormFileSizeBytes` (default 10 MB).
  - **wiki/Parameters-and-Schemas.md**: Added "File Upload Tools" section.
  - **wiki/Limitations.md**, **README.md**: Updated [FromForm] to supported.
  - **plan-for-missing-transport.md**: Marked File Upload acceptance criteria complete (integration tests passing).
  - **SyntheticHttpContextFactory**: FormFile creation now initializes `Headers = new HeaderDictionary()` and sets `ContentType` so `document.ContentType` does not throw NRE; fixes `FormFile_UploadDocument_ReturnsFileInfo`.

---

## 2026-03-05 – Priority 6: Legacy SSE Transport (MCP spec 2024-11-05)

- **ZeroMcpOptions**: Added `EnableLegacySseTransport` (default: false).
- **McpLegacySseEndpointHandler**: New handler for GET `/mcp/sse` (creates session, sends `endpoint` event with `/mcp/messages?sessionId=` URL, holds SSE connection) and POST `/mcp/messages?sessionId=` (routes JSON-RPC via `McpHttpEndpointHandler`, writes response as SSE `message` event). Session store: `ConcurrentDictionary<string, SseSession>` with `Channel<string>` and `CancellationTokenSource`; cleanup on `RequestAborted`.
- **ZeroMcpEndpointBuilder** / **ZeroMcpEndpointBuilderExtensions**: `MapZeroMCP` returns builder; `WithLegacySseTransport()` adds SSE endpoints when opted in.
- **ServiceCollectionExtensions**: Registers `McpLegacySseEndpointHandler` as singleton.
- **Sample Program.cs**: Uses `WithLegacySseTransport()`.
- **Integration tests**: `McpLegacySseTests` — `LegacySse_GetSse_ReturnsEndpointEvent`, `LegacySse_InitializeAndToolsList_WorkOverSse`.
- **wiki/Limitations.md**: Documented Legacy SSE opt-in and horizontal scale limitation.
- **plan-for-missing-transport.md**: Marked Legacy SSE acceptance criteria complete.

---

## 2026-03-05 – Priority 3: Minimal API Query and Body Binding Parity

- **McpToolDiscoveryService**: `BuildMinimalApiDescriptor` now matches minimal API endpoints to `ApiDescription` (from `AddEndpointsApiExplorer`) by RelativePath and HttpMethod; extracts Query and Body from `ParameterDescriptions` (Source.Id: Query, Body), same as controller actions.
- **FindApiDescriptionForMinimalEndpoint**: New helper to locate ApiDescription for minimal endpoints; skips controller actions.
- **McpParameterDescriptor**: Added `DefaultValue` for optional params (e.g. `page = 1`).
- **McpSchemaBuilder**: `BuildPrimitiveProperty` accepts `defaultValue`; emits `"default"` in JSON Schema for query params.
- **Sample Program.cs**: Added `list_orders_minimal` (GET with status, page, pageSize) and `create_order_minimal` (POST with CreateOrderRequest body).
- **examples/Minimal/Program.cs**: Added `list_orders_minimal` example with query params.
- **Tests**: `BuildSchema_QueryParamsWithDefaults_IncludesDefaultInSchema`; `Priority3_MinimalApi_*` integration tests (conditional when tools available).
- **wiki/Parameters-and-Schemas.md**: Added minimal API examples (query params, [FromBody]).
- **plan-missing-transport.md**: Marked Priority 3 acceptance criteria for query params, defaults, and wiki update.

---

## 2026-03-05 – Priority 1+2: stdio Transport and CancellationToken

- **Priority 1 — stdio Transport**
  - **ZeroMcpOptions**: Added `StdioIdentity` (ClaimsPrincipal?) for fixed-identity stdio deployments.
  - **McpStdioExtensions**: New `RunMcpStdioAsync()` extension on WebApplication; supports `--mcp-stdio` CLI flag.
  - **McpStdioHostRunner**: New transport that reads newline-delimited JSON-RPC from stdin, routes through `McpHttpEndpointHandler.ProcessMessageAsync`, writes responses to stdout. Uses `StreamReader`/`StreamWriter`; overload accepts custom streams for testing.
  - **McpHttpEndpointHandler**: Extracted `ProcessMessageAsync(JsonDocument, HttpContext)` for reuse by HTTP and stdio; handles `initialize`, `tools/list`, `tools/call`, `notifications/initialized`, `notifications/cancelled`.
  - **Sample Program.cs**: Added stdio branch: `if (args.Contains("--mcp-stdio")) { await app.RunMcpStdioAsync(); return; }`.
  - **wiki/Connecting-Clients.md**: Added stdio (Option A) and HTTP (Option B) Claude Desktop examples; documented `--mcp-stdio` and published binary config.
  - **McpStdioTests**: Integration test `Stdio_Initialize_ReturnsServerInfo` using piped streams.

- **Priority 2 — CancellationToken**
  - **McpHttpEndpointHandler**: Added `_cancellationRegistry` (ConcurrentDictionary) for in-flight tools/call; `HandleToolsCallWithCancellationAsync` creates linked CTS, registers by request id, passes token to handler; `HandleCancelledAsync` handles `notifications/cancelled` and cancels by requestId; `OperationCanceledException` returns JSON-RPC error `-32800`.
  - **SyntheticHttpContextFactory**: Added `CancellableHttpRequestLifetimeFeature`; `Build` accepts `CancellationToken` and sets `RequestAborted` on synthetic context so `[Mcp]` actions receive cancellation.
  - **McpToolDispatcher**: Passes `cancellationToken` to `SyntheticHttpContextFactory.Build`.
  - **McpToolDiscoveryService**: Skips `CancellationToken` parameters in schema (excluded from MCP input schema).
  - **McpCancellationTests**: Integration test `Cancellation_NotificationsCancelled_Returns204`.

- **plan-missing-transport.md**: Acceptance criteria for stdio and CancellationToken addressed.

---

## 2026-03-05 – Missing Transport & Input Types implementation plan

- **plan-missing-transport.md** — New implementation plan derived from [.localplanning/plan-for-missing-transport.md](.localplanning/plan-for-missing-transport.md). Covers six features in priority order: (1) stdio Transport — RunMcpStdioAsync, --mcp-stdio, newline-delimited JSON-RPC, StdioIdentity; (2) CancellationToken — auto-binding, notifications/cancelled, -32800 on cancel; (3) Minimal API Binding Parity — query params, [AsParameters], validation mapping; (4) Streaming (IAsyncEnumerable) — detection, SSE partial results, streaming: true; (5) File Upload ([FromForm]) — base64 schema, FormFile construction, MaxFormFileSizeBytes; (6) Legacy SSE Transport — WithLegacySseTransport, GET /mcp/sse, POST /mcp/messages. Each section has Goals, task tables, acceptance criteria; plan includes implementation order, dependencies, files to touch, and references.

---

## 2026-03-05 – Tool versioning via versioned MCP endpoints

- **Model:** `McpToolAttribute.Version` (int, 0 = unversioned), `McpToolEndpointMetadata.Version`, `McpToolDescriptor.Version`, `ZeroMcpOptions.DefaultVersion`, `AsMcp(..., version: null)`.
- **Discovery:** `McpToolDiscoveryService` refactored to `ToolRegistry` with version buckets; `GetToolsForVersion`, `GetToolsForDefaultEndpoint`, `GetTool(name, version)`, per-version duplicate detection.
- **Handlers:** `McpToolHandler` and `McpHttpEndpointHandler` accept endpoint version and available versions; inspector payload includes `version`, `availableVersions`, per-tool `version`.
- **Routing:** `MapZeroMCP` registers `/mcp/v{n}`, `/mcp/v{n}/tools`, `/mcp/v{n}/ui` when versioned tools exist; single `/mcp` when not.
- **Inspector UI:** Version selector in topbar, version badges on tools, correct invoke/fetch targets for versioned endpoints.
- **Sample:** Versioned `get_order` v1/v2 in OrdersController; unversioned and versioned `health_check` in Program.cs.
- **Tests:** `McpVersioningTests` for v1/v2/404, default, inspector JSON/UI, tools/call scoped to version. All 55 tests pass.
- **Docs:** New [wiki/Tool-Versioning](wiki/Tool-Versioning.md); Configuration (DefaultVersion), The-Mcp-Attribute (Version), Tool-Inspector-UI (version selector, badges), Controllers-and-Minimal-APIs (version param), Migration-Guide (no migration), Limitations (integer versions), README (Tool Versioning section), plan-phase4.md (versioning complete).
- **Bug fix (EnforceAuthorizationAsync):** When `descriptor.Endpoint` is null (common for controller actions), `[Authorize]` was not enforced because `authorizeData` was empty. Fixed by falling back to reflection on `ControllerActionDescriptor.MethodInfo` and controller type for `[Authorize]`/`[AllowAnonymous]` attributes. Also fixed test header format (`Authorization: Bearer INVALID` instead of `Bearer: INVALID`) and `CreateOrder` to use explicit URL for `Created()`.

---

## 2026-03-04 – Pipeline build fix (Linux case-sensitivity)

- **ZeroMCP/ZeroMCP.csproj** — Set **EnableDefaultCompileItems** to **false** and added explicit **Compile Include** for every .cs file (root and subfolders **Ui**, **Observability**, **Metadata**). On Linux the default glob can fail to include subfolders or respect casing; explicit includes ensure **ZeroMCP.Options**, **ZeroMCP.Ui**, and **ZeroMCP.Transport** are always compiled. If the pipeline then fails with "file not found", ensure repo folder names match exactly: **Ui**, **Observability**, **Metadata** (e.g. `git mv ui Ui` if needed).

---

## 2026-03-03 – Plan: Completions and true streaming in Phase 6

- **plan.md** — Phase 6 (Month 10+): added **True streaming support** — end-to-end streaming for MCP transport (e.g. SSE or chunked encoding), streamed responses for tools/call and completions, back-pressure and cancellation. Completions support row unchanged.

---

## 2026-03-03 – Plan: Prompts support in Phase 5 (Month 7–9)

- **plan.md** — Added **Prompts support** to Phase 5: prompts/list, prompts/get, discovery/dispatch model (e.g. attributed prompts or registry). Outcome: MCP prompts protocol support for template-based prompts.

---

## 2026-03-03 – Phase 4 Part 1 (Option A): Rate limiting docs and example

- **examples/WithRateLimiting/** — New example: **AddRateLimiter** with fixed-window policy (10 req/10 sec), **UseRateLimiter()**, **MapZeroMcp().RequireRateLimiting("McpPolicy")**. **OnRejected** returns HTTP 429 and JSON-RPC–style error (`code: -32029`, "Rate limit exceeded"). README explains Option A and points to wiki.
- **wiki/Configuration.md** — New section **"Rate limiting (Option A)"**: use ASP.NET Core rate limiting; add policy, UseRateLimiter, RequireRateLimiting on MapZeroMcp; note inspector is separate; link to WithRateLimiting and Enterprise Usage; per-user/per-tool via custom partitioner.
- **wiki/Enterprise-Usage.md** — Replaced "Phase 4 may add built-in options" with concrete steps: AddRateLimiter, UseRateLimiter, MapZeroMcp().RequireRateLimiting; partition key for per-user/per-tool; link to WithRateLimiting and Configuration.
- **wiki/Security-Model.md** — Added mitigation row: "Rate limiting / abuse" → use ASP.NET Core rate limiting and WithRateLimiting example.
- **plan-phase4.md** — Marked Part 1 Option A completed; added pointer to progress.
- **README.md** — Examples table and project structure updated to include **WithRateLimiting**.
- **examples/WithRateLimiting/Program.cs** — Added `using Microsoft.AspNetCore.RateLimiting` so `AddFixedWindowLimiter` resolves (build fix).

---

## 2026-03-03 – Phase 4 implementation plan

- **plan-phase4.md** — New implementation plan for Phase 4 (Month 6): (1) Enterprise Features — rate limiting (options, partition key, hook point, 429 response) and quota (document or minimal); (2) Tool Versioning — version/deprecated on attribute and descriptor, tools/list and inspector, optional aliases; (3) Security Hardening — IMcpAuditSink, payload limits, strict schema validation, optional signature validation. Includes implementation order, acceptance criteria, and estimated files to touch. Recommendation: start rate limiting with docs + optional policy name; defer full per-tool/quota to follow-up if needed.
- **plan.md** — Phase 4 rows now link to plan-phase4.md.

---

## 2026-03-03 – Wiki: dedicated Tool Inspector UI page

- **wiki/Tool-Inspector-UI.md** — New page describing the Tool Inspector UI: URL (GET {RoutePrefix}/ui), when it’s available (EnableToolInspector + EnableToolInspectorUI), what you can do (browse, group by category, view schema, try it out), auth/roles (same HTTP context; test role-restricted tools while authenticated), JSON tool list link, production (disable or protect). See also links to Configuration, Governance, Security Model, Enterprise Usage.
- **wiki/Home.md** — Added [Tool Inspector UI](Tool-Inspector-UI) to wiki pages table.
- **wiki/README.md** — Added Tool Inspector UI to quick links table.

---

## 2026-03-03 – tools/call enforces roles/policy (no UI bypass)

- **ZeroMCP/McpToolHandler.cs** — Before dispatching **tools/call**, the handler now calls **IsVisibleAsync** when **sourceContext** is present. If the caller does not satisfy the tool’s RequiredRoles, RequiredPolicy, or ToolVisibilityFilter, **HandleCallAsync** returns an error and does not invoke the tool. This prevents the Tool Inspector UI (and any MCP client) from bypassing role-based access by calling a tool by name.
- **wiki/Governance-and-Security.md** — Call behavior updated: tools/call enforces same visibility as tools/list; added note that the UI uses the request’s HTTP context (cookies/headers) and to test role-restricted tools from the UI you must be authenticated with the required role.
- **wiki/Security-Model.md** — Role/policy section updated to state that tools/call enforces visibility and returns an error when not allowed (no bypass).
- **ZeroMCP.Tests/McpEndpointIntegrationTests.cs** — **Governance_ToolsCall_WithoutAuth_ToRoleRequiredTool_ReturnsError**: asserts that tools/call for `admin_health` without auth returns `result.isError` true and message contains "not available".

---

## 2026-03-03 – Tool Inspector UI: group tools by category

- **ZeroMCP/Ui/McpInspectorUiHtml.cs** — UI now groups tools by `category`: tools with the same category appear under a section heading; tools without a category appear under "(Uncategorized)". Category sections are sorted alphabetically with "(Uncategorized)" last. Added CSS for `.category-group` and `.category-group h2`.

---

## 2026-03-03 – Inspector/UI: production note and sample env switch

- **ZeroMCP.Sample/Program.cs** — EnableToolInspector and EnableToolInspectorUI set from `builder.Environment.IsDevelopment()` so /mcp/tools and /mcp/ui are only available in Development.
- **wiki/Configuration.md** — Tool Inspector section: added sentence that sample app enables both only when IsDevelopment().
- **README.md** — Tool Inspector: added sentence referencing sample’s Development-based switch.
- **ZeroMCP/README.md** — After options table: added “Set … to false (e.g. in production)” and reference to sample’s IsDevelopment() pattern.

---

## 2026-03-03 – Docs: UI, EnableXMLDocAnalysis, options in READMEs

- **wiki/Configuration.md** — Already had full options table and Tool Inspector/UI section; confirmed `EnableXMLDocAnalysis` and GET {RoutePrefix}/ui are documented.
- **wiki/Home.md** — How-it-works and index updated to mention GET /mcp/tools and GET /mcp/ui; Configuration page blurb mentions Phase 3 inspector/UI and EnableXMLDocAnalysis.
- **wiki/The-Mcp-Attribute.md** — Description bullet and placement rules now cross-reference `EnableXMLDocAnalysis` in Configuration.
- **README.md** — Configuration snippet: added `EnableToolInspectorUI = true` with comment; kept `EnableXMLDocAnalysis` and `EnableToolInspector`; options block is complete.
- **ZeroMCP/README.md** — Configuration summary table expanded: added `IncludeInputSchemas`, `EnableXMLDocAnalysis`, Phase 2 options (`EnableResultEnrichment`, `EnableSuggestedFollowUps`, `EnableStreamingToolResults`, `StreamingChunkSize`), and `EnableToolInspectorUI`; MapZeroMcp line updated to mention GET /mcp/ui.

---

## 2026-02-24 – Wiki pages

- **wiki/** — Added a full set of wiki pages for in-repo or GitLab/GitHub wiki use.
- **wiki/README.md** — Index and quick links for the wiki.
- **wiki/Home.md** — Overview, how it works, and table of all pages.
- **wiki/Quick-Start.md** — Install, register, map, tag actions.
- **wiki/Configuration.md** — Options, route prefix, ToolFilter, ToolVisibilityFilter, observability.
- **wiki/The-Mcp-Attribute.md** — [Mcp] attribute: name, description, tags, roles, policy.
- **wiki/Parameters-and-Schemas.md** — Route/query/body → JSON Schema.
- **wiki/Controllers-and-Minimal-APIs.md** — Using controllers and minimal APIs together, .AsMcp().
- **wiki/Governance-and-Security.md** — Roles, policy, ToolFilter, ToolVisibilityFilter, auth.
- **wiki/Observability.md** — Logging, correlation ID, metrics, OpenTelemetry.
- **wiki/Dispatch-and-Pipeline.md** — In-process dispatch, auth forwarding, CreatedAtAction.
- **wiki/Connecting-Clients.md** — Claude Desktop, Claude.ai, production auth.
- **wiki/Versioning.md** — SemVer summary and link to VERSIONING.md.
- **wiki/Project-Structure.md** — Repo layout, build, test commands.
- **wiki/Limitations.md** — Known limitations and workarounds.
- **wiki/Contributing.md** — How to contribute, high-impact ideas.
- **README.md** — Add a short "Wiki" bullet under Project Structure or a dedicated line pointing to wiki/ for detailed docs.

---

## 2026-02-24 – Build fix after Order/Repository drift

- **Order** (OrdersController): Restored `int? CustomerId`; removed string `CustomerID` / `ProductID` that had been added (and caused SampleData/CustomerController to fail). Order shape is again: Id, CustomerId, CustomerName, Product, Quantity, Status.
- **Repository.cs**: Updated to use **int** Ids for Customer and Product and **CustomerId** (int) / **CustomerName** / **Product** on Order so it compiles with the shared Customer/Product/Order types.
- **OrdersController**: Wired back to **SampleData.Orders** (single store) and added `using SampleApi` so `SampleData` is in scope.

---

## 2026-02-24 – Customer/Product controllers, nested routing, tests

- **ZeroMCP.Sample**
  - **Models:** `Customer` and `Product` in `SampleApi` with public setters; `Order` in `OrdersController` given `CustomerId` and wired to `SampleData.Orders`.
  - **SampleData:** Shared in-memory store (`SampleData.Customers`, `SampleData.Products`, `SampleData.Orders`); `SampleData.cs` uses `SampleApi.Controllers` for `Order` type.
  - **OrdersController:** Uses `SampleData.Orders` as single store; create/list/get/update/delete unchanged; all MCP tools unchanged.
  - **CustomerController:** `api/Customer` — GET list, GET `{id}`, GET `{id}/orders` (nested route), POST create; all actions tagged with `[Mcp("...")]` (list_customers, get_customer, get_customer_orders, create_customer).
  - **ProductController:** `api/Product` — GET list, GET `{id}`, POST create; MCP tools list_products, get_product, create_product.
- **ZeroMCP.Tests**
  - **Project references:** Updated from `..\ZeroMCP\` and `..\ZeroMCP.Sample\` to `..\ZeroMcp\ZeroMCP.csproj` and `..\ZeroMCP.Sample\ZeroMCP.Sample.csproj` so build and tests run after rename.
  - **ToolsList_ReturnsTaggedToolsOnly:** Now asserts presence of list_customers, get_customer, get_customer_orders, create_customer, list_products, get_product, create_product.
  - **GetCustomerOrders_ToolsCall_ReturnsOrdersForCustomer:** New integration test; calls `get_customer_orders` with `id: 1` and asserts result content is an array with at least one order (id=1, customerName=Alice, product=Widget).
- **README.md:** Project structure and build commands updated to ZeroMcp / ZeroMCP.Sample / ZeroMCP.Tests; sample line notes Customer/Product and nested route `Customer/{id}/orders`.

---

## 2026-02-24 – Two READMEs (GitLab vs NuGet)

- **Repository (GitLab):** Root **README.md** — full documentation, build, tests, contributing, project structure. Intro now states it is the repo README and that the NuGet package has its own README.
- **NuGet package:** **ZeroMCP/README.md** — consumer-focused: install, quick start, configuration summary table, governance/metrics one-liners, link to full docs. Packed into the NuGet via `None Include="README.md"` in ZeroMCP.csproj (replaced `..\README.md`).
- **ZeroMCP.csproj:** Pack `README.md` from project directory instead of repo root.
- **README.md:** Added "Two READMEs" section describing both files and that feature changes should update both.

---

## 2026-02-24 – Phase 1 Observability

### Completed

1. **Structured logging**
   - **McpHttpEndpointHandler**: Log scope for each request with `CorrelationId`, `JsonRpcId`, `Method`. Logs completion with `Method`, `DurationMs`; warnings for method not found / invalid params; error for unhandled exception.
   - **McpToolHandler**: Per tool invocation logs `ToolName`, `StatusCode`, `IsError`, `DurationMs`, `CorrelationId` (Debug on success, Warning on error). Unknown tool logs Warning with ToolName and CorrelationId.

2. **Execution timing**
   - **McpHttpEndpointHandler**: `Stopwatch` around the method switch; logs `DurationMs` on completion and on error paths.
   - **McpToolHandler**: `Stopwatch` around `DispatchAsync`; duration passed to metrics sink and logs.

3. **Success/failure tracking**
   - Tool invocations record `StatusCode`, `IsError` (!result.IsSuccess) and feed them into logs and **IMcpMetricsSink**.

4. **Correlation ID propagation**
   - **CorrelationIdHeader** (default `X-Correlation-ID`): read from request or generate new GUID; set on `context.Items[McpCorrelationId]`; echoed in response via `OnStarting`.
   - Log scope and tool logs include CorrelationId.
   - **SyntheticHttpContextFactory**: copies correlation ID from source context into synthetic `TraceIdentifier` and `Items` so dispatched actions see the same ID.

5. **IMcpMetricsSink**
   - **Observability/IMcpMetricsSink.cs**: `RecordToolInvocation(toolName, statusCode, isError, durationMs, correlationId)`.
   - **NoOpMcpMetricsSink**: default registration; app can register its own after `AddZeroMcp()` to push to Prometheus/AppInsights/etc.

6. **Optional OpenTelemetry**
   - **EnableOpenTelemetryEnrichment** in `ZeroMCPOptions` (default false). When true, `McpToolHandler` tags `Activity.Current` with `mcp.tool`, `mcp.status_code`, `mcp.is_error`, `mcp.duration_ms`, `mcp.correlation_id`.

7. **Options**
   - **CorrelationIdHeader** (default `"X-Correlation-ID"`); set to null/empty to disable.
   - **EnableOpenTelemetryEnrichment** (default false).

8. **Tests**
   - **Observability_CorrelationId_EchoedInResponse**: sends `X-Correlation-ID`, asserts response header echoes the same value.

9. **Docs**
   - README: new "Observability (Phase 1)" subsection and Configuration snippet for CorrelationIdHeader and EnableOpenTelemetryEnrichment.

---

## 2026-02-24 – Phase 1 Governance & Tool Control

### Completed

1. **Role-based tool exposure**
   - `[McpTool]`: added **Roles** (string[]). When set, tool is only included in `tools/list` if the current user is in at least one role.
   - `.WithMcpTool(..., roles: new[] { "Admin" })` for minimal APIs.
   - `McpToolDescriptor` and `McpToolEndpointMetadata` carry `RequiredRoles` / `Roles`.

2. **Policy-based exposure**
   - `[McpTool]`: added **Policy** (string). When set, tool is only listed if `IAuthorizationService.AuthorizeAsync(user, null, policy)` succeeds.
   - `.WithMcpTool(..., policy: "RequireEditor")` for minimal APIs.

3. **Environment / per-request filtering**
   - **ToolFilter** (existing): discovery-time filter by name; documented for environment-specific exclusions.
   - **ToolVisibilityFilter** (new): `Func<string, HttpContext, bool>?` — run at `tools/list` time; return true to include the tool. Enables custom logic (user, headers, feature flags).

4. **Integration with ASP.NET authorization**
   - `tools/list` now uses **GetToolDefinitionsAsync(HttpContext)** so each request gets a filtered list.
   - Visibility: (1) if descriptor has RequiredRoles, user must be in one role; (2) if descriptor has RequiredPolicy, resolved `IAuthorizationService` must authorize; (3) if ToolVisibilityFilter is set, it must return true.
   - Dispatch already enforced `[Authorize]` (401 when unauthenticated). No change to dispatch behavior.

5. **Implementation details**
   - `McpToolHandler`: injects `IOptions<ZeroMCPOptions>`, adds `GetToolDefinitionsAsync(context, cancellationToken)` and private `IsVisibleAsync(descriptor, context)`.
   - `McpHttpEndpointHandler.HandleToolsList` → **HandleToolsListAsync(context)** calling `GetToolDefinitionsAsync(context)`.
   - README: new subsection "Governance & tool control", updated Configuration and `[McpTool]` examples.

6. **Tests (Governance)**
   - **ZeroMCP.Sample**: `ApiKeyAuthenticationHandler` accepts "admin-key" and adds `ClaimTypes.Role` "Admin"; added minimal endpoint `admin_health` with `.WithMcpTool("admin_health", ..., roles: new[] { "Admin" })`.
   - **ZeroMCP.Tests**: `PostMcpAsync(body, headers)` overload to send request headers (e.g. `X-Api-Key`).
   - **Governance_ToolsList_WithoutAuth_ExcludesRoleRequiredTool**: tools/list without auth → `admin_health` not in list.
   - **Governance_ToolsList_WithAdminKey_IncludesRoleRequiredTool**: tools/list with `X-Api-Key: admin-key` → `admin_health` in list.
   - **ToolsList_ReturnsTaggedToolsOnly**: now asserts `admin_health` is not in list (no auth) and `health_check` is in list.

---

## 2026-02-24 – Phase 1 Production Hardening

### Completed

1. **Lock MCP protocol version**
   - Added `ZeroMCP/McpProtocolConstants.cs` with `McpProtocolConstants.ProtocolVersion = "2024-11-05"`.
   - `McpHttpEndpointHandler` now uses this constant for GET example and `initialize` response (single source of truth).

2. **Semantic versioning**
   - Package already uses SemVer (e.g. 1.0.2). Documented in VERSIONING.md.

3. **Compatibility tests**
   - In `ZeroMCP.Tests/McpEndpointIntegrationTests.cs`:
     - `Compatibility_ProtocolVersion_IsLocked` — asserts `initialize` returns locked protocol version.
     - `Compatibility_Request_WithoutMethod_ReturnsInvalidRequest` — JSON-RPC missing method returns -32600.
     - `Compatibility_ToolsList_EachToolHasRequiredFields` — each tool has name, description, inputSchema.
     - `Compatibility_ErrorResponse_HasCodeAndMessage` — method not found returns error with code and message.
   - `Initialize_ReturnsServerInfo` updated to use `McpProtocolConstants.ProtocolVersion` in request and assertion.

4. **Breaking change policy**
   - Added `VERSIONING.md`: SemVer rules, what we consider breaking, locked MCP protocol version, non-breaking changes, compatibility tests, and release notes guidance.
   - README: added link to VERSIONING.md from Quick Start.

---

## 2026-02-24 – Combo (controllers + minimal API): only minimal served

### Issue

Apps that expose both controller actions (`[McpTool]`) and minimal API endpoints (`.WithMcpTool(...)`) were only showing minimal API tools in `tools/list` (e.g. testingAPI at https://localhost:7196).

### Cause

Controller tools are discovered from `IApiDescriptionGroupCollectionProvider`. That provider is populated only when **`AddEndpointsApiExplorer()`** is registered. Without it, controller actions are never added to the registry; only minimal API tools (from `EndpointDataSource`) appear.

### Changes

1. **README.md**
   - Added subsection **"Using controllers and minimal APIs together"** under Configuration.
   - Documented that `AddControllers()` and **`AddEndpointsApiExplorer()`** are required for controller tool discovery when using a combo.
   - Included minimal Program.cs snippet: AddControllers, AddEndpointsApiExplorer, MapControllers, minimal APIs, MapZeroMcp.

### Fix for your app (e.g. testingAPI)

In `Program.cs` ensure:

```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();   // required so controller [McpTool] actions are discovered
// ... AddZeroMCP(...) ...

app.MapControllers();
// then minimal APIs with .WithMcpTool(...)
app.MapZeroMcp();
```

After adding `AddEndpointsApiExplorer()`, restart the app; both controller and minimal tools should appear in `tools/list`.

---

## 2026-02-24 – Build errors resolved

### Changes made

1. **ZeroMCP.csproj**
   - Removed invalid `Microsoft.AspNetCore` (Version 2.3.9) package reference; it was unnecessary for net10.0 and caused NU1510.
   - Added **NJsonSchema** (11.0.0) for `McpSchemaBuilder` (JSON Schema generation, `SystemTextJsonSchemaGeneratorSettings`, `JsonObjectType`).
   - Added **Swashbuckle.AspNetCore** (7.2.0) for `Program.cs` (AddSwaggerGen, UseSwagger, UseSwaggerUI).

2. **McpToolHandler.cs**
   - Removed unused `using ModelContextProtocol.Server;` (types `McpToolDefinition` and `McpToolResult` are defined in the same file).

3. **SyntheticHttpContextFactory.cs**
   - Made `SyntheticHttpContextFactory` **public** to fix CS0051 (parameter type less accessible than `McpToolDispatcher` constructor).
   - Replaced `RequestServicesFeature(scope.ServiceProvider)` with custom **SyntheticRequestServicesFeature** (ASP.NET Core 10 constructor expects `(HttpContext, IServiceScopeFactory?)`).
   - Removed **HttpActivityFeature** usage (type is inaccessible in ASP.NET Core 10).

4. **McpSchemaBuilderTests.cs**
   - Replaced `typeof(string?)` with `typeof(string)` for the optional "filter" query parameter to fix CS8639 (typeof on nullable reference type).

5. **TestService**
   - **CreateOrderRequest.cs** and **Order.cs**: Initialized `ProductName` with `= string.Empty` to clear CS8618 (non-nullable property when exiting constructor).

### Build status

- `dotnet build TestService\TestService.csproj` — **succeeds** (0 errors, 0 warnings).
- Application (TestService) builds and is ready to run; ensure `Program.cs` registers controllers and ZeroMCP if you use the Orders API and MCP endpoint.

### Later fix: FluentAssertions JSON API

- **McpEndpointIntegrationTests.cs:** Replaced `ContainKey("result")` / `ContainKey("error")` with **`HaveProperty("result")`** / **`HaveProperty("error")`**. `JsonNodeAssertions<JsonObject>` uses `HaveProperty` / `NotHaveProperty`, not `ContainKey`.

### GET /mcp returning something

- **McpHttpEndpointHandler:** For **GET** requests to `/mcp`, now return a JSON description (protocol, server name/version, example initialize payload). **POST** unchanged (JSON-RPC 2.0).
- **EndpointRouteBuilderExtensions:** Route registered with **MapMethods(route, ["GET", "POST"], ...)** so both GET and POST are handled at `/mcp`.

### NuGet package layout

- **ZeroMCP** is now a library-only project that packs as a NuGet package.
  - **ZeroMCP.csproj:** OutputType=Library, only NJsonSchema dependency; PackageId=ZeroMCP, Version=1.0.0; Compile Remove for Program.cs, OrdersController.cs, *Tests.cs (moved to other projects).
  - **ZeroMCP.Sample:** Standalone sample app (Program.cs, Controllers/OrdersController.cs), references ZeroMCP + Swashbuckle.
  - **ZeroMCP.Tests:** Unit and integration tests; references ZeroMCP and ZeroMCP.Sample (WebApplicationFactory&lt;Program&gt;).
- **Pack:** `dotnet pack ZeroMCP\ZeroMCP.csproj -c Release -o .\nupkgs` produces **ZeroMCP.1.0.0.nupkg**.
- **TestService** still references ZeroMCP via ProjectReference (unchanged).

### Phase 1 + Phase 2 (2026-02-24)

**Phase 1:** Auth token forwarding via `ZeroMCPOptions.ForwardHeaders` and `sourceContext` through factory/dispatcher/handler. XML doc descriptions via `XmlDocHelper.GetMethodSummary` when `[McpTool].Description` is null.

**Phase 2:** Minimal API support: `McpToolDescriptor.Endpoint`, `McpToolEndpointMetadata`, `WithMcpTool` extension; discovery from `EndpointDataSource.Endpoints`; dispatch branch for minimal endpoints (`DispatchMinimalEndpointAsync`). Discovery uses `EndpointDataSource` (not IEndpointDataSource).

**Sample:** ZeroMCP.Sample Program.cs now includes a minimal API example: `GET /api/health` with `.WithMcpTool("health_check", "Returns API health status.", tags: new[] { "system" })`.

**create_order 500 fix:** Controller actions now get their matching RouteEndpoint from EndpointDataSource (by ControllerActionDescriptor.Id) and the dispatcher sets context.SetEndpoint before invoking so CreatedAtAction/LinkGenerator no longer hit IRouter/ActionContext 500.

**CreatedAtAction robustness:** FindEndpointForAction now falls back to matching by ControllerName+ActionName when Id does not match. Synthetic request sets PathBase = Empty and Path with trimmed RelativeUrl. Log a warning when no endpoint is found for a controller action so link generation failures can be diagnosed. **"No route matches the supplied values" fix:** Synthetic request route values now include ambient `controller` and `action` from the ActionDescriptor so LinkGenerator/CreatedAtAction can resolve the target action (e.g. GetOrder) when generating the Location URL.

## 2026-02-24 – Expanded MCP validation/schema/auth test coverage

### Changes made

1. **ZeroMCP.Sample/Program.cs**
   - Added authentication/authorization services:
     - `AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)`
     - `AddAuthorization()`
   - Added `app.UseAuthentication()` before `app.UseAuthorization()`.

2. **ZeroMCP.Sample/ApiKeyAuthenticationHandler.cs** (new)
   - Added a lightweight API-key auth handler (`X-Api-Key: dev-key`) for sample authorization scenarios.
   - Missing/invalid key yields unauthenticated requests, enabling deterministic unauthorized behavior in tests.

3. **ZeroMCP.Sample/Controllers/OrdersController.cs**
   - Added protected MCP tool:
     - `get_secure_order` (`[Authorize]`) for auth failure-path verification.
   - Added status value validation on `UpdateStatusRequest.Status`:
     - `[RegularExpression("^(pending|shipped|cancelled)$", ...)]`
   - This enables a concrete invalid-status model-validation test case.

4. **ZeroMCP.Tests/McpEndpointIntegrationTests.cs**
   - Added tool-list schema shape assertions (not just tool-name presence).
   - Added MCP transport and validation/error tests for:
     - malformed JSON body parse errors (`-32700`)
     - create-order model-state failure (missing required fields)
     - wrong argument type (`id` as string instead of int)
     - empty `{}` arguments for a required-params tool
     - valid route + invalid body value (`update_order_status`)
     - unauthorized protected endpoint call returning MCP error content (HTTP 401 wrapped in MCP result)
   - Updated tool list assertions to include `get_secure_order`.

5. **ZeroMCP.Tests/McpSchemaBuilderTests.cs**
   - Expanded schema-builder coverage for:
     - `[Required]` body properties flowing into `required[]`
     - nullable primitive query parameter generating `["type","null"]`
     - body + route merged schema + required propagation
     - nested complex body property shape (`object`)
     - collection properties (`List<string>`, arrays) mapping to `array`
     - enum property containing an `enum` array
     - empty body type yielding object schema with no properties

6. **README.md**
   - Added a test-coverage highlights section documenting the expanded validation/schema/auth scenarios.

### Verification status in this environment

- Attempted:
  - `dotnet build ZeroMCP.Tests/ZeroMCP.Tests.csproj -v detailed`
  - `dotnet` location checks (`command -v dotnet`, `whereis dotnet`, `/usr/share/dotnet`)
- Result:
  - Build/test execution is currently blocked on this runner because the .NET SDK is not installed (`dotnet: command not found`).

## Failing tests fixed (ToolsList_ReturnsExpectedInputSchemaShapes)

- **Cause:** Integration test expected `update_order_status` input schema to have `status.pattern` (from `[RegularExpression]` on `UpdateStatusRequest.Status`). NJsonSchema does not populate `Pattern` from `[RegularExpression]` by default, so the emitted schema had no `pattern`.
- **Fix:** In **McpSchemaBuilder.ExtractBodyProperties**, after building each body property from NJsonSchema, call new **GetRegularExpressionPattern(bodyType, propName)** to get `[RegularExpression].Pattern` via reflection and set `propObj["pattern"]` when present. Added `using System.ComponentModel.DataAnnotations` and `System.Reflection`; null/empty propertyName and PascalCase fallback for property lookup.
- **Test:** **McpEndpointIntegrationTests** line 247: **TestContext.Current.TestOutputHelper** dereference warning fixed with `TestContext.Current?.TestOutputHelper?.WriteLine(...)`.

## README.md updated for current project state

- How It Works: discovery from controllers + minimal APIs; GET and POST /mcp; dispatch to action or endpoint.
- Quick Start: package version 1.0.2; MapZeroMcp registers GET and POST.
- In-Process Dispatch: synthetic context has ambient controller/action and endpoint; CreatedAtAction supported.
- Minimal API section moved before Connecting MCP Clients; single consolidated section.
- Project Structure: reflects Metadata/, Options/, controller + minimal discovery, sample with health + optional auth.
- Known Limitations: streamlined; CreatedAtAction as fallback note only.
- Build: targets net9.0 and net10.0; simplified test coverage paragraph.
- NuGet: version 1.0.2; NJsonSchema-only dependency note.

---

## 2026-03-03 – Phase 2 metadata and descriptions (Category/Examples/Hints)

- **ZeroMCP core**
  - `McpToolDescriptor`: added `Category`, `Examples`, and `Hints` properties so tools can carry AI-native metadata alongside tags and descriptions.
  - `McpToolAttribute`: extended to accept optional `Category`, `Examples`, and `Hints` for controller-based tools.
  - `McpToolEndpointMetadata` and `McpToolEndpointExtensions.AsMcp(...)`: minimal API tools can now specify `category`, `examples`, and `hints` in addition to tags/roles/policy.
  - `McpToolDiscoveryService`: wires new metadata from attributes and minimal API metadata into `McpToolDescriptor` so discovery captures the full AI-facing shape.
  - `McpToolHandler`: `McpToolDefinition` now exposes `Category`, `Tags`, `Examples`, and `Hints` to MCP clients; `BuildDescription` was updated to emit LLM-optimized descriptions that include method/route, category, tags, and hints in a concise form.

- **Build**
  - `dotnet build ZeroMCP.slnx -v detailed` — **succeeds** after metadata and description changes (no compilation errors).

---

## 2026-03-03 – Phase 2 result enrichment, streaming, and rename

- **Result enrichment**
  - `ZeroMCPOptions`: added `EnableResultEnrichment`, `EnableSuggestedFollowUps`, `ResponseHintProvider`, `SuggestedFollowUpsProvider`. When enabled, `tools/call` result can include `metadata` (statusCode, contentType, correlationId, durationMs), `suggestedNextActions` (toolName + rationale), and `hints`.
  - `McpToolResult`: added optional `StatusCode`, `DurationMs`, `CorrelationId`, `SuggestedNextActions`, `Hints`; internal factories `SuccessWithEnrichment`, `ErrorWithEnrichment`, `SuccessWithSuggested`, `ErrorWithSuggested`.
  - `McpHttpEndpointHandler.HandleToolsCallAsync`: when result carries enrichment data, response JSON includes metadata, suggestedNextActions, hints.

- **Streaming / partial responses**
  - `ZeroMCPOptions`: added `EnableStreamingToolResults` (default false), `StreamingChunkSize` (default 4096). When enabled, `tools/call` content is split into chunks with `chunkIndex` and `isFinal` for streaming-aware clients; single response body, backward compatible when off.

- **tools/list optional fields**
  - `McpHttpEndpointHandler.HandleToolsListAsync`: each tool object can now include optional `category`, `tags`, `examples`, `hints` when present (Phase 2 metadata).

- **Legacy SwaggerMcp naming removed**
  - Renamed `McpSwaggerToolHandler` → `McpToolHandler` (class and file `McpToolHandler.cs`); updated all references in `McpHttpEndpointHandler`, `ServiceCollectionExtensions`, `EndpointRouteBuilderExtensions`.
  - `progress.md` and `VERSIONING.md`: replaced SwaggerMcp/MCPSwagger references with ZeroMCP naming; `ZeroMCP/Properties/launchSettings.json` profile name updated to ZeroMcp.

- **Auth propagation fix**
  - `SyntheticHttpContextFactory.Build`: now copies `sourceContext.User` onto the synthetic `HttpContext` so that `[Authorize]` and authorization filters see the same identity as the MCP request. Previously the synthetic request bypassed the auth middleware, so User was never set and valid logins could be rejected (or the action could see an incorrect identity). Auth runs on the MCP request before the handler; we now propagate that User into the synthetic context used for dispatch.

- **Tests**
  - `Phase2_ToolsList_WhenToolHasTags_IncludesTagsInResponse`: asserts health_check tool in tools/list has `tags` including "system".
  - `Phase2_ToolsCall_WithoutEnrichment_ResultHasOnlyContentAndIsError`: asserts default tools/call result has only `content` and `isError` (no metadata).

- **Docs**
  - README: added Phase 2 options snippet (EnableResultEnrichment, EnableSuggestedFollowUps, EnableStreamingToolResults, StreamingChunkSize); extended `[Mcp]` attribute example with Category, Examples, Hints.

---

## 2026-03-03 – Wiki updated for Phase 2

- **wiki/Home.md**
  - Updated page descriptions to include Phase 2 enrichment/streaming and expanded `[Mcp]` metadata fields (category/examples/hints).

- **wiki/Configuration.md**
  - Added Phase 2 option examples and option-table entries for:
    - `EnableResultEnrichment`
    - `EnableSuggestedFollowUps`
    - `ResponseHintProvider`
    - `SuggestedFollowUpsProvider`
    - `EnableStreamingToolResults`
    - `StreamingChunkSize`

- **wiki/Quick-Start.md**
  - Added optional Phase 2 config snippet.
  - Updated “Next steps” descriptions to include enrichment/streaming and extended metadata fields.

- **wiki/The-Mcp-Attribute.md**
  - Added `Category`, `Examples`, and `Hints` to full attribute sample and parameter table.

- **wiki/Controllers-and-Minimal-APIs.md**
  - Expanded `.AsMcp(...)` example with category/examples/hints.
  - Added those fields to the minimal API option list.

- **wiki/Dispatch-and-Pipeline.md**
  - Clarified auth context propagation wording.
  - Added sections documenting Phase 2 enriched `tools/call` response fields and chunked response behavior.

---

## 2026-03-03 – README cleanup (naming + examples)

- **README.md (repo root)**
  - Fixed malformed controller sample attributes (`[ApiController]`, `[Route("api/[controller]")]`).
  - Replaced stale naming and usage references:
    - `[McpTool]` → `[Mcp]`
    - `.WithMcpTool(...)` → `.AsMcp(...)`
  - Updated project structure bullets to use current symbols (`[Mcp]`, `AsMcp`).

- **ZeroMCP/README.md (NuGet/package README)**
  - Replaced stale API casing and names:
    - `AddZeroMCP`/`MapZeroMCP` → `AddZeroMcp`/`MapZeroMcp`
    - `[McpTool(...)]`/`.WithMcpTool(...)` → `[Mcp(...)]`/`.AsMcp(...)`
  - Updated metrics note to current registration method (`AddZeroMcp()`).

- **Verification**
  - `dotnet build ZeroMCP.slnx -v detailed` — succeeds after README corrections.

---

## 2026-03-03 – plan.md phase order update

- **plan.md**
  - Swapped the order of Phase 3 and Phase 4 per request:
    - **Phase 3 (Month 5)** is now **Developer Experience**.
    - **Phase 4 (Month 6)** is now **Enterprise Features**.
  - Preserved all task content; only phase ordering/timeline association changed.

---

## 2026-03-03 – Phase 3 Developer Experience (Increment 1: Examples)

- **examples/** — Added four standalone example projects, all referencing ZeroMcp via ProjectReference, targeting net8.0;net9.0;net10.0.
  - **Minimal** — MinimalExample.csproj, Program.cs, WeatherController with `[Mcp("get_weather")]`, health minimal API with `.AsMcp("health_check")`, README.md.
  - **WithAuth** — WithAuthExample.csproj, ApiKey auth handler, SecureController (public/secure/admin actions), minimal APIs with roles, README.md.
  - **WithEnrichment** — WithEnrichmentExample.csproj, EnableResultEnrichment, EnableSuggestedFollowUps, EnableStreamingToolResults, ResponseHintProvider, SuggestedFollowUpsProvider, CatalogController with Category/Examples/Hints, README.md.
  - **Enterprise** — EnterpriseExample.csproj, full auth + enrichment + ToolFilter + ToolVisibilityFilter + CorrelationIdHeader + OpenTelemetry, ItemsController (CRUD + admin), README.md.
- **ZeroMcp.slnx** — Added folder `/examples/` with all four example projects. Build verified with `dotnet build`.

---

## 2026-03-03 – Phase 3 Developer Experience (Increment 2: Tool Inspector)

- **ZeroMcpOptions** — Added **EnableToolInspector** (default `true`). When true, GET {RoutePrefix}/tools is registered.
- **McpToolHandler** — Added **GetInspectorPayload()** building JSON-shaped payload (serverName, serverVersion, protocolVersion, toolCount, tools[]) from discovery; each tool has name, description, httpMethod, route, inputSchema, category, tags, examples, hints, requiredRoles, requiredPolicy.
- **McpHttpEndpointHandler** — Added **HandleToolsInspectorAsync(HttpContext)** writing the payload as application/json.
- **EndpointRouteBuilderExtensions** — When **EnableToolInspector** is true, **MapGet(route + "/tools", ...)** registers the inspector endpoint. Inspector respects ToolFilter (discovery-time) only; no per-request visibility.

---

## 2026-03-03 – Phase 3 Developer Experience (Increment 2: Inspector tests)

- **McpEndpointIntegrationTests** — Added **Phase3_Inspector_Get_ReturnsJsonWithTools** (GET /mcp/tools returns 200, serverName, protocolVersion, toolCount, tools with name/description/httpMethod/route/inputSchema). **Phase3_Inspector_EachToolHasSchemaShape** (each tool has inputSchema.type). **McpInspectorDisabledTests** with **DisabledInspectorWebApplicationFactory** (IPostConfigureOptions&lt;ZeroMCPOptions&gt; sets EnableToolInspector = false) and **Phase3_Inspector_WhenDisabled_Returns404**.

---

## 2026-03-03 – Phase 3 Developer Experience (Increment 3: Wiki and README)

- **wiki/Enterprise-Usage.md** — New: deployment checklist (HTTPS, auth, CORS, rate limiting, correlation IDs, health), recommended production options, tool inspector in production, distributed tracing, see-also links.
- **wiki/Security-Model.md** — New: overview, auth flow (tools/list visibility, tools/call synthetic context, User propagation, ForwardHeaders), role/policy visibility, securing MCP and inspector, attack surfaces table.
- **wiki/Migration-Guide.md** — New: Phase 1→2 (McpToolHandler rename, attribute/options, new optional behavior), Phase 2→3 (inspector, examples), general upgrade checklist, link to VERSIONING.md.
- **wiki/Home.md** — Added links to Enterprise Usage, Security Model, Migration Guide, Performance; noted GET /mcp/tools in “How it works.”
- **wiki/Quick-Start.md** — Documented GET /mcp, GET /mcp/tools, POST /mcp; added Examples section (Minimal, WithAuth, WithEnrichment, Enterprise); next steps link to Enterprise Usage.
- **wiki/Configuration.md** — Added **EnableToolInspector** to options snippet and options table.
- **wiki/Governance-and-Security.md** — Cross-link to Security-Model.md.
- **wiki/Limitations.md** — Added “Tool inspector” note (no per-request visibility; disable or protect in production).
- **README.md** (root) — Tool Inspector section, Examples section (table), EnableToolInspector in config snippet, examples/ in project structure, wiki links (Enterprise Usage, Security Model, Migration Guide).
- **ZeroMCP/README.md** — Mention of GET /mcp/tools, EnableToolInspector in config table, wiki link.

---

## 2026-03-03 – Phase 3 Developer Experience (Increment 4: Benchmarks and Performance wiki)

- **ZeroMCP.Benchmarks** — New BenchmarkDotNet console project (net10.0), referencing ZeroMcp and ZeroMCP.Sample. **McpEndpointBenchmarks**: GET /mcp/tools, POST tools/list, POST tools/call list_orders, POST tools/call get_order (WebApplicationFactory&lt;OrdersController&gt;, MemoryDiagnoser). **Program.cs** runs BenchmarkSwitcher. **README.md** with run instructions and filter example.
- **ZeroMcp.slnx** — Added ZeroMCP.Benchmarks project.
- **wiki/Performance.md** — New: how to run benchmarks, what is benchmarked, baseline/reference numbers (orders of magnitude), reproducibility notes.
- **wiki/Home.md** — Added Performance to wiki pages table.

---

## 2026-03-03 – Additional tests for Phase 2 and Phase 3 features

- **Phase 3 Inspector**
  - **Phase3_Inspector_ToolCount_MatchesToolsArrayLength** — Asserts `toolCount` equals `tools` array length.
  - **Phase3_Inspector_WhenToolHasTags_IncludesTagsInResponse** — Asserts `health_check` in inspector response has `tags` including "system".
  - **Phase3_Inspector_WhenToolHasRequiredRoles_IncludesRequiredRoles** — Asserts `admin_health` in inspector has `requiredRoles` including "Admin".
  - **Phase3_Inspector_PostToTools_Returns405MethodNotAllowed** — Asserts POST /mcp/tools returns 405 (inspector is GET only).
- **Phase 2 Enrichment**
  - **EnrichmentEnabledWebApplicationFactory** — IPostConfigureOptions sets `EnableResultEnrichment = true`.
  - **McpEnrichmentEnabledTests.Phase2_WhenEnrichmentEnabled_ResultIncludesMetadata** — tools/call result has `metadata` with `statusCode` and `durationMs`.
- **Phase 2 Streaming**
  - **StreamingEnabledWebApplicationFactory** — IPostConfigureOptions sets `EnableStreamingToolResults = true`, `StreamingChunkSize = 64`.
  - **McpStreamingEnabledTests.Phase2_WhenStreamingEnabled_ContentHasChunkIndexAndIsFinal** — tools/call content is array of chunks with `chunkIndex`, `isFinal`, `text`; last chunk has `isFinal` true.

---

## 2026-03-03 – MCP Tool Inspector UI (Swagger-like test invocation)

- **ZeroMcpOptions** — Added **EnableToolInspectorUI** (default `true`). When true and **EnableToolInspector** is true, GET {RoutePrefix}/ui is registered.
- **ZeroMCP/Ui/McpInspectorUiHtml.cs** — New: embedded HTML page with inline CSS and JS. Fetches GET .../tools, renders collapsible list of tools (name, description, method, route), input schema (pre), and "Try it out" with textarea for JSON arguments and Invoke button; POSTs to MCP base with tools/call and displays JSON result. Base path injected via **GetHtml(mcpBasePath)**.
- **EndpointRouteBuilderExtensions** — When **EnableToolInspector** and **EnableToolInspectorUI**, **MapGet(baseRoute + "/ui", ...)** returns the HTML with `text/html; charset=utf-8`.
- **McpEndpointIntegrationTests** — **Phase3_InspectorUI_Get_ReturnsHtmlWithTitle** (GET /mcp/ui returns 200, text/html, body contains "ZeroMCP Tool Inspector" and "/mcp").
- **Docs** — Configuration.md, Quick-Start.md, README.md: document **EnableToolInspectorUI** and GET /mcp/ui.

## 2026-03-18 — McpResource / McpTemplate / McpPrompt sample + tests

### New: ZeroMCP.Sample/Controllers/CatalogController.cs
Demonstrates all three new MCP attributes in a product-catalog context.

**Static resources via [McpResource]**
- `GetCatalogInfo()` mapped to `catalog://info` — returns CatalogInfo (name, version, productCount, categoryCount, categories)
- `GetCategories()` mapped to `catalog://categories` — returns the distinct category array

**Parameterised resource templates via [McpTemplate]**
- `GetProductById(int id)` mapped to `catalog://products/{id}`
- `GetProductsByCategory(string category)` mapped to `catalog://categories/{category}/products`

**Prompt templates via [McpPrompt]**
- `SearchProductsPrompt([Required] string keyword, string? category)` → `search_products_prompt`
- `RestockRecommendationPrompt(int productId)` → `restock_recommendation_prompt`

### Model/data changes
- `Product.cs`: added `Category` and `Description` properties; added `CatalogInfo` class
- `SampleData.cs`: Products expanded to 5 items with categories; added `Categories` property and `BuildCatalogInfo()`; Orders expanded to 3

### New: ZeroMCP.Tests/McpResourcesAndPromptsIntegrationTests.cs
25 tests covering the full lifecycle: initialize capability advertisement, resources/list, resources/templates/list, resources/read (static + template + error cases), prompts/list (argument schema), prompts/get (message envelope, optional/required args, error cases).

### Bug fix: McpPromptDiscoveryService
Prompt argument `Required` flag was using `ApiParameterDescription.IsRequired` only. Changed to `param.IsRequired || (param.ModelMetadata?.IsRequired == true)` so `[Required]` DataAnnotations attributes on query parameters are correctly surfaced.

**Build/test result:** 0 errors, 25/25 new tests passing.

## 2026-03-18 — Wiki: Resources-and-Prompts page + minimal API clarification

### New wiki page
- `wiki/Resources-and-Prompts.md` and `wiki-repo/Resources-and-Prompts.md` — full documentation for `[McpResource]`, `[McpTemplate]`, and `[McpPrompt]` including attribute reference tables, JSON-RPC method mapping, URI variable extraction, argument discovery, error handling, and links to the sample controller and tests.

### Updated
- `wiki/Home.md` — added Resources and Prompts row to the page index table.
- `wiki-repo/Home.md` — same row added to the repo-mirror index.

### Minimal API support
`[McpResource]`, `[McpTemplate]`, and `[McpPrompt]` are **controller-action only**. The discovery services filter on `ControllerActionDescriptor`; minimal API endpoints are not discovered. There is no `.AsResource()` / `.AsTemplate()` / `.AsPrompt()` extension. This limitation is documented in the new wiki page.

## 2026-03-18 — Minimal API support for resources/templates/prompts

Added `.AsResource()`, `.AsTemplate()`, and `.AsPrompt()` extension methods so minimal API endpoints can be exposed as MCP resources and prompts, alongside the existing controller-attribute approach.

### New metadata classes
- `ZeroMCP/Metadata/McpResourceEndpointMetadata.cs` — attaches static resource metadata to a minimal API endpoint
- `ZeroMCP/Metadata/McpTemplateEndpointMetadata.cs` — attaches resource-template metadata
- `ZeroMCP/Metadata/McpPromptEndpointMetadata.cs` — attaches prompt metadata

### Updated extension methods (McpToolEndpointExtensions.cs)
Added `AsResource<TBuilder>()`, `AsTemplate<TBuilder>()`, and `AsPrompt<TBuilder>()`.

### Updated discovery services
- `McpResourceDiscoveryService.BuildRegistry()` — scans `EndpointDataSource` for `McpResourceEndpointMetadata` / `McpTemplateEndpointMetadata` in addition to controller attributes.
- `McpPromptDiscoveryService.BuildRegistry()` — scans `EndpointDataSource` for `McpPromptEndpointMetadata`; uses `RoutePattern.Parameters` to surface route-parameter arguments without needing API description lookup.

### Dispatch fix (SyntheticHttpContextFactory.cs)
For minimal API endpoints with no body (GET), any argument that is not already bound to a route value and not in the known `QueryParameters` list now falls through to the query string automatically. This mirrors the `.AsMcp()` tool dispatch and means optional query parameters (e.g. `urgency`) reach the endpoint even when the API description lookup fails.

### Sample (Program.cs)
Three new minimal API endpoints:
- `GET /api/system/status` → `system://status` (static resource via `.AsResource()`)
- `GET /api/orders/resource/{id:int}` → `orders://order/{id}` (template resource via `.AsTemplate()`)
- `GET /api/prompts/fulfil/{orderId:int}` → `fulfil_order_prompt` (prompt via `.AsPrompt()`); `orderId` is a route parameter so it is auto-discovered; optional `urgency` query parameter reaches the endpoint via the fallback dispatch path.

### Tests (McpResourcesAndPromptsIntegrationTests.cs)
Expanded from 25 to 34 tests; 9 new tests cover minimal API resources and prompts.

**Build/test result:** 0 errors, 106/106 tests passing.

## 2026-03-18 — Client compatibility fixes (Codex / Copilot / Claude HTTP)

Analysis of client findings revealed three gaps. All three are now fixed and covered by new tests.

### Fix 1: GET /mcp SSE stream (Codex, Claude HTTP)

`McpHttpEndpointHandler.HandleAsync()` — GET handler now checks the `Accept` header before returning the JSON description. When `Accept: text/event-stream` is present the server returns `200 OK`, `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive` and enters a keep-alive loop sending SSE comment lines (`: keep-alive\n\n`) every 15 seconds until the client disconnects. A plain GET without that header continues to return the human-readable JSON description unchanged.

### Fix 2: notifications/initialized → 202 Accepted (Codex)

The null-payload branch in `HandleAsync()` now returns `202 Accepted` specifically for `notifications/initialized`. All other fire-and-forget notifications (e.g. `notifications/cancelled`) continue to return `204 No Content`.

### Fix 3: Empty lists for disabled features (Copilot)

`resources/list`, `resources/templates/list`, and `prompts/list` now return empty lists (`{ resources: [] }` / `{ resourceTemplates: [] }` / `{ prompts: [] }`) when `EnableResources=false` or `EnablePrompts=false` instead of throwing `-32601 Method Not Found`. Copilot calls these methods unconditionally and marks the server unavailable on any error response. The `resources/read`, `prompts/get` methods still return `-32601` when disabled as those require a URI/name argument and cannot silently no-op. Both dispatch paths (`HandleAsync` and `ProcessMessageAsync`) were updated.

### New test file: ZeroMCP.Tests/McpClientCompatibilityTests.cs

17 new tests across two test classes:

- `McpClientCompatibilityTests` (standard factory, features enabled):
  - 4 × GET SSE: status 200, content-type, stream open, Cache-Control no-cache
  - 2 × plain GET: no Accept header → JSON, `Accept: application/json` → JSON
  - 2 × notifications/initialized: 202 status, empty body
  - 1 × notifications/cancelled: 204 (regression guard)
  - 2 × enabled baseline: resources/templates/list and prompts/list carry correct keys

- `McpClientCompatibilityDisabledFeaturesTests` (`FeaturesDisabledFactory`, `EnableResources=false`, `EnablePrompts=false`):
  - 3 × empty-list returns for resources/list, resources/templates/list, prompts/list
  - 2 × no -32601 guard for templates and prompts
  - 1 × capabilities check: resources and prompts not advertised when disabled

**Build/test result:** 0 errors, 123/123 tests passing.

## 2026-03-18 — listChanged notification support

Implemented the `listChanged` capability for the MCP protocol, enabling servers to notify connected SSE clients when tool, resource, or prompt registries change at runtime.

### New: McpNotificationService (ZeroMCP/Notifications/)
Singleton service that tracks active SSE sessions via `ConcurrentDictionary<string, ChannelWriter<string>>`. Provides:
- `RegisterSession(ChannelWriter<string>)` / `UnregisterSession(string)` for session lifecycle
- `NotifyToolsListChangedAsync()` / `NotifyResourcesListChangedAsync()` / `NotifyPromptsListChangedAsync()` to broadcast JSON-RPC notifications to all connected clients
- `ActiveSessionCount` property for monitoring
- Dead session cleanup during broadcast

### SSE handler refactored (McpHttpEndpointHandler.cs)
Replaced the `Task.Delay`-based keep-alive loop with a `Channel<string>`-based loop. When `McpNotificationService` is present, the handler registers a session before flushing headers. The loop uses `WaitToReadAsync` with a 15-second timeout — if a notification arrives, it's written as an SSE `data:` event; if no message arrives within the timeout, a keep-alive comment is sent. Diagnostic header `X-ZeroMCP-Notifications` is set on SSE responses.

### Discovery cache invalidation
Added `InvalidateCache()` to `McpToolDiscoveryService`, `McpResourceDiscoveryService`, and `McpPromptDiscoveryService`. Clears the write-once cache under the existing lock so the next access triggers a full rebuild. Designed to pair with the notification methods:
```csharp
_toolDiscovery.InvalidateCache();
await _notificationService.NotifyToolsListChangedAsync();
```

### Capability advertisement (HandleInitialize)
`listChanged` is now conditionally set to `true` in the `initialize` response when `McpNotificationService` is injected (i.e. when `EnableListChangedNotifications` is on). Previously hardcoded to `false`.

### New option: EnableListChangedNotifications (ZeroMcpOptions.cs)
Opt-in (default `false`). When true, `McpNotificationService` is wired into the SSE handler and capabilities advertise `listChanged: true`.

### DI and wiring
- `ServiceCollectionExtensions.cs` — registers `McpNotificationService` as singleton
- `EndpointRouteBuilderExtensions.cs` — resolves `McpNotificationService` (conditional on option) and passes to all `McpHttpEndpointHandler` constructors

### Tests: McpListChangedNotificationTests.cs (15 new tests)
- 3 × capabilities: `listChanged: true` for tools, resources, prompts when enabled
- 1 × capabilities: `listChanged: false` when disabled (default)
- 2 × SSE handler: session registration header (enabled vs disabled)
- 4 × direct channel broadcast: tools/resources/prompts/multi-session delivery
- 1 × unregister cleanup
- 1 × option verification
- 3 × discovery cache invalidation: tools, resources, prompts

Note: End-to-end SSE stream reading is not feasible in ASP.NET Core TestServer (body is buffered until handler returns). SSE delivery is verified via the direct channel tests and session registration headers.

**Build/test result:** 0 errors, 141/141 tests passing.

---

## 2026-03-18 — Codex handshake fix: response ID type fidelity

### Bug
Codex reported `MCP startup failed: handshaking with MCP server failed: conflict initialized — response id: expected 0, got 0`.

### Root cause
In all three `idValue` computation sites in `McpHttpEndpointHandler`, the response ID was extracted with `id.GetRawText()`. `GetRawText()` returns a **C# string** (`"0"`) regardless of whether the JSON source was a number or a string. When `JsonSerializer` serializes that C# string into the response, it writes it as a JSON string (`"0"` with quotes) even when the client sent a JSON number (`0`). Codex performs strict type-matching on the echoed `id` field and rejects the response.

### Fix
Replaced all three occurrences of `id.GetRawText()` with `id.Clone()`. `JsonElement.Clone()` produces a self-contained copy that `JsonSerializer` serializes with the original JSON type — number stays number, string stays string, null stays null.

Changed in `HandleAsync` (line ~136), `ProcessStreamingMessageAsync` (line ~268), and `ProcessMessageAsync` (line ~353).

### New tests (McpClientCompatibilityTests.cs)
- `ResponseId_IntegerZero_EchoedAsIntegerNotString` — `id:0` request → `id:0` (JsonValueKind.Number) in response
- `ResponseId_PositiveInteger_EchoedAsInteger` — `id:42` → `id:42`
- `ResponseId_StringId_EchoedAsString` — `id:"req-1"` → `id:"req-1"` (JsonValueKind.String)

**Build/test result:** 0 errors, 126/126 tests passing.

---

## 2026-03-18 — Enabled listChanged in Sample app

Enabled `EnableListChangedNotifications = true` in `ZeroMCP.Sample\Program.cs` so the Sample app advertises `listChanged: true` for tools, resources, and prompts in its `initialize` response.

Updated `McpListChangedNotificationTests.cs` to add a dedicated `ListChangedDisabledFactory` (with `PostConfigure` setting `EnableListChangedNotifications = false`) for the two "disabled" tests, so they no longer rely on the Sample app's default configuration.

**Build/test result:** 0 errors, 141/141 tests passing.

---

## 2026-03-18 — Resource subscription support (`resources/subscribe`)

Implemented the MCP `resources/subscribe` and `resources/unsubscribe` methods, enabling clients to register interest in specific resource URIs and receive targeted `notifications/resources/updated` notifications when resource content changes. The framework is trigger-agnostic — the developer calls `NotifyResourceUpdatedAsync(uri)` from anywhere.

### New option: EnableResourceSubscriptions (ZeroMcpOptions.cs)
Opt-in (default `false`). When true and `EnableResources` is true, advertises `subscribe: true` in the `resources` capability and accepts `resources/subscribe`/`resources/unsubscribe` JSON-RPC methods.

### McpNotificationService extended (Notifications/)
- New `ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>` for URI → subscribed session IDs
- `SubscribeSession(sessionId, uri)` / `UnsubscribeSession(sessionId, uri)` for per-URI subscription management
- `NotifyResourceUpdatedAsync(uri)` — sends `notifications/resources/updated` only to sessions subscribed to that URI; cleans up dead sessions
- `GetSubscriberCount(uri)` for monitoring
- `UnregisterSession` now calls `UnsubscribeAll(sessionId)` to clean up on disconnect
- Renamed internal `BroadcastAsync` → `BroadcastAllAsync` for clarity

### Transport: Mcp-Session-Id header
- SSE GET response now includes `Mcp-Session-Id` header (GUID, 32 chars) so clients can associate POST requests with their SSE session
- `resources/subscribe` and `resources/unsubscribe` resolve the session via `Mcp-Session-Id` request header
- Missing header returns `-32602` (Invalid Params) error

### Handler changes (McpHttpEndpointHandler.cs)
- Added `resources/subscribe` and `resources/unsubscribe` to both `HandleAsync` and `ProcessMessageAsync` method switches
- New `HandleResourceSubscribe`, `HandleResourceUnsubscribe`, `ResolveSessionId` private methods
- `HandleInitialize` now conditionally advertises `subscribe: true` based on `EnableResourceSubscriptions` option

### Capability advertisement
```json
"resources": { "listChanged": true, "subscribe": true }
```

### Sample app
- Enabled `EnableResourceSubscriptions = true` in `Program.cs`
- Added demo endpoint `POST /api/orders/resource/{id}/notify-change` that calls `NotifyResourceUpdatedAsync("orders://order/{id}")`

### Tests: McpResourceSubscriptionTests.cs (11 new tests)
- 2 × capabilities: `subscribe: true` when enabled, `false` when disabled
- 2 × subscribe/unsubscribe: valid URI returns empty result
- 1 × subscribe without Mcp-Session-Id returns error (-32602)
- 1 × subscribe when feature disabled returns Method Not Found (-32601)
- 3 × direct channel: targeted delivery, no broadcast after unsubscribe, multi-URI isolation
- 1 × unregister session cleans up subscriptions
- 1 × SSE response includes Mcp-Session-Id header

**Build/test result:** 0 errors, 152/152 tests passing.
