using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ZeroMCP.Protocol;

/// <summary>
/// Parsed modern-era request metadata from params._meta and HTTP headers.
/// </summary>
internal sealed class McpModernRequestContext
{
    public required string ProtocolVersion { get; init; }
    public required string ClientName { get; init; }
    public required string ClientVersion { get; init; }
    public JsonElement ClientCapabilities { get; init; }
}

/// <summary>
/// Helpers for dual-era detection, modern <c>_meta</c> parsing, and Streamable HTTP header validation.
/// </summary>
internal static class McpModernProtocol
{
    private const string Base64Prefix = "=?base64?";
    private const string Base64Suffix = "?=";

    public static bool LooksModern(HttpContext? httpContext, JsonElement root, string method)
    {
        if (method is "server/discover" or "subscriptions/listen")
            return true;

        if (httpContext is not null
            && httpContext.Request.Headers.TryGetValue(McpProtocolConstants.HeaderProtocolVersion, out var hdr)
            && !string.IsNullOrWhiteSpace(hdr))
        {
            var v = hdr.ToString().Trim();
            if (v == McpProtocolConstants.ModernProtocolVersion)
                return true;
            // Unknown modern-looking version header still goes through modern validation
            // so UnsupportedProtocolVersionError can be returned.
            if (v.Length >= 10 && v[4] == '-' && v[7] == '-' && v != McpProtocolConstants.LegacyProtocolVersion)
                return true;
        }

        if (TryGetParamsMeta(root, out var meta)
            && meta.TryGetProperty(McpProtocolConstants.MetaProtocolVersion, out var pv)
            && pv.ValueKind == JsonValueKind.String)
        {
            var v = pv.GetString();
            if (v == McpProtocolConstants.ModernProtocolVersion)
                return true;
            if (!string.IsNullOrEmpty(v) && v != McpProtocolConstants.LegacyProtocolVersion)
                return true;
        }

        return false;
    }

    public static bool TryGetParamsMeta(JsonElement root, out JsonElement meta)
    {
        meta = default;
        if (!root.TryGetProperty("params", out var @params) || @params.ValueKind != JsonValueKind.Object)
            return false;
        if (!@params.TryGetProperty("_meta", out meta) || meta.ValueKind != JsonValueKind.Object)
            return false;
        return true;
    }

