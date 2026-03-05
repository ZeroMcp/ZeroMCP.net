# Parameters and Schemas

ZeroMCP merges all parameter sources into a single flat JSON Schema object that the LLM fills in when calling a tool.

---

## Mapping

| Parameter source | MCP mapping |
|------------------|-------------|
| **Route params** (`{id}`) | Always required properties |
| **Query params** (`?status=`) | Optional (or required if `[Required]`) |
| **`[FromBody]` object** | Properties expanded inline from JSON Schema |

---

## Controller example

```csharp
[HttpPatch("{id}/status")]
[Mcp("update_order_status", Description = "Updates an order's status.")]
public IActionResult UpdateStatus(int id, [FromBody] UpdateStatusRequest req) { ... }

public class UpdateStatusRequest
{
    [Required] public string Status { get; set; }
    public string? Reason { get; set; }
}
```

Produces this MCP input schema:

```json
{
  "type": "object",
  "properties": {
    "id":     { "type": "integer" },
    "status": { "type": "string" },
    "reason": { "type": "string" }
  },
  "required": ["id", "status"]
}
```

---

## Minimal API examples

### Query parameters

```csharp
app.MapGet("/api/orders", (string? status, int page = 1, int pageSize = 20) => ...)
   .AsMcp("list_orders", "Lists orders with optional filtering.");
```

Produces:

```json
{
  "type": "object",
  "properties": {
    "status":   { "type": "string" },
    "page":     { "type": "integer", "default": 1 },
    "pageSize": { "type": "integer", "default": 20 }
  }
}
```

### [FromBody] (POST with complex type)

```csharp
app.MapPost("/api/orders", (CreateOrderRequest req) => ...)
   .AsMcp("create_order", "Creates a new order.");
```

Body properties are expanded inline with the same DataAnnotations mapping as controller actions (`[Required]`, `[Range]`, `[MinLength]`, etc.).

**Note:** Minimal API query and body discovery requires `AddEndpointsApiExplorer()` so ZeroMCP can match endpoints to their API descriptions.

---

## File Upload Tools (`IFormFile`, `IFormFileCollection`)

Actions that accept `IFormFile` or `IFormFileCollection` can be called by MCP clients passing file content as base64-encoded strings.

### Single file (`IFormFile`)

```csharp
[HttpPost("upload")]
[Mcp("upload_document", Description = "Uploads a document.")]
public IActionResult UploadDocument(IFormFile document, [FromForm] string? title = null) { ... }
```

MCP schema:

```json
{
  "type": "object",
  "properties": {
    "document":         { "type": "string", "format": "byte", "description": "Base64-encoded file content" },
    "document_filename": { "type": "string", "description": "Original filename (optional)" },
    "document_content_type": { "type": "string", "description": "MIME type (optional)" },
    "title":            { "type": "string" }
  },
  "required": ["document"]
}
```

### Multiple files (`IFormFileCollection`)

```csharp
[Mcp("upload_files", Description = "Uploads multiple files.")]
public IActionResult UploadFiles(IFormFileCollection files) { ... }
```

MCP schema: `files` is an array of objects with `content` (base64, required), `filename` (optional), `content_type` (optional).

### Size limit

`MaxFormFileSizeBytes` (default: 10 MB) is enforced before decoding. Exceeded payloads return a structured error.

---

## Nested and complex types

- Nested object properties are expanded as `object` in the schema.
- Collection properties (e.g. `List<string>`) map to `array`.
- `[RegularExpression]` on body properties can be reflected in the schema when supported.

See the integration and schema tests in the repo for more edge cases (nullable, enums, required propagation).

---

## See also

- [The [Mcp] Attribute](The-Mcp-Attribute) — Exposing actions as tools
- [Controllers and Minimal APIs](Controllers-and-Minimal-APIs) — Using both together
- [Dispatch and Pipeline](Dispatch-and-Pipeline) — How arguments are bound and validated
