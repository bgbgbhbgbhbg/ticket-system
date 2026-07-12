using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Interfaces.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Services;

public class TicketService : ITicketService
{
    private readonly ITicketRepository _ticketRepository;

    public TicketService(ITicketRepository ticketRepository)
    {
        _ticketRepository = ticketRepository;
    }

    public async Task<List<Ticket>> GetAllTicketsAsync(CancellationToken cancellationToken = default)
    {
        return await _ticketRepository.GetAllAsync(cancellationToken);
    }

    public async Task<Ticket?> GetTicketByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _ticketRepository.GetByIdAsync(id, cancellationToken);
    }
}
