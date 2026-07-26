using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using TicketBooking.Api.Dtos;

namespace TicketBooking.Api;

/// <summary>
/// 攔截授權失敗（403 Forbidden），回傳統一的 ErrorResponse 格式。
/// 若不加此 handler，ASP.NET Core 預設回傳空 body 的 403，前端無法判斷是「沒登入」還是「角色不夠」。
/// 對應 docs/3_specs/error-codes.md 的 AUTH_INSUFFICIENT_ROLE。
/// </summary>
public class CustomAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                ErrorCode = "AUTH_INSUFFICIENT_ROLE",
                Message = "權限不足，此操作需要 Admin 角色",
                TraceId = context.TraceIdentifier
            });
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
