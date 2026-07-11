using TicketBooking.Domain.Enums;

namespace TicketBooking.Domain.Exceptions;

/// <summary>
/// 對應 docs/specs/domain-state-machine.md 定義的不合法狀態轉換。
/// 例如:Success/Failed 是終態不可逆、Pending 不可跳級到 Success 等。
/// </summary>
public class InvalidStatusTransitionException : Exception
{
    public OrderStatus FromStatus { get; }
    public OrderStatus ToStatus { get; }

    public InvalidStatusTransitionException(OrderStatus fromStatus, OrderStatus toStatus)
        : base($"不允許從 {fromStatus} 轉換到 {toStatus},請對照 docs/3_specs/domain-state-machine.md 的合法轉換表。")
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
    }
}