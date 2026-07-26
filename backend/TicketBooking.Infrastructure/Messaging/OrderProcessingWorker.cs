using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TicketBooking.Application.Interfaces.Services;

namespace TicketBooking.Infrastructure.Messaging;

/// <summary>
/// 訂單處理 BackgroundService，持續消費 order.processing.queue。
/// 對應 docs/3_specs/message-contracts.md 第 3 節的消費端邏輯。
///
/// 設計重點：
/// - autoAck = false（手動 ack/nack，確保訊息不被提前標記完成）
/// - 每筆訊息建立獨立 IServiceScope（避免 scoped DbContext 被 singleton Worker 長期持有）
/// - 業務失敗（庫存不足/重試耗盡）→ ack，不重新投遞
/// - 技術失敗（DB 斷線等基礎設施例外）→ nack，交 RabbitMQ 重新投遞
/// </summary>
public class OrderProcessingWorker : BackgroundService
{
    /// <summary>
    /// 技術性失敗最大重試次數，超過後轉入 DLQ。
    /// 對應 docs/3_specs/message-contracts.md 的重試規格。
    /// </summary>
    private const int MaxRetryCount = 3;
    private const string RetryCountHeader = "x-retry-count";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderProcessingWorker> _logger;

    public OrderProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OrderProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderProcessingWorker 啟動");

        // 建立長駐的 RabbitMQ connection（BackgroundService 生命週期內重用）
        var factory = BuildConnectionFactory();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                // 確保 exchange、queue 存在（冪等操作，與 RabbitMqPublisher 宣告一致）
                await DeclareTopologyAsync(channel, stoppingToken);

                // QoS：每次只預取 1 筆，確保處理完才拿下一筆
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                    await HandleMessageAsync(channel, ea, stoppingToken);

