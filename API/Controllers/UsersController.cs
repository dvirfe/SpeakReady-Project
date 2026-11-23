using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Model;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UsersController(UserDB users, SessionDB sessions) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<List<User>>> GetAll() => Ok(await users.GetAllUsersAsync());

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:int:min(1)}")]
    public async Task<ActionResult<User>> GetById(int id)
    {
        var user = await users.GetUserByIdAsync(id);
        return user is null ? NotFound(new { message = "המשתמש לא נמצא." }) : Ok(user);
    }

    [AllowAnonymous]
    [EnableRateLimiting("accounts")]
    [HttpGet("username-exists")]
    public async Task<ActionResult<bool>> UsernameExists([FromQuery, Required, StringLength(50)] string username)
        => Ok(await users.UsernameExistsAsync(username.Trim()));

    [AllowAnonymous]
    [EnableRateLimiting("accounts")]
    [HttpGet("email-exists")]
    public async Task<ActionResult<bool>> EmailExists([FromQuery, Required, EmailAddress, StringLength(254)] string email)
        => Ok(await users.EmailExistsAsync(email.Trim()));

    [Authorize(Roles = "Admin")]
    [HttpGet("count-by-role")]
    public async Task<ActionResult<List<RoleCount>>> CountByRole() => Ok(await users.CountUsersByRoleAsync());

    [AllowAnonymous]
    [EnableRateLimiting("accounts")]
    [HttpPost]
    public async Task<ActionResult<User>> Add(CreateUserRequest request)
    {
        string username = request.Username.Trim();
        string email = request.Email.Trim();
        if (await users.UsernameExistsAsync(username))
            return Conflict(new { message = "שם המשתמש כבר תפוס." });
        if (await users.EmailExistsAsync(email))
            return Conflict(new { message = "כתובת הדואר כבר רשומה." });
        var user = new User(username, request.FullName.Trim(), email, request.Password);
        try { user.Id = await users.AddUserAsync(user); }
        catch (DuplicateUserException) { return Conflict(new { message = "שם המשתמש או כתובת הדואר כבר רשומים." }); }
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, await users.GetUserByIdAsync(user.Id));
    }

    [AllowAnonymous]
    [EnableRateLimiting("accounts")]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResult>> Login(LoginRequest request)
    {
        var user = await users.CheckLoginAsync(request.Username.Trim(), request.Password);
        return user is null ? Unauthorized(new { message = "פרטי כניסה שגויים או חשבון לא פעיל." }) : Ok(await sessions.CreateAsync(user));
    }

    [Authorize(Roles = "Student,Teacher,Admin")]
    [HttpGet("me")]
    public async Task<ActionResult<User>> Me() => Ok(await users.GetUserByIdAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)));

    [Authorize(Roles = "Student,Teacher,Admin")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await sessions.RevokeAsync(Request.Headers.Authorization.ToString()[7..]);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int:min(1)}/role")]
    public async Task<IActionResult> UpdateRole(int id, UpdateRoleRequest request)
    {
        if (!Enum.IsDefined(request.Role)) return BadRequest(new { message = "התפקיד אינו תקין." });
        if (await users.GetUserByIdAsync(id) is null) return NotFound();
        await users.UpdateUserRoleAsync(id, request.Role);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int:min(1)}/password")]
    public async Task<IActionResult> UpdatePassword(int id, UpdatePasswordRequest request)
    {
        if (await users.GetUserByIdAsync(id) is null) return NotFound();
        await users.UpdatePasswordAsync(id, request.Password);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int:min(1)}/email")]
    public async Task<IActionResult> UpdateEmail(int id, UpdateEmailRequest request)
    {
        var user = await users.GetUserByIdAsync(id);
        if (user is null) return NotFound();
        var email = request.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase) && await users.EmailExistsAsync(email))
            return Conflict(new { message = "כתובת הדואר כבר רשומה." });
        try { await users.UpdateEmailAsync(id, email); }
        catch (DuplicateUserException) { return Conflict(new { message = "כתובת הדואר כבר רשומה." }); }
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int:min(1)}/active")]
    public async Task<IActionResult> UpdateActive(int id, UpdateActiveRequest request)
    {
        if (await users.GetUserByIdAsync(id) is null) return NotFound();
        await users.UpdateActiveAsync(id, request.IsActive);
        return NoContent();
    }
}

