# Versioning and Breaking Change Policy

ZeroMCP follows [Semantic Versioning](https://semver.org/) (SemVer) for the **NuGet package** and documents how we treat breaking changes.

## Package version (e.g. 1.0.2)

- **MAJOR** (e.g. 2.0.0): Breaking changes to the public API or behavior. Upgrading may require code or configuration changes.
- **MINOR** (e.g. 1.1.0): New features, backward compatible. No breaking changes to existing APIs or MCP response shapes.
- **PATCH** (e.g. 1.0.3): Bug fixes and safe improvements only. Fully backward compatible.

## What we consider breaking

- Removing or renaming public types, methods, or options (e.g. `ZeroMCPOptions`, `AddZeroMCP`, `MapZeroMCP`, `[Mcp]`, `.AsMcp()`).
- Changing the meaning of existing options in a way that changes runtime behavior for current callers.
- Changing the **MCP protocol version** we advertise (see below) or the shape of JSON-RPC responses (e.g. `initialize`, `tools/list`, `tools/call` result/error format) in a way that breaks existing MCP clients.
- Changing the default value of an option in a way that alters behavior (we may do this only in a MAJOR release, or document it as a rare exception with migration notes).

## MCP protocol version

ZeroMCP **2.0** is a **dual-era** MCP server:

| Era | Version | How clients open |
|-----|---------|------------------|
| **Modern** | `2026-07-28` (`McpProtocolConstants.ProtocolVersion`) | Per-request `params._meta` + Streamable HTTP headers; `server/discover` |
| **Legacy** | `2024-11-05` (`McpProtocolConstants.LegacyProtocolVersion`) | `initialize` / `notifications/initialized` handshake (when `EnableLegacyProtocol` is true) |

- Modern version is advertised via `server/discover` (`supportedVersions`) and used in GET `/mcp` examples.
- Legacy `initialize` continues to return `2024-11-05` for existing clients.
- Changing either locked version string is a **MAJOR** release.

## Non-breaking changes

- Adding new optional parameters or options (with safe defaults).
- Adding new overloads.
- Improving error messages or logging (without changing contract).
- Bug fixes that restore documented behavior.
- New tests and compatibility tests that validate existing behavior.

## Compatibility tests

The repository includes **compatibility tests** that assert:

- Modern protocol version `2026-07-28` and legacy `2024-11-05` constants.
- `server/discover`, mirrored HTTP headers, and `UnsupportedProtocolVersion` / `HeaderMismatch` errors.
- Legacy `initialize` response shapes for dual-era clients.
- Required JSON-RPC and MCP response shapes (e.g. `tools/list` tool structure, error `code`/`message`).

These tests help ensure that MINOR and PATCH releases do not introduce breaking changes to the MCP contract.

## Changelog and release notes

For each release we recommend:

- **PATCH**: List bug fixes and any notable behavior corrections.
- **MINOR**: List new features and point to migration notes if any.
- **MAJOR**: List breaking changes and provide a migration guide.

A `CHANGELOG.md` or GitHub Releases can be used to record these.
