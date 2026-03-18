# Resources and Prompts

Beyond **tools**, the MCP specification defines two additional capability types that let AI clients interact with your API in richer ways:

| Type | MCP concept | Controller attribute | Minimal API extension |
|------|-------------|---------------------|-----------------------|
| Static content | Resource | `[McpResource]` | `.AsResource(uri, name, ...)` |
| Parameterised content | Resource template | `[McpTemplate]` | `.AsTemplate(uriTemplate, name, ...)` |
| Prompt template | Prompt | `[McpPrompt]` | `.AsPrompt(name, ...)` |

Both approaches are fully supported. Controllers use attributes; minimal API endpoints use the fluent builder extensions in the same chain as `.AsMcp()`.

---

## Enabling

Resources and prompts are **on by default** when you register ZeroMCP. You can disable them individually:

```csharp
builder.Services.AddZeroMCP(options =>
{
    options.EnableResources = false;  // disable resources/list, resources/templates/list, resources/read
    options.EnablePrompts   = false;  // disable prompts/list, prompts/get
});
```

When enabled, the `initialize` response advertises the capabilities automatically:

```json
{
  "capabilities": {
    "tools":     {},
    "resources": {},
    "prompts":   {}
  }
}
```

---

## [McpResource] — Static resources

A **static resource** is a well-known, fixed URI. MCP clients discover it via `resources/list` and fetch it with `resources/read` using the exact URI.

```csharp
[HttpGet("catalog/info")]
[McpResource("catalog://info", "catalog_info",
    Description = "High-level metadata about the product catalog.",
    MimeType    = "application/json")]
public ActionResult<CatalogInfo> GetCatalogInfo()
    => Ok(SampleData.BuildCatalogInfo());
```

| Parameter | Description |
|-----------|-------------|
| **uri** (first arg) | Required. The fixed, well-known URI (e.g. `catalog://info`). Any scheme is valid. |
| **name** (second arg) | Required. Snake_case identifier shown to the AI client. |
| **Description** | Optional. Shown to the AI client in `resources/list`. |
| **MimeType** | Optional. MIME type of the response (e.g. `application/json`, `text/plain`). |

### JSON-RPC methods served

| Method | Behaviour |
|--------|-----------|
| `resources/list` | Returns all `[McpResource]` actions as `{ uri, name, description?, mimeType? }` objects. |
| `resources/read` | Dispatches the action matching the URI and returns `{ contents: [{ uri, text, mimeType? }] }`. |

---

## [McpTemplate] — Parameterised resource templates

