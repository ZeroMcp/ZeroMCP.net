# Versioning

ZeroMcp follows [Semantic Versioning](https://semver.org/) (SemVer) for the **NuGet package**. Full policy is in **[VERSIONING.md](../VERSIONING.md)** in the repo root.

---

## Package version (e.g. 1.0.2)

- **MAJOR** (e.g. 2.0.0) — Breaking changes. Upgrading may require code or config changes.
- **MINOR** (e.g. 1.1.0) — New features, backward compatible.
- **PATCH** (e.g. 1.0.3) — Bug fixes and safe improvements only.

---

## What we consider breaking

- Removing or renaming public types, methods, or options (e.g. **AddZeroMcp**, **MapZeroMcp**, **\[Mcp\]**, **.AsMcp()**).
- Changing the meaning of existing options so current callers behave differently.
- Changing the **MCP protocol version** we advertise or the shape of JSON-RPC responses (`initialize`, `tools/list`, `tools/call`) in a way that breaks existing MCP clients.
- Changing default option values in a way that alters behavior (reserved for MAJOR or documented exceptions).

---

## MCP protocol version

The supported MCP protocol version is **locked** to a single value (**McpProtocolConstants.ProtocolVersion**). It is used in the **initialize** response and in the GET **/mcp** example. We will **not** change it in a MINOR or PATCH release; a change would be a MAJOR release with release notes and migration notes.

---

## Non-breaking changes

- Adding new optional parameters or options (with safe defaults).
- Adding new overloads.
- Improving error messages or logging without changing contract.
- Bug fixes that restore documented behavior.

---

## Compatibility tests

The repo includes tests that assert the locked protocol version and required MCP response shapes so MINOR and PATCH releases do not break the MCP contract.

---

See **[VERSIONING.md](../VERSIONING.md)** for full text and changelog guidance.
