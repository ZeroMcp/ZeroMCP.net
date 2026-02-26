# Progress

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
  - **Project references:** Updated from `..\MCPSwagger\` and `..\MCPSwagger.Sample\` to `..\ZeroMcp\ZeroMCP.csproj` and `..\ZeroMCP.Sample\ZeroMCP.Sample.csproj` so build and tests run after rename.
  - **ToolsList_ReturnsTaggedToolsOnly:** Now asserts presence of list_customers, get_customer, get_customer_orders, create_customer, list_products, get_product, create_product.
  - **GetCustomerOrders_ToolsCall_ReturnsOrdersForCustomer:** New integration test; calls `get_customer_orders` with `id: 1` and asserts result content is an array with at least one order (id=1, customerName=Alice, product=Widget).
- **README.md:** Project structure and build commands updated to ZeroMcp / ZeroMCP.Sample / ZeroMCP.Tests; sample line notes Customer/Product and nested route `Customer/{id}/orders`.

---

## 2026-02-24 – Two READMEs (GitLab vs NuGet)

- **Repository (GitLab):** Root **README.md** — full documentation, build, tests, contributing, project structure. Intro now states it is the repo README and that the NuGet package has its own README.
- **NuGet package:** **MCPSwagger/README.md** — consumer-focused: install, quick start, configuration summary table, governance/metrics one-liners, link to full docs. Packed into the NuGet via `None Include="README.md"` in MCPSwagger.csproj (replaced `..\README.md`).
- **MCPSwagger.csproj:** Pack `README.md` from project directory instead of repo root.
- **README.md:** Added "Two READMEs" section describing both files and that feature changes should update both.

---

## 2026-02-24 – Phase 1 Observability

### Completed

1. **Structured logging**
   - **McpHttpEndpointHandler**: Log scope for each request with `CorrelationId`, `JsonRpcId`, `Method`. Logs completion with `Method`, `DurationMs`; warnings for method not found / invalid params; error for unhandled exception.
   - **McpSwaggerToolHandler**: Per tool invocation logs `ToolName`, `StatusCode`, `IsError`, `DurationMs`, `CorrelationId` (Debug on success, Warning on error). Unknown tool logs Warning with ToolName and CorrelationId.

2. **Execution timing**
   - **McpHttpEndpointHandler**: `Stopwatch` around the method switch; logs `DurationMs` on completion and on error paths.
   - **McpSwaggerToolHandler**: `Stopwatch` around `DispatchAsync`; duration passed to metrics sink and logs.

3. **Success/failure tracking**
   - Tool invocations record `StatusCode`, `IsError` (!result.IsSuccess) and feed them into logs and **IMcpMetricsSink**.

4. **Correlation ID propagation**
   - **CorrelationIdHeader** (default `X-Correlation-ID`): read from request or generate new GUID; set on `context.Items[McpCorrelationId]`; echoed in response via `OnStarting`.
   - Log scope and tool logs include CorrelationId.
   - **SyntheticHttpContextFactory**: copies correlation ID from source context into synthetic `TraceIdentifier` and `Items` so dispatched actions see the same ID.

5. **IMcpMetricsSink**
   - **Observability/IMcpMetricsSink.cs**: `RecordToolInvocation(toolName, statusCode, isError, durationMs, correlationId)`.
   - **NoOpMcpMetricsSink**: default registration; app can register its own after `AddSwaggerMcp()` to push to Prometheus/AppInsights/etc.

6. **Optional OpenTelemetry**
   - **EnableOpenTelemetryEnrichment** in `SwaggerMcpOptions` (default false). When true, `McpSwaggerToolHandler` tags `Activity.Current` with `mcp.tool`, `mcp.status_code`, `mcp.is_error`, `mcp.duration_ms`, `mcp.correlation_id`.

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
   - `McpSwaggerToolHandler`: injects `IOptions<SwaggerMcpOptions>`, adds `GetToolDefinitionsAsync(context, cancellationToken)` and private `IsVisibleAsync(descriptor, context)`.
   - `McpHttpEndpointHandler.HandleToolsList` → **HandleToolsListAsync(context)** calling `GetToolDefinitionsAsync(context)`.
   - README: new subsection "Governance & tool control", updated Configuration and `[McpTool]` examples.

6. **Tests (Governance)**
   - **MCPSwagger.Sample**: `ApiKeyAuthenticationHandler` accepts "admin-key" and adds `ClaimTypes.Role` "Admin"; added minimal endpoint `admin_health` with `.WithMcpTool("admin_health", ..., roles: new[] { "Admin" })`.
   - **MCPSwagger.Tests**: `PostMcpAsync(body, headers)` overload to send request headers (e.g. `X-Api-Key`).
   - **Governance_ToolsList_WithoutAuth_ExcludesRoleRequiredTool**: tools/list without auth → `admin_health` not in list.
   - **Governance_ToolsList_WithAdminKey_IncludesRoleRequiredTool**: tools/list with `X-Api-Key: admin-key` → `admin_health` in list.
   - **ToolsList_ReturnsTaggedToolsOnly**: now asserts `admin_health` is not in list (no auth) and `health_check` is in list.

---

## 2026-02-24 – Phase 1 Production Hardening

### Completed

1. **Lock MCP protocol version**
   - Added `MCPSwagger/McpProtocolConstants.cs` with `McpProtocolConstants.ProtocolVersion = "2024-11-05"`.
   - `McpHttpEndpointHandler` now uses this constant for GET example and `initialize` response (single source of truth).

2. **Semantic versioning**
   - Package already uses SemVer (e.g. 1.0.2). Documented in VERSIONING.md.

3. **Compatibility tests**
   - In `MCPSwagger.Tests/McpEndpointIntegrationTests.cs`:
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
   - Included minimal Program.cs snippet: AddControllers, AddEndpointsApiExplorer, MapControllers, minimal APIs, MapSwaggerMcp.

### Fix for your app (e.g. testingAPI)

In `Program.cs` ensure:

```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();   // required so controller [McpTool] actions are discovered
// ... AddSwaggerMcp(...) ...