    /// <summary>
    /// Validates modern params._meta. Returns null on success, or (code, message, data) on failure.
    /// </summary>
    public static (int Code, string Message, object? Data)? ValidateAndParseMeta(
        JsonElement root,
        out McpModernRequestContext? context)
    {
        context = null;

        if (!TryGetParamsMeta(root, out var meta))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                "Modern MCP requests require params._meta with protocolVersion, clientInfo, and clientCapabilities",
                null);
        }

        if (!meta.TryGetProperty(McpProtocolConstants.MetaProtocolVersion, out var versionEl)
            || versionEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(versionEl.GetString()))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"params._meta must include '{McpProtocolConstants.MetaProtocolVersion}'",
                null);
        }

        var protocolVersion = versionEl.GetString()!.Trim();

        if (!meta.TryGetProperty(McpProtocolConstants.MetaClientInfo, out var clientInfo)
            || clientInfo.ValueKind != JsonValueKind.Object)
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"params._meta must include '{McpProtocolConstants.MetaClientInfo}'",
                null);
        }

        if (!clientInfo.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameEl.GetString()))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"params._meta['{McpProtocolConstants.MetaClientInfo}'] must include name",
                null);
        }

        if (!clientInfo.TryGetProperty("version", out var verEl) || verEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(verEl.GetString()))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"params._meta['{McpProtocolConstants.MetaClientInfo}'] must include version",
                null);
        }

        if (!meta.TryGetProperty(McpProtocolConstants.MetaClientCapabilities, out var caps)
            || caps.ValueKind != JsonValueKind.Object)
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"params._meta must include '{McpProtocolConstants.MetaClientCapabilities}'",
                null);
        }

        if (!IsSupportedVersion(protocolVersion))
        {
            return (McpProtocolConstants.ErrorUnsupportedProtocolVersion,
                "Unsupported protocol version",
                new
                {
                    supported = McpProtocolConstants.SupportedProtocolVersions,
                    requested = protocolVersion
                });
        }

        // Modern path only accepts the modern revision for non-discover traffic
        // after negotiation; server/discover may advertise both.
        if (protocolVersion != McpProtocolConstants.ModernProtocolVersion
            && protocolVersion != McpProtocolConstants.LegacyProtocolVersion)
        {
            return (McpProtocolConstants.ErrorUnsupportedProtocolVersion,
                "Unsupported protocol version",
                new
                {
                    supported = McpProtocolConstants.SupportedProtocolVersions,
                    requested = protocolVersion
                });
        }

        context = new McpModernRequestContext
        {
            ProtocolVersion = protocolVersion,
            ClientName = nameEl.GetString()!,
            ClientVersion = verEl.GetString()!,
            ClientCapabilities = caps.Clone()
        };
        return null;
    }

    public static bool IsSupportedVersion(string version)
        => McpProtocolConstants.SupportedProtocolVersions.Contains(version, StringComparer.Ordinal);

    /// <summary>
    /// Validates Streamable HTTP mirrored headers against the JSON-RPC body.
    /// Returns null on success.
    /// </summary>
    public static (int Code, string Message)? ValidateHttpHeaders(
        HttpContext httpContext,
        string method,
        JsonElement root)
    {
        var headers = httpContext.Request.Headers;

        if (!headers.TryGetValue(McpProtocolConstants.HeaderProtocolVersion, out var versionHdr)
            || string.IsNullOrWhiteSpace(versionHdr))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"Missing required header '{McpProtocolConstants.HeaderProtocolVersion}'");
        }

        var headerVersion = versionHdr.ToString().Trim();

        if (!TryGetParamsMeta(root, out var meta)
            || !meta.TryGetProperty(McpProtocolConstants.MetaProtocolVersion, out var metaVersionEl)
            || metaVersionEl.ValueKind != JsonValueKind.String)
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"{McpProtocolConstants.HeaderProtocolVersion} requires matching params._meta['{McpProtocolConstants.MetaProtocolVersion}']");
        }

        var metaVersion = metaVersionEl.GetString()!.Trim();
        if (!string.Equals(headerVersion, metaVersion, StringComparison.Ordinal))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"Header mismatch: {McpProtocolConstants.HeaderProtocolVersion} header value '{headerVersion}' does not match body value '{metaVersion}'");
        }

        if (!headers.TryGetValue(McpProtocolConstants.HeaderMethod, out var methodHdr)
            || string.IsNullOrWhiteSpace(methodHdr))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"Missing required header '{McpProtocolConstants.HeaderMethod}'");
        }

        var headerMethod = methodHdr.ToString().Trim();
        if (!string.Equals(headerMethod, method, StringComparison.Ordinal))
        {
            return (McpProtocolConstants.ErrorHeaderMismatch,
                $"Header mismatch: {McpProtocolConstants.HeaderMethod} header value '{headerMethod}' does not match body value '{method}'");
        }

        if (RequiresNameHeader(method))
        {
            if (!headers.TryGetValue(McpProtocolConstants.HeaderName, out var nameHdr)
                || string.IsNullOrWhiteSpace(nameHdr))
            {
                return (McpProtocolConstants.ErrorHeaderMismatch,
                    $"Missing required header '{McpProtocolConstants.HeaderName}' for method '{method}'");
            }

            var decodedName = DecodeHeaderValue(nameHdr.ToString().Trim());
            var bodyName = ExtractNameFromBody(root, method);
            if (bodyName is null)
            {
                return (McpProtocolConstants.ErrorHeaderMismatch,
                    $"Method '{method}' requires params.name or params.uri matching '{McpProtocolConstants.HeaderName}'");
            }

            if (!string.Equals(decodedName, bodyName, StringComparison.Ordinal))
            {
                return (McpProtocolConstants.ErrorHeaderMismatch,
                    $"Header mismatch: {McpProtocolConstants.HeaderName} header value '{decodedName}' does not match body value '{bodyName}'");
            }
        }

        return null;
    }

    public static bool RequiresNameHeader(string method)
        => method is "tools/call" or "resources/read" or "prompts/get";

    public static string? ExtractNameFromBody(JsonElement root, string method)
    {
        if (!root.TryGetProperty("params", out var @params) || @params.ValueKind != JsonValueKind.Object)
            return null;

        if (method == "resources/read")
        {
            if (@params.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
                return uri.GetString();
            return null;
        }

        if (@params.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return name.GetString();
        return null;
    }

    public static string DecodeHeaderValue(string value)
    {
        if (value.StartsWith(Base64Prefix, StringComparison.Ordinal)
            && value.EndsWith(Base64Suffix, StringComparison.Ordinal)
            && value.Length > Base64Prefix.Length + Base64Suffix.Length)
        {
            var b64 = value.Substring(Base64Prefix.Length, value.Length - Base64Prefix.Length - Base64Suffix.Length);
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch (FormatException)
            {
                return value;
            }
        }

        return value;
    }

    public static Dictionary<string, object?> CreateServerInfoMeta(string? serverName, string serverVersion)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpProtocolConstants.MetaServerInfo] = new
            {
                name = serverName ?? "ZeroMCP",
                version = serverVersion
            }
        };
    }

    public static Dictionary<string, object?> WithModernResultFields(
        IDictionary<string, object?> payload,
        string? serverName,
        string serverVersion,
        bool includeCache = false,
        int ttlMs = 0,
        string cacheScope = "private")
    {
        var result = new Dictionary<string, object?>(payload, StringComparer.Ordinal)
        {
            ["resultType"] = "complete",
            ["_meta"] = CreateServerInfoMeta(serverName, serverVersion)
        };

        if (includeCache)
        {
            result["ttlMs"] = ttlMs;
            result["cacheScope"] = cacheScope;
        }

        return result;
    }
}
