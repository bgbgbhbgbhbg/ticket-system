namespace TicketBooking.Application.Exceptions;

/// <summary>
/// 並發場景下的 TOCTOU：兩個請求同時通過 idempotency key 查詢（都查到 null），
/// 第二個 INSERT 觸發 DB 的 UNIQUE constraint（idx_orders_user_idempotency_key）失敗。
/// OrderRepository.CreateAsync 捕捉到 unique violation 後拋出此例外；
/// OrderService 捕捉後重新查詢，回傳既有訂單（IsNew = false），讓呼叫端得到正確的 409 回應。
/// </summary>
public class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException()
        : base("Duplicate idempotency key: a concurrent request already created this order.") { }
}
