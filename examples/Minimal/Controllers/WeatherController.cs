using Microsoft.AspNetCore.Mvc;
using ZeroMcp.Attributes;

namespace MinimalExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherController : ControllerBase
{
    [HttpGet]
    [Mcp("get_weather", Description = "Returns current weather summary.")]
    public IActionResult Get()
    {
        return Ok(new
        {
            summary = "Sunny",
            temperatureC = 22,
            timestamp = DateTime.UtcNow
        });
    }
}
