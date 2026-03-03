# Project Structure

Layout of the **mcpAPI** repository and how to build and test.

---

## Repository layout

```
mcpAPI/
├── ZeroMcp/                       ← Library (NuGet package ZeroMcp)
│   ├── README.md                  ← Package README (NuGet)
│   ├── Attributes/                ← [Mcp] (McpAttribute)
│   ├── Discovery/                 ← Controller + minimal API tool discovery
│   ├── Schema/                    ← JSON Schema for tool inputs (NJsonSchema)
│   ├── Dispatch/                  ← Synthetic HttpContext, controller/minimal invoke
│   ├── Metadata/                  ← McpToolEndpointMetadata for minimal APIs
│   ├── Extensions/                ← AddZeroMcp, MapZeroMcp, AsMcp
│   ├── Options/                   ← ZeroMcpOptions
│   └── ZeroMCP.csproj
├── ZeroMCP.Sample/                ← Sample app (Orders, Customer, Product APIs; nested route Customer/{id}/orders; health minimal endpoint; optional auth)
├── ZeroMCP.Tests/                 ← Integration + schema tests
├── wiki/                          ← Wiki documentation (this folder)
├── nupkgs/                        ← dotnet pack -o nupkgs
├── progress.md
├── VERSIONING.md
└── README.md
```

---

## Build commands

- **Targets** — Library targets .NET 9.0 and .NET 10.0; sample and tests may target a single framework.
- **Library:** `dotnet build ZeroMcp\ZeroMCP.csproj`
- **Sample:** `dotnet build ZeroMCP.Sample\ZeroMCP.Sample.csproj`
- **Tests:** `dotnet build ZeroMCP.Tests\ZeroMCP.Tests.csproj` then `dotnet test ZeroMCP.Tests\ZeroMCP.Tests.csproj`

From repo root you can run **dotnet build** to build the solution (if using a .sln or solution-style build).

---

## Test coverage

Integration and schema tests cover:

- JSON-RPC validation and errors
- Model binding failures, wrong/empty arguments
- Unauthorized **\[Authorize\]** tool calls
- **tools/list** schema shape
- Schema edge cases (nested objects, arrays, enums, route+body merging)
- Customer/Product/Orders sample tools (e.g. **get_customer_orders**)

---

## See also

- [Limitations](Limitations) — What is not supported
- [Contributing](Contributing) — How to contribute
