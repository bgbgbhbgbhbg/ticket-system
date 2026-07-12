using Microsoft.Extensions.Configuration;
using NSubstitute;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Interfaces.Security;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.UnitTests.Services;

public class AuthServiceTests
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _passwordHasher = Substitute.For<IPasswordHasher>();
        _configuration = Substitute.For<IConfiguration>();

        // 設定 JWT secret key
        _configuration["Jwt:SecretKey"].Returns("test-secret-key-at-least-32-characters-long");

        _authService = new AuthService(_userRepository, _passwordHasher, _configuration);
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_ShouldCreateUser()
    {
        // Arrange
        var email = "test@example.com";
        var password = "password123";
        var displayName = "Test User";
        var hashedPassword = "hashed_password";

        _userRepository.GetByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _passwordHasher.HashPassword(password).Returns(hashedPassword);

        var createdUser = User.Create(email, hashedPassword, displayName, "User");
        _userRepository.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(createdUser);

        // Act
        var result = await _authService.RegisterAsync(email, password, displayName);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(email, result.Email);
        Assert.Equal(displayName, result.DisplayName);
        Assert.Equal("User", result.Role);

        await _userRepository.Received(1).GetByEmailAsync(email, Arg.Any<CancellationToken>());
        _passwordHasher.Received(1).HashPassword(password);
        await _userRepository.Received(1).CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ShouldThrowEmailAlreadyExistsException()
    {
        // Arrange
        var email = "existing@example.com";
        var password = "password123";
        var displayName = "Test User";

        var existingUser = User.Create(email, "existing_hash", "Existing User", "User");
        _userRepository.GetByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(existingUser);

        // Act & Assert
        await Assert.ThrowsAsync<EmailAlreadyExistsException>(() =>
            _authService.RegisterAsync(email, password, displayName));

        await _userRepository.Received(1).GetByEmailAsync(email, Arg.Any<CancellationToken>());
        _passwordHasher.DidNotReceive().HashPassword(Arg.Any<string>());
        await _userRepository.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnUserAndToken()
    {
        // Arrange
        var email = "test@example.com";
        var password = "password123";
        var hashedPassword = "hashed_password";

        var user = User.Create(email, hashedPassword, "Test User", "User");
        _userRepository.GetByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(user);
        _passwordHasher.VerifyPassword(password, hashedPassword).Returns(true);

        // Act
        var (returnedUser, token) = await _authService.LoginAsync(email, password);

        // Assert
        Assert.NotNull(returnedUser);
        Assert.Equal(email, returnedUser.Email);
        Assert.NotEmpty(token);

        await _userRepository.Received(1).GetByEmailAsync(email, Arg.Any<CancellationToken>());
        _passwordHasher.Received(1).VerifyPassword(password, hashedPassword);
    }

    [Fact]
    public async Task LoginAsync_WithNonexistentEmail_ShouldThrowInvalidCredentialsException()
    {
        // Arrange
        var email = "nonexistent@example.com";
        var password = "password123";

        _userRepository.GetByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _authService.LoginAsync(email, password));

        await _userRepository.Received(1).GetByEmailAsync(email, Arg.Any<CancellationToken>());
        _passwordHasher.DidNotReceive().VerifyPassword(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ShouldThrowInvalidCredentialsException()
    {
        // Arrange
        var email = "test@example.com";
        var password = "wrong_password";
        var hashedPassword = "hashed_password";

        var user = User.Create(email, hashedPassword, "Test User", "User");
        _userRepository.GetByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(user);
        _passwordHasher.VerifyPassword(password, hashedPassword).Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _authService.LoginAsync(email, password));

        await _userRepository.Received(1).GetByEmailAsync(email, Arg.Any<CancellationToken>());
        _passwordHasher.Received(1).VerifyPassword(password, hashedPassword);
    }
}
