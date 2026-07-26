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
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
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

        // 簽發 JWT（委派給 Infrastructure 層的 IJwtTokenGenerator）
        var token = _jwtTokenGenerator.GenerateToken(user);

        return (user, token);
    }

    public async Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _userRepository.GetByIdAsync(userId, cancellationToken);
    }
}
