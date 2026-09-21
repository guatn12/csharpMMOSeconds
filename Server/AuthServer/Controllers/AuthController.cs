using AuthServer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace AuthServer.Controllers
{
	[ApiController]
	[Route("api/auth")]
	public class AuthController : ControllerBase
	{
		[HttpPost("register")]
		public async Task<IActionResult> Register([FromBody] RegisterRequest request)
		{
			// Implement registration logic here
			return Ok(new { Message = "User registered successfully." });
		}
	}
}
