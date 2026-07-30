# Versioning

ZeroMCP follows [Semantic Versioning](https://semver.org/) (SemVer) for the **NuGet package**. Full policy is in **[VERSIONING.md](../VERSIONING.md)** in the repo root.

---

## Package version (e.g. 2.0.0)

- **MAJOR** (e.g. 2.0.0) — Breaking changes. Upgrading may require code or config changes.
- **MINOR** (e.g. 1.1.0) — New features, backward compatible.
- **PATCH** (e.g. 1.0.3) — Bug fixes and safe improvements only.

---

## What we consider breaking

- Removing or renaming public types, methods, or options (e.g. **AddZeroMCP**, **MapZeroMCP**, **\[Mcp\]**, **.AsMcp()**).
- Changing the meaning of existing options so current callers behave differently.
- Changing the **MCP protocol versions** we advertise or the shape of JSON-RPC responses in a way that breaks existing MCP clients.
- Changing default option values in a way that alters behavior (reserved for MAJOR or documented exceptions).

---

## MCP protocol versions (dual-era)

ZeroMCP 2.0 supports both eras:

| Era | Constant | Version | Entry point |
|-----|----------|---------|-------------|
| Modern | `McpProtocolConstants.ProtocolVersion` | **2026-07-28** | `server/discover` + per-request `_meta` / `MCP-Protocol-Version` |
| Legacy | `McpProtocolConstants.LegacyProtocolVersion` | **2024-11-05** | `initialize` (when **EnableLegacyProtocol** is true, default) |

We will **not** change these locked strings in a MINOR or PATCH release.

---

## Non-breaking changes

- Adding new optional parameters or options (with safe defaults).
- Adding new overloads.
- Improving error messages or logging without changing contract.
- Bug fixes that restore documented behavior.

---

## Compatibility tests

The repo includes tests for modern discover/header validation and legacy initialize so MINOR and PATCH releases do not break the MCP contract.

---

See **[VERSIONING.md](../VERSIONING.md)** for full text and changelog guidance.
