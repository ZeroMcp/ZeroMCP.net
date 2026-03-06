using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;

namespace ZeroMCP.Dispatch;

/// <summary>
/// Output formatter that intercepts IAsyncEnumerable results during MCP streaming dispatch.
/// When the synthetic HttpContext has the capture flag set, this formatter stores the raw
/// enumerable in HttpContext.Items instead of serializing it to the response body.
/// This allows McpToolDispatcher to enumerate and stream items individually.
/// </summary>
internal sealed class McpStreamingCaptureFormatter : TextOutputFormatter
{
    internal const string CaptureFlag = "ZeroMCP.CaptureStreaming";
    internal const string CapturedEnumerable = "ZeroMCP.CapturedEnumerable";

    public McpStreamingCaptureFormatter()
    {
        SupportedMediaTypes.Add("application/json");
        SupportedEncodings.Add(Encoding.UTF8);
    }

    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        if (!context.HttpContext.Items.ContainsKey(CaptureFlag))
            return false;

        var type = context.ObjectType ?? context.Object?.GetType();
        return type is not null && IsAsyncEnumerable(type);
    }

    public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        context.HttpContext.Items[CapturedEnumerable] = context.Object;
        context.HttpContext.Response.StatusCode = 200;
        return Task.CompletedTask;
    }

    private static bool IsAsyncEnumerable(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            return true;

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return true;
        }

        return false;
    }
}
