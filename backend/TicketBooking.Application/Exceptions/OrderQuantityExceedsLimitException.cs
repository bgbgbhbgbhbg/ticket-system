namespace TicketBooking.Application.Exceptions;

/// <summary>
/// 單筆訂單購買數量超過上限時拋出（對應 error-codes.md: ORDER_QUANTITY_EXCEEDS_LIMIT, 422）
/// </summary>
public class OrderQuantityExceedsLimitException : Exception
{
    public int RequestedQuantity { get; }
    public int MaxQuantity { get; }

    public OrderQuantityExceedsLimitException(int requestedQuantity, int maxQuantity = 10)
        : base($"Order quantity {requestedQuantity} exceeds the maximum limit of {maxQuantity}.")
    {
        RequestedQuantity = requestedQuantity;
        MaxQuantity = maxQuantity;
    }
}
