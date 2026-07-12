using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketBooking.Api.Dtos;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Services;

namespace TicketBooking.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// POST /api/v1/orders — 建立訂單（搶票入口）
    /// - 需要 JWT 認證；userId 從 token sub claim 取得，不允許 request body 自帶 userId
    /// - Idempotency-Key header 必填；重複 key 直接回傳原本訂單（HTTP 202）
    /// - 回傳 HTTP 202 Accepted，訂單進入非同步處理流程（狀態 Pending）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return UnprocessableEntity(new ErrorResponse
            {
                ErrorCode = "VALIDATION_ERROR",
                Message = "Idempotency-Key header is required",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        // userId 從 JWT sub claim 取得，防止使用者代替別人下單
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new ErrorResponse
            {
                ErrorCode = "AUTH_TOKEN_INVALID",
                Message = "Invalid user identity in token",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        try
        {
            var (order, isNew) = await _orderService.CreateOrderAsync(
                userId,
                request.TicketId,
                request.Quantity,
                idempotencyKey,
                cancellationToken);

            var response = MapToResponse(order);

            // 不管是新建還是冪等重複，都回傳 202 Accepted
            return StatusCode(StatusCodes.Status202Accepted, response);
        }
        catch (OrderQuantityExceedsLimitException ex)
        {
            return UnprocessableEntity(new ErrorResponse
            {
                ErrorCode = "ORDER_QUANTITY_EXCEEDS_LIMIT",
                Message = $"單筆訂單最多購買 {ex.MaxQuantity} 張",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
        catch (TicketNotFoundException)
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = "TICKET_NOT_FOUND",
                Message = "查無此票券",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// GET /api/v1/orders/{orderId} — 查詢單筆訂單（前端 polling 用）
    /// 只能查看屬於自己的訂單；查不到或不屬於自己都回 404
    /// </summary>
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrder(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new ErrorResponse
            {
                ErrorCode = "AUTH_TOKEN_INVALID",
                Message = "Invalid user identity in token",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        var order = await _orderService.GetOrderByIdAsync(orderId, userId, cancellationToken);
        if (order is null)
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = "ORDER_NOT_FOUND",
                Message = "查無此訂單",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        return Ok(MapToResponse(order));
    }

    private static OrderResponse MapToResponse(Domain.Entities.Order order) => new()
    {
        Id = order.Id,
        TicketId = order.TicketId,
        Quantity = order.Quantity,
        TotalAmount = order.TotalAmount,
        Status = order.Status.ToString(),
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };
}
