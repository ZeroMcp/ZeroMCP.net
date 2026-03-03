# Parameters and Schemas

ZeroMcp merges all parameter sources into a single flat JSON Schema object that the LLM fills in when calling a tool.

---

## Mapping

| Parameter source | MCP mapping |
|------------------|-------------|
| **Route params** (`{id}`) | Always required properties |
| **Query params** (`?status=`) | Optional (or required if `[Required]`) |
| **`[FromBody]` object** | Properties expanded inline from JSON Schema |

---

## Example

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

## Nested and complex types

- Nested object properties are expanded as `object` in the schema.
- Collection properties (e.g. `List<string>`) map to `array`.
- `[RegularExpression]` on body properties can be reflected in the schema when supported.

See the integration and schema tests in the repo for more edge cases (nullable, enums, required propagation).

---

## See also

- [The [Mcp] Attribute](The-Mcp-Attribute) — Exposing actions as tools
- [Dispatch and Pipeline](Dispatch-and-Pipeline) — How arguments are bound and validated
