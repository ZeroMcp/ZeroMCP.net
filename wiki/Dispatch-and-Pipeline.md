# Dispatch and Pipeline

When an MCP client calls a tool, ZeroMcp invokes your action or endpoint **in-process** through the normal ASP.NET Core pipeline.

---

## Steps

1. **DI scope** — A fresh scope is created (same as a normal request).
2. **Synthetic HttpContext** — Built with route values (including ambient **controller** / **action** for link generation), query string, and body from the JSON arguments.
3. **Endpoint** — The matched endpoint is set on the context so **CreatedAtAction** and **LinkGenerator** can resolve.
4. **Invocation** — The controller action is invoked via **IActionInvokerFactory** or the minimal endpoint's **RequestDelegate**.
5. **Response** — The response body is captured and returned as the MCP tool result.

---

## What works out of the box

- **`[Authorize]`** — Auth middleware and authorization run on the synthetic request; 401/403 are returned as MCP errors.
- **Auth forwarding** — Headers listed in **ForwardHeaders** (e.g. `Authorization`) are copied from the MCP request to the synthetic request.
- **CreatedAtAction** — The synthetic request has endpoint and controller/action route values so link generation succeeds.
- **ModelState / validation** — Validation errors (e.g. missing required body, invalid status) return as MCP error results (HTTP 4xx body in the result content).
- **Exception filters** — Unhandled exceptions are caught and returned as MCP errors.
- **DI** — Your services, repositories, and business logic are resolved and run as in a normal request.

---

## Fallback if link generation fails

If **CreatedAtAction** or **LinkGenerator** ever fails in your environment (e.g. missing route values), use an explicit URL:

```csharp
return Created(Url.Action(nameof(GetOrder), new { id = order.Id })!, order);
// or
return Created($"/api/orders/{order.Id}", order);
```

---

## See also

- [Parameters and Schemas](Parameters-and-Schemas) — How arguments are bound
- [Governance and Security](Governance-and-Security) — Auth and authorization
- [Limitations](Limitations) — Streaming, form data, etc.
