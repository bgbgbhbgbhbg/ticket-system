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

    private static readonly HashSet<string> ValidRoles =
        new(StringComparer.Ordinal) { "User", "Admin" };

    // 統一的靜態工廠方法，確保領域模型的不變性 (Invariants)
    public static User Create(string email, string passwordHash, string displayName, string role)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("email 不可為空白。", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("passwordHash 不可為空白。", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("displayName 不可為空白。", nameof(displayName));
        if (!ValidRoles.Contains(role))
            throw new ArgumentException("role 必須是 \"User\" 或 \"Admin\"。", nameof(role));

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
