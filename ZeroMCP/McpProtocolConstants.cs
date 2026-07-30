namespace ZeroMCP;

/// <summary>
/// MCP (Model Context Protocol) transport constants.
/// ZeroMCP is a dual-era server: modern (<c>2026-07-28</c>) and legacy (<c>2024-11-05</c>).
/// </summary>
public static class McpProtocolConstants
{
    /// <summary>
    /// Latest (modern) MCP protocol version. Per-request <c>_meta</c>, no initialize handshake.
    /// </summary>
    public const string ProtocolVersion = "2026-07-28";

    /// <summary>
    /// Alias for <see cref="ProtocolVersion"/> (modern era).
    /// </summary>
    public const string ModernProtocolVersion = ProtocolVersion;

    /// <summary>
    /// Legacy MCP protocol version that uses the <c>initialize</c>/<c>initialized</c> handshake.
    /// Kept for dual-era compatibility with existing clients.
    /// </summary>
    public const string LegacyProtocolVersion = "2024-11-05";

    /// <summary>
    /// All protocol versions this server advertises via <c>server/discover</c>.
    /// </summary>
    public static readonly string[] SupportedProtocolVersions =
    [
        ModernProtocolVersion,
        LegacyProtocolVersion
    ];

    /// <summary>JSON-RPC error: HTTP headers missing or mismatched with body (-32020).</summary>
    public const int ErrorHeaderMismatch = -32020;

    /// <summary>JSON-RPC error: client missing a required capability (-32021).</summary>
    public const int ErrorMissingRequiredClientCapability = -32021;

    /// <summary>JSON-RPC error: unsupported protocol version (-32022).</summary>
    public const int ErrorUnsupportedProtocolVersion = -32022;

    /// <summary>JSON-RPC error: request cancelled (-32800).</summary>
    public const int ErrorRequestCancelled = -32800;

    /// <summary>HTTP header: protocol version (modern Streamable HTTP).</summary>
    public const string HeaderProtocolVersion = "MCP-Protocol-Version";

    /// <summary>HTTP header: JSON-RPC method name.</summary>
    public const string HeaderMethod = "Mcp-Method";

    /// <summary>HTTP header: tool/prompt name or resource URI.</summary>
    public const string HeaderName = "Mcp-Name";

    /// <summary>Legacy session header (ignored on modern; used by dual-era GET SSE).</summary>
    public const string HeaderSessionId = "Mcp-Session-Id";

    /// <summary><c>_meta</c> key for protocol version.</summary>
    public const string MetaProtocolVersion = "io.modelcontextprotocol/protocolVersion";

    /// <summary><c>_meta</c> key for client identity.</summary>
    public const string MetaClientInfo = "io.modelcontextprotocol/clientInfo";

    /// <summary><c>_meta</c> key for client capabilities.</summary>
    public const string MetaClientCapabilities = "io.modelcontextprotocol/clientCapabilities";

    /// <summary><c>_meta</c> key for server identity on results.</summary>
    public const string MetaServerInfo = "io.modelcontextprotocol/serverInfo";

    /// <summary><c>_meta</c> key for subscription id on listen streams.</summary>
    public const string MetaSubscriptionId = "io.modelcontextprotocol/subscriptionId";
}
