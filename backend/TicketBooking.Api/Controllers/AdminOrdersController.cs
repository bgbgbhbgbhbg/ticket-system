using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketBooking.Api.Dtos;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Services;
using TicketBooking.Domain.Enums;
using TicketBooking.Domain.Exceptions;

namespace TicketBooking.Api.Controllers;

[ApiController]
[Route("api/v1/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public AdminOrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// GET /api/v1/admin/orders — 分頁查詢所有訂單（Admin 專用）
    /// 對應 api-spec.yaml Admin 區塊；一般 User 呼叫會得到 403 AUTH_INSUFFICIENT_ROLE
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOrders(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        OrderStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
            {
                return UnprocessableEntity(new ErrorResponse
                {
                    ErrorCode = "VALIDATION_ERROR",
                    Message = $"status 不合法：{status}，允許值為 Pending / Processing / Success / Failed",
                    TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
                });
            }
            statusFilter = parsed;
        }

        var (items, total) = await _orderService.GetOrdersAsync(statusFilter, page, pageSize, cancellationToken);

        return Ok(new PagedOrderResponse
        {
            Items = items.Select(o => new OrderResponse
            {
                Id = o.Id,
                TicketId = o.TicketId,
                Quantity = o.Quantity,
                TotalAmount = o.TotalAmount,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    /// <summary>
    /// PATCH /api/v1/admin/orders/{orderId}/status — 手動介入訂單狀態
    /// toStatus 限定 Success / Failed；狀態轉換仍須通過 domain-state-machine.md 合法轉換表
    /// </summary>
    [HttpPatch("{orderId:guid}/status")]
    public async Task<IActionResult> UpdateOrderStatus(
        Guid orderId,
        [FromBody] AdminUpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrderStatus>(request.ToStatus, out var toStatus))
        {
            return UnprocessableEntity(new ErrorResponse
            {
                ErrorCode = "VALIDATION_ERROR",
                Message = "toStatus 不合法",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        try
        {
            // reason 前綴標示為 Admin 手動介入（與 Worker 自動處理的 reason 做區分）
            var fullReason = $"admin_manual_override: {request.Reason}";
            var order = await _orderService.UpdateOrderStatusAsync(orderId, toStatus, fullReason, cancellationToken);

            return Ok(new OrderResponse
            {
                Id = order.Id,
                TicketId = order.TicketId,
                Quantity = order.Quantity,
                TotalAmount = order.TotalAmount,
                Status = order.Status.ToString(),
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt
            });
        }
        catch (OrderNotFoundException)
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = "ORDER_NOT_FOUND",
                Message = $"訂單 {orderId} 不存在",
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
        catch (InvalidStatusTransitionException ex)
        {
            return UnprocessableEntity(new ErrorResponse
            {
                ErrorCode = "ORDER_INVALID_STATUS_TRANSITION",
                Message = ex.Message,
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