app.MapControllers();
// then minimal APIs with .WithMcpTool(...)
app.MapSwaggerMcp();
```

After adding `AddEndpointsApiExplorer()`, restart the app; both controller and minimal tools should appear in `tools/list`.

---

## 2026-02-24 – Build errors resolved

### Changes made

1. **MCPSwagger.csproj**
   - Removed invalid `Microsoft.AspNetCore` (Version 2.3.9) package reference; it was unnecessary for net10.0 and caused NU1510.
   - Added **NJsonSchema** (11.0.0) for `McpSchemaBuilder` (JSON Schema generation, `SystemTextJsonSchemaGeneratorSettings`, `JsonObjectType`).
   - Added **Swashbuckle.AspNetCore** (7.2.0) for `Program.cs` (AddSwaggerGen, UseSwagger, UseSwaggerUI).

2. **McpSwaggerToolHandler.cs**
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
- Application (TestService) builds and is ready to run; ensure `Program.cs` registers controllers and SwaggerMcp if you use the Orders API and MCP endpoint.

### Later fix: FluentAssertions JSON API

- **McpEndpointIntegrationTests.cs:** Replaced `ContainKey("result")` / `ContainKey("error")` with **`HaveProperty("result")`** / **`HaveProperty("error")`**. `JsonNodeAssertions<JsonObject>` uses `HaveProperty` / `NotHaveProperty`, not `ContainKey`.

### GET /mcp returning something

- **McpHttpEndpointHandler:** For **GET** requests to `/mcp`, now return a JSON description (protocol, server name/version, example initialize payload). **POST** unchanged (JSON-RPC 2.0).
- **EndpointRouteBuilderExtensions:** Route registered with **MapMethods(route, ["GET", "POST"], ...)** so both GET and POST are handled at `/mcp`.

### NuGet package layout

- **MCPSwagger** is now a library-only project that packs as a NuGet package.
  - **MCPSwagger.csproj:** OutputType=Library, only NJsonSchema dependency; PackageId=SwaggerMcp, Version=1.0.0; Compile Remove for Program.cs, OrdersController.cs, *Tests.cs (moved to other projects).
  - **MCPSwagger.Sample:** Standalone sample app (Program.cs, Controllers/OrdersController.cs), references MCPSwagger + Swashbuckle.
  - **MCPSwagger.Tests:** Unit and integration tests; references MCPSwagger and MCPSwagger.Sample (WebApplicationFactory&lt;Program&gt;).
- **Pack:** `dotnet pack MCPSwagger\MCPSwagger.csproj -c Release -o .\nupkgs` produces **SwaggerMcp.1.0.0.nupkg**.
- **TestService** still references MCPSwagger via ProjectReference (unchanged).

### Phase 1 + Phase 2 (2026-02-24)

**Phase 1:** Auth token forwarding via `SwaggerMcpOptions.ForwardHeaders` and `sourceContext` through factory/dispatcher/handler. XML doc descriptions via `XmlDocHelper.GetMethodSummary` when `[McpTool].Description` is null.

**Phase 2:** Minimal API support: `McpToolDescriptor.Endpoint`, `McpToolEndpointMetadata`, `WithMcpTool` extension; discovery from `EndpointDataSource.Endpoints`; dispatch branch for minimal endpoints (`DispatchMinimalEndpointAsync`). Discovery uses `EndpointDataSource` (not IEndpointDataSource).

**Sample:** MCPSwagger.Sample Program.cs now includes a minimal API example: `GET /api/health` with `.WithMcpTool("health_check", "Returns API health status.", tags: new[] { "system" })`.

**create_order 500 fix:** Controller actions now get their matching RouteEndpoint from EndpointDataSource (by ControllerActionDescriptor.Id) and the dispatcher sets context.SetEndpoint before invoking so CreatedAtAction/LinkGenerator no longer hit IRouter/ActionContext 500.

**CreatedAtAction robustness:** FindEndpointForAction now falls back to matching by ControllerName+ActionName when Id does not match. Synthetic request sets PathBase = Empty and Path with trimmed RelativeUrl. Log a warning when no endpoint is found for a controller action so link generation failures can be diagnosed. **"No route matches the supplied values" fix:** Synthetic request route values now include ambient `controller` and `action` from the ActionDescriptor so LinkGenerator/CreatedAtAction can resolve the target action (e.g. GetOrder) when generating the Location URL.

## 2026-02-24 – Expanded MCP validation/schema/auth test coverage

### Changes made

1. **MCPSwagger.Sample/Program.cs**
   - Added authentication/authorization services:
     - `AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)`
     - `AddAuthorization()`
   - Added `app.UseAuthentication()` before `app.UseAuthorization()`.

2. **MCPSwagger.Sample/ApiKeyAuthenticationHandler.cs** (new)
   - Added a lightweight API-key auth handler (`X-Api-Key: dev-key`) for sample authorization scenarios.
   - Missing/invalid key yields unauthenticated requests, enabling deterministic unauthorized behavior in tests.

3. **MCPSwagger.Sample/Controllers/OrdersController.cs**
   - Added protected MCP tool:
     - `get_secure_order` (`[Authorize]`) for auth failure-path verification.
   - Added status value validation on `UpdateStatusRequest.Status`:
     - `[RegularExpression("^(pending|shipped|cancelled)$", ...)]`
   - This enables a concrete invalid-status model-validation test case.

4. **MCPSwagger.Tests/McpEndpointIntegrationTests.cs**
   - Added tool-list schema shape assertions (not just tool-name presence).
   - Added MCP transport and validation/error tests for:
     - malformed JSON body parse errors (`-32700`)
     - create-order model-state failure (missing required fields)
     - wrong argument type (`id` as string instead of int)
     - empty `{}` arguments for a required-params tool
     - valid route + invalid body value (`update_order_status`)
     - unauthorized protected endpoint call returning MCP error content (HTTP 401 wrapped in MCP result)
   - Updated tool list assertions to include `get_secure_order`.

5. **MCPSwagger.Tests/McpSchemaBuilderTests.cs**
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
  - `dotnet build MCPSwagger.Tests/MCPSwagger.Tests.csproj -v detailed`
  - `dotnet` location checks (`command -v dotnet`, `whereis dotnet`, `/usr/share/dotnet`)
- Result:
  - Build/test execution is currently blocked on this runner because the .NET SDK is not installed (`dotnet: command not found`).

## Failing tests fixed (ToolsList_ReturnsExpectedInputSchemaShapes)

- **Cause:** Integration test expected `update_order_status` input schema to have `status.pattern` (from `[RegularExpression]` on `UpdateStatusRequest.Status`). NJsonSchema does not populate `Pattern` from `[RegularExpression]` by default, so the emitted schema had no `pattern`.
- **Fix:** In **McpSchemaBuilder.ExtractBodyProperties**, after building each body property from NJsonSchema, call new **GetRegularExpressionPattern(bodyType, propName)** to get `[RegularExpression].Pattern` via reflection and set `propObj["pattern"]` when present. Added `using System.ComponentModel.DataAnnotations` and `System.Reflection`; null/empty propertyName and PascalCase fallback for property lookup.
- **Test:** **McpEndpointIntegrationTests** line 247: **TestContext.Current.TestOutputHelper** dereference warning fixed with `TestContext.Current?.TestOutputHelper?.WriteLine(...)`.

## README.md updated for current project state

- How It Works: discovery from controllers + minimal APIs; GET and POST /mcp; dispatch to action or endpoint.
- Quick Start: package version 1.0.2; MapSwaggerMcp registers GET and POST.
- In-Process Dispatch: synthetic context has ambient controller/action and endpoint; CreatedAtAction supported.
- Minimal API section moved before Connecting MCP Clients; single consolidated section.
- Project Structure: reflects Metadata/, Options/, controller + minimal discovery, sample with health + optional auth.
- Known Limitations: streamlined; CreatedAtAction as fallback note only.
- Build: targets net9.0 and net10.0; simplified test coverage paragraph.
- NuGet: version 1.0.2; NJsonSchema-only dependency note.