A **resource template** uses an [RFC 6570 Level 1](https://tools.ietf.org/html/rfc6570) URI template. Variables in `{curly_braces}` are extracted from the read URI and bound to action parameters by name.

```csharp
[HttpGet("catalog/products/{id:int}")]
[McpTemplate("catalog://products/{id}", "catalog_product",
    Description = "Retrieves a single product by its numeric ID.",
    MimeType    = "application/json")]
public ActionResult<Product> GetProductById(int id)
{
    var product = SampleData.Products.FirstOrDefault(p => p.Id == id);
    return product is null ? NotFound($"Product {id} not found.") : Ok(product);
}

[HttpGet("catalog/categories/{category}/products")]
[McpTemplate("catalog://categories/{category}/products", "catalog_products_by_category",
    Description = "Lists all products in the specified category.",
    MimeType    = "application/json")]
public ActionResult<IEnumerable<Product>> GetProductsByCategory(string category)
    => Ok(SampleData.Products.Where(p =>
           p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)));
```

| Parameter | Description |
|-----------|-------------|
| **uriTemplate** (first arg) | Required. RFC 6570 level-1 URI template; variables must match action parameter names. |
| **name** (second arg) | Required. Snake_case identifier. |
| **Description** | Optional. |
| **MimeType** | Optional. |

### JSON-RPC methods served

| Method | Behaviour |
|--------|-----------|
| `resources/templates/list` | Returns all `[McpTemplate]` actions as `{ uriTemplate, name, description?, mimeType? }` objects. |
| `resources/read` | Matches the read URI against all registered templates, extracts variables, dispatches the action, and returns `{ contents: [{ uri, text, mimeType? }] }`. |

### URI variable extraction

Given the template `catalog://products/{id}` and a read request for `catalog://products/42`, ZeroMCP extracts `id = "42"` and passes it to the action as if it were a route parameter. Type coercion (e.g. string → int) is handled by the normal ASP.NET Core model-binder.

---

## [McpPrompt] — Prompt templates

A **prompt** action builds and returns a ready-to-use text prompt that the AI client can send directly to a model. The framework wraps the action's string response in the standard MCP message envelope.

```csharp
[HttpGet("catalog/prompts/search")]
[McpPrompt("search_products_prompt",
    Description = "Generates a search prompt for finding products by keyword and optional category.")]
public IActionResult SearchProductsPrompt(
    [FromQuery][Required] string keyword,
    [FromQuery] string? category = null)
{
    var categoryClause = string.IsNullOrWhiteSpace(category)
        ? string.Empty
        : $" within the '{category}' category";

    return Ok($"Search the product catalog for items matching '{keyword}'{categoryClause}. " +
              "For each result include the name, price, and a one-sentence description.");
}
```

| Parameter | Description |
|-----------|-------------|
| **name** (first arg) | Required. Snake_case identifier. |
| **Description** | Optional. Shown to the AI client in `prompts/list`. |

### JSON-RPC methods served

| Method | Behaviour |
|--------|-----------|
| `prompts/list` | Returns all `[McpPrompt]` actions as `{ name, description?, arguments?: [...] }`. Arguments are discovered from route/query parameters. |
| `prompts/get` | Dispatches the action with the supplied arguments and wraps the response as `{ messages: [{ role: "user", content: { type: "text", text: "..." } }], description? }`. |

### Argument discovery

ZeroMCP reads the action's route and query parameters to build the `arguments` array in `prompts/list`. Each argument reports:

- `name` — parameter name
- `required` — `true` if the parameter has `[Required]` (DataAnnotations) or `[BindRequired]`
- `description` — from `ModelMetadata` when present

```json
{
  "name": "search_products_prompt",
  "description": "Generates a search prompt for finding products...",
  "arguments": [
    { "name": "keyword",  "required": true  },
    { "name": "category", "required": false }
  ]
}
```

> Mark required arguments with `[Required]` from `System.ComponentModel.DataAnnotations`. Parameters with a default value (e.g. `string? category = null`) are automatically optional.

### Response shape

`prompts/get` always returns a single `user` message. The action should return a plain string (via `Ok("...")`) — the framework handles the wrapping:

```json
{
  "description": "Generates a search prompt...",
  "messages": [
    {
      "role": "user",
      "content": { "type": "text", "text": "Search the product catalog for..." }
    }
  ]
}
```

---

## Minimal API support

All three types can also be registered on minimal API endpoints using the builder extension methods. Chain them in the same place as `.AsMcp()`:

```csharp
// Static resource
app.MapGet("/api/system/status", () => Results.Ok(new { status = "ok", version = "1.0" }))
   .AsResource("system://status", "system_status",
       description: "Current system health and version.",
       mimeType: "application/json");

// Resource template — route variable {id} maps to the URI template variable {id}
app.MapGet("/api/orders/resource/{id:int}", (int id) => Results.Ok(new { orderId = id, customer = "Alice" }))
   .AsTemplate("orders://order/{id}", "order_resource",
       description: "Retrieves a single order by ID.",
       mimeType: "application/json");

// Prompt — route parameter orderId is auto-discovered; optional query params reach the
// endpoint via the fallback query-string dispatch path
app.MapGet("/api/prompts/fulfil/{orderId:int}", (int orderId, string? urgency) =>
{
    var urgencyClause = urgency is not null ? $" Urgency: {urgency}." : string.Empty;
    return Results.Ok($"Fulfil order {orderId} for Alice.{urgencyClause}");
}).AsPrompt("fulfil_order_prompt", "Generates a fulfilment prompt for an order.");
```

### Argument discovery for minimal API prompts

ZeroMCP extracts route parameters directly from the endpoint's `RoutePattern`. These appear as `required` arguments in `prompts/list` without any additional annotation. Optional query parameters (those not in the route pattern) reach the backing endpoint via a query-string fallback in the dispatcher — they do not appear in the `arguments` list unless the API description is available.

To ensure an argument appears in `prompts/list`, place it in the route template:

```csharp
// orderId appears in arguments; urgency is passed through but not advertised
app.MapGet("/api/prompts/fulfil/{orderId:int}", (int orderId, string? urgency) => ...)
   .AsPrompt("fulfil_order_prompt", ...);
```

---

## Error handling

All three dispatchers follow the same HTTP → MCP mapping as tools:

| HTTP response | `resources/read` / `prompts/get` |
|---------------|-----------------------------------|
| 2xx | Success — response body returned as `text` |
| 4xx / 5xx | Result still returned (not a JSON-RPC error) — the HTTP error text is wrapped in `text`, so the caller can inspect it |
| Unknown URI / name | JSON-RPC `-32602 InvalidParams` error |

---

## Routing and HTTP method

The attribute discovery uses **IApiDescriptionGroupCollectionProvider** (the same source as Swagger). You must place the standard ASP.NET Core HTTP verb attribute (`[HttpGet]`, `[HttpPost]`, etc.) on the action alongside the ZeroMCP attribute. Any HTTP method works.

Because dispatch is in-process, all filters, validation, and authorization apply normally — for example, an `[Authorize]` attribute on a resource action will prevent the content from being returned unless the caller's request is authenticated.

---

## Sample

A full working example is available in the sample app:

`ZeroMCP.Sample/Controllers/CatalogController.cs`

It demonstrates all four attribute types (`[McpResource]`, `[McpTemplate]`, `[McpPrompt]`, and the existing `[Mcp]`) in a product-catalog controller.

Integration tests for all six JSON-RPC methods are in:

`ZeroMCP.Tests/McpResourcesAndPromptsIntegrationTests.cs`

---

## See also

- [The \[Mcp\] Attribute](The-Mcp-Attribute) — Exposing actions as MCP tools
- [Controllers and Minimal APIs](Controllers-and-Minimal-APIs) — Controller vs minimal API support
- [Parameters and Schemas](Parameters-and-Schemas) — How parameters become JSON schema
- [Configuration](Configuration) — `EnableResources` / `EnablePrompts` options
