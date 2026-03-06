namespace ZeroMCP.Dispatch;

/// <summary>
/// A single chunk yielded during streaming dispatch of an IAsyncEnumerable tool.
/// </summary>
public sealed class DispatchStreamChunk
{
    /// <summary>Serialized JSON content of this chunk (one element from the enumerable).</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>True when this is the final chunk (enumeration complete or error).</summary>
    public bool IsLast { get; init; }

    /// <summary>True when this chunk represents an error (e.g. enumerator exception, max items exceeded).</summary>
    public bool IsError { get; init; }
}
