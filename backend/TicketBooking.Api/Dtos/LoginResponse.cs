namespace TicketBooking.Api.Dtos;

public class LoginResponse
{
    public string AccessToken { get; set; } = null!;
    public int ExpiresIn { get; set; }
}
