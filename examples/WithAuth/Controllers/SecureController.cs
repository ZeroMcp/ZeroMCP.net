using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZeroMCP.Attributes;

namespace WithAuthExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecureController : ControllerBase
{
    [HttpGet("public")]
    [Mcp("get_public_info", Description = "Returns public info. No auth required.")]
    public IActionResult GetPublic()
    {
        return Ok(new { message = "Public data" });
    }

    [HttpGet("secure")]
    [Authorize]
    [Mcp("get_secure_info", Description = "Returns secure info. Requires authentication.")]
    public IActionResult GetSecure()
    {
        return Ok(new { message = "Secure data", user = User.Identity?.Name });
    }

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    [Mcp("get_admin_info", Description = "Admin-only data.", Roles = ["Admin"])]
    public IActionResult GetAdmin()
    {
        return Ok(new { message = "Admin data" });
    }
}
