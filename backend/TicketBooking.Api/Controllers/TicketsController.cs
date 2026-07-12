using Microsoft.AspNetCore.Mvc;
using TicketBooking.Api.Dtos;
using TicketBooking.Application.Interfaces.Services;

namespace TicketBooking.Api.Controllers;

/// <summary>
/// 票券相關 API，對應 api-spec.yaml 的 Tickets 區塊
/// Task 1: 只實作 GET /tickets 和 GET /tickets/{id}，不需要 JWT
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _ticketService;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(ITicketService ticketService, ILogger<TicketsController> logger)
    {
        _ticketService = ticketService;
        _logger = logger;
    }

    /// <summary>
    /// 查詢票券列表
    /// GET /api/v1/tickets
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TicketResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TicketResponse>>> GetTickets(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching all tickets");

        var tickets = await _ticketService.GetAllTicketsAsync(cancellationToken);

        var response = tickets.Select(t => new TicketResponse
        {
            Id = t.Id,
            Name = t.Name,
            EventName = t.EventName,
            EventStartAt = t.EventStartAt,
            Price = t.Price,
            AvailableQuantity = t.AvailableQuantity
        }).ToList();

        return Ok(response);
    }

    /// <summary>
    /// 查詢票券詳情
    /// GET /api/v1/tickets/{id}
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetTicketById(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching ticket with ID: {TicketId}", id);

        var ticket = await _ticketService.GetTicketByIdAsync(id, cancellationToken);

        if (ticket == null)
        {
            _logger.LogWarning("Ticket not found: {TicketId}", id);
            return NotFound(new { errorCode = "TICKET_NOT_FOUND", message = "找不到此票券" });
        }

        var response = new TicketResponse
        {
            Id = ticket.Id,
            Name = ticket.Name,
            EventName = ticket.EventName,
            EventStartAt = ticket.EventStartAt,
            Price = ticket.Price,
            AvailableQuantity = ticket.AvailableQuantity
        };

        return Ok(response);
    }
}
