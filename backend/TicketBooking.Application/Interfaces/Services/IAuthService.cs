using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Interfaces.Services;

public interface IAuthService
{
    Task<User> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default);
    Task<(User User, string Token)> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
