using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Interfaces.Security;
using TicketBooking.Application.Interfaces.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public async Task<User> RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        // 檢查 email 是否已存在
        var existingUser = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (existingUser is not null)
        {
            throw new EmailAlreadyExistsException(email);
        }

        // 雜湊密碼
        var passwordHash = _passwordHasher.HashPassword(password);

        // 使用 User.Create() factory method 建立使用者
        // 注意：不要指定 Id，讓資料庫的 uuidv7() default 生效
        var user = User.Create(email, passwordHash, displayName, role: "User");

        // 儲存到資料庫
        return await _userRepository.CreateAsync(user, cancellationToken);
    }

    public async Task<(User User, string Token)> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        // 查詢使用者
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            throw new InvalidCredentialsException();
        }

        // 驗證密碼
        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        // 簽發 JWT
        var token = GenerateJwtToken(user);

        return (user, token);
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSecret = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("JWT:SecretKey not configured");

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(jwtSecret);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };
    
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
    public async Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _userRepository.GetByIdAsync(userId, cancellationToken);
    }
}