                await channel.BasicConsumeAsync(
                    queue: "order.processing.queue",
                    autoAck: false,  // 手動 ack/nack
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("OrderProcessingWorker 開始消費 order.processing.queue");

                // 建立連線中斷感知的 CancellationToken：
                // 若 broker 在 stoppingToken 取消之前斷線，connectionCts 被取消，
                // Task.Delay 拋出 OperationCanceledException → 進入重連流程。
                // （若只用 stoppingToken，broker 斷線後 Task.Delay 永遠不會結束）
                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                connection.ConnectionShutdownAsync += (_, _) =>
                {
                    _logger.LogWarning("RabbitMQ 連線中斷，觸發重連");
                    connectionCts.Cancel();
                    return Task.CompletedTask;
                };

                await Task.Delay(Timeout.Infinite, connectionCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常關機，直接退出
                break;
            }
            catch (Exception ex)
            {
                // 連線中斷時，等待後重連
                _logger.LogError(ex, "OrderProcessingWorker 連線中斷，5 秒後重試");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 正常關機：不讓 OperationCanceledException 逸出 catch 區塊導致 BackgroundService 以例外結束
                    break;
                }
            }
        }

        _logger.LogInformation("OrderProcessingWorker 停止");
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        Guid? orderId = null;
        try
        {
            // ── 反序列化訊息 ──────────────────────────────────────────────────
            var body = Encoding.UTF8.GetString(ea.Body.Span);
            var message = JsonSerializer.Deserialize<OrderCreatedMessage>(body, JsonOptions);

            if (message?.Payload?.OrderId is null)
            {
                _logger.LogWarning("收到無效訊息（缺少 orderId），直接 ack 丟棄：{Body}", body);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            orderId = message.Payload.OrderId;
            _logger.LogInformation("收到 order.created 訊息，orderId={OrderId}", orderId);

            // ── 每筆訊息使用獨立 scope（避免 scoped DbContext 被 singleton 長期持有）──
            using var scope = _scopeFactory.CreateScope();
            var orderProcessingService = scope.ServiceProvider.GetRequiredService<IOrderProcessingService>();

            await orderProcessingService.ProcessOrderAsync(orderId.Value, stoppingToken);

            // 業務流程完整執行完（不管 Success 還是 Failed）→ ack
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            _logger.LogInformation("Order {OrderId} 處理完成，ack", orderId);
        }
        catch (JsonException ex)
        {
            // 訊息格式錯誤（poison message）→ ack 丟棄，避免 requeue 後的無限重試
            _logger.LogError(ex, "收到格式錯誤的訊息（delivery tag {Tag}），直接 ack 丟棄", ea.DeliveryTag);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常關機：不 ack/nack，channel 關閉時未 ack 訊息會由 RabbitMQ 自動 requeue
            _logger.LogInformation(
                "OrderProcessingWorker 正在停止，訊息尚未處理完成（delivery tag {Tag}），將由 RabbitMQ 重新投遞",
                ea.DeliveryTag);
        }
        catch (NpgsqlException ex)
        {
            // 技術性失敗（DB 連線中斷等基礎設施例外）→ 手動 republish 並帶入遞增的重試計數
            await HandleTechnicalFailureAsync(channel, ea, orderId, ex, stoppingToken);
        }
        catch (Exception ex)
        {
            // 其他未預期例外（同上）
            await HandleTechnicalFailureAsync(channel, ea, orderId, ex, stoppingToken);
        }
    }

    /// <summary>
    /// 技術性失敗的重試邏輯：讀取 x-retry-count header，未超過上限則 republish 到原始 queue，
    /// 超過上限則手動發到 DLQ。兩種情況都對原訊息 ack（因為已複製到別處）。
    /// 這樣能精確控制重試次數（相對於 Redelivered 布林值只能判斷是否第一次）。
    /// </summary>
    private async Task HandleTechnicalFailureAsync(
        IChannel channel, BasicDeliverEventArgs ea, Guid? orderId, Exception ex, CancellationToken ct)
    {
        // 讀取目前的重試次數（header 值以 UTF-8 byte[] 儲存）
        var retryCount = 0;
        if (ea.BasicProperties.Headers?.TryGetValue(RetryCountHeader, out var raw) == true && raw is byte[] bytes)
        {
            retryCount = int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) ? parsed : 0;
        }

        if (retryCount < MaxRetryCount)
        {
            // 尚未超過重試上限：republish 到原始 queue，帶遞增後的重試計數
            var props = new BasicProperties
            {
                ContentType = ea.BasicProperties.ContentType,
                DeliveryMode = ea.BasicProperties.DeliveryMode,
                MessageId = ea.BasicProperties.MessageId,  // 保留原始 messageId，供去重判斷
                Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
            };
            props.Headers[RetryCountHeader] = Encoding.UTF8.GetBytes((retryCount + 1).ToString());

            await channel.BasicPublishAsync(
                exchange: "order.exchange",
                routingKey: "order.created",
                mandatory: false,
                basicProperties: props,
                body: ea.Body,
                cancellationToken: ct);

            _logger.LogWarning(ex,
                "Order {OrderId} 技術性失敗，第 {Retry}/{Max} 次重試，重新發布到 order.processing.queue",
                orderId, retryCount + 1, MaxRetryCount);
        }
        else
        {
            // 重試次數耗盡：手動發布到 DLQ
            var dlqProps = new BasicProperties
            {
                ContentType = ea.BasicProperties.ContentType,
                DeliveryMode = ea.BasicProperties.DeliveryMode,
                MessageId = ea.BasicProperties.MessageId,
                Headers = ea.BasicProperties.Headers is not null
                    ? new Dictionary<string, object?>(ea.BasicProperties.Headers)
                    : null
            };

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: "order.processing.dlq",
                mandatory: false,
                basicProperties: dlqProps,
                body: ea.Body,
                cancellationToken: ct);

            _logger.LogError(ex,
                "Order {OrderId} 技術性失敗，已重試 {Max} 次仍失敗，轉入 order.processing.dlq",
                orderId, MaxRetryCount);
        }

        // 不管是重新發布還是進 DLQ，原本這則訊息都要 ack 掉（已手動複製到別處）
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: "order.exchange",
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: "order.processing.queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = "order.processing.dlq"
            },
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: "order.processing.dlq",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: "order.processing.queue",
            exchange: "order.exchange",
            routingKey: "order.created",
            cancellationToken: ct);
    }

    private ConnectionFactory BuildConnectionFactory()
    {
        var host = _configuration["RabbitMQ:Host"] ?? "localhost";
        var port = int.TryParse(_configuration["RabbitMQ:Port"], out var p) ? p : 5672;
        var user = _configuration["RabbitMQ:Username"] ?? "guest";
        var password = _configuration["RabbitMQ:Password"] ?? "guest";

        return new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = user,
            Password = password
        };
    }

    // ── 訊息反序列化 DTO ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record OrderCreatedMessage(
        string? MessageId,
        string? EventType,
        DateTime OccurredAt,
        OrderCreatedPayload? Payload);

    private sealed record OrderCreatedPayload(
        Guid? OrderId,
        Guid? UserId,
        Guid? TicketId,
        int Quantity);
}
