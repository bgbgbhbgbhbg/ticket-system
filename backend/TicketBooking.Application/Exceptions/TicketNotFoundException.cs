namespace TicketBooking.Application.Exceptions;

/// <summary>
/// 查無此票券時拋出（對應 error-codes.md: TICKET_NOT_FOUND, 404）
/// </summary>
public class TicketNotFoundException : Exception
{
    public Guid TicketId { get; }

    public TicketNotFoundException(Guid ticketId)
        : base($"Ticket '{ticketId}' not found.")
    {
        TicketId = ticketId;
    }
}
