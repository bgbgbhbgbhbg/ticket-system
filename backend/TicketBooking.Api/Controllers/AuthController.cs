using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketBooking.Api.Dtos;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Services;

namespace TicketBooking.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _authService.RegisterAsync(
                request.Email,
                request.Password,
                request.DisplayName,
                cancellationToken);

            var response = new UserResponse
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            };

            return CreatedAtAction(nameof(GetMe), null, response);
        }
        catch (EmailAlreadyExistsException)
        {
            return Conflict(new ErrorResponse
            {
                ErrorCode = "AUTH_EMAIL_ALREADY_EXISTS",
                Message = $"Email '{request.Email}' is already registered",
                TraceId = Activity.Current?.Id
            });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (user, token) = await _authService.LoginAsync(
                request.Email,
                request.Password,
                cancellationToken);

            return Ok(new LoginResponse
            {
                AccessToken = token,
                ExpiresIn = 3600 // 1 hour in seconds
            });
        }
        catch (InvalidCredentialsException)
        {
            return Unauthorized(new ErrorResponse
            {
                ErrorCode = "AUTH_INVALID_CREDENTIALS",
                Message = "Invalid email or password",
                TraceId = Activity.Current?.Id
            });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new ErrorResponse
            {
                ErrorCode = "AUTH_TOKEN_INVALID",
                Message = "Invalid token",
                TraceId = Activity.Current?.Id
            });
        }

        var user = await _authService.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new ErrorResponse
            {
                ErrorCode = "AUTH_TOKEN_INVALID",
                Message = "User not found",
                TraceId = Activity.Current?.Id
            });
        }

        return Ok(new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        });
    }
}
