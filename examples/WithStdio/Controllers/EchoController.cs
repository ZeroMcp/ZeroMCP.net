using Microsoft.AspNetCore.Mvc;
using ZeroMCP.Attributes;

namespace WithStdioExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EchoController : ControllerBase
{
    [HttpGet]
    [Mcp("echo", Description = "Echoes a message back. Useful for testing stdio connectivity.")]
    public IActionResult Get(string? message = null)
    {
        var msg = message ?? "Hello from stdio!";
        return Ok(new { echo = msg, timestamp = DateTime.UtcNow });
    }
}
