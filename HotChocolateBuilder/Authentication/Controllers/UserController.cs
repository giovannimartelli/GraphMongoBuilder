using HotChocolateBuilder.Authentication.Models;
using HotChocolateBuilder.Authentication.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotChocolateBuilder.Authentication.Controllers;

[Authorize]
[ApiController]
[Route("login")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [AllowAnonymous]
    [HttpPost("authenticate")]
    public IActionResult Authenticate([FromBody] AuthenticateModel model)
    {
        var user = _userService.Authenticate(model.Username, model.Password);

        if (user == null)
            return Unauthorized(new { message = "Username or password is incorrect" });

        return Ok(user);
    }
}