namespace TicketBooking.Domain.Entities;

public class User
{
public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string Role { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // EF Core 所需的私有建構子，防止 EF Core 在反序列化時觸發工廠邏輯
    private User() { }

    // 統一的靜態工廠方法，確保領域模型的不變性 (Invariants)
    public static User Create(string email, string passwordHash, string displayName, string role)
    {
        var now = DateTime.UtcNow;
        return new User
        {
            Email = email,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = role,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
