namespace TicketBooking.Application.Exceptions;

public class EmailAlreadyExistsException : Exception
{
    public EmailAlreadyExistsException(string email)
        : base($"Email '{email}' is already registered")
    {
    }
}
