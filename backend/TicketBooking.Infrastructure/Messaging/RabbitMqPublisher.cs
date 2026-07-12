using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using TicketBooking.Application.Interfaces;

namespace TicketBooking.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ 訊息發布者，對應 docs/3_specs/message-contracts.md 的 Exchange/Queue 設計。
/// 每次呼叫建立獨立的 connection + channel，適合低頻的 API 發布場景。
/// 高頻或高吞吐場景可改為 singleton connection + channel pool，但目前不需要過早最佳化。
/// </summary>
public class RabbitMqPublisher : IMessagePublisher
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(IConfiguration configuration, ILogger<RabbitMqPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task PublishOrderCreatedAsync(
        Guid orderId,
        Guid userId,
        Guid ticketId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var factory = BuildConnectionFactory();

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // 確保 exchange 和 queue 存在（冪等操作，已存在不會報錯）
        await channel.ExchangeDeclareAsync(
            exchange: "order.exchange",
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: "order.processing.queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                // DLQ 設定：消費失敗達重試上限後轉入 order.processing.dlq
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = "order.processing.dlq"
            },
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: "order.processing.dlq",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: "order.processing.queue",
            exchange: "order.exchange",
            routingKey: "order.created",
            cancellationToken: cancellationToken);

        // 建立 payload（對應 docs/3_specs/message-contracts.md 第 2.1 節）
        var message = new
        {
            messageId = Guid.NewGuid().ToString(),
            eventType = "order.created",
            occurredAt = DateTime.UtcNow,
            payload = new
            {
                orderId = orderId.ToString(),
                userId = userId.ToString(),
                ticketId = ticketId.ToString(),
                quantity
            }
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent  // 持久化，確保 RabbitMQ 重啟後訊息不遺失
        };

        await channel.BasicPublishAsync(
            exchange: "order.exchange",
            routingKey: "order.created",
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Published order.created event: orderId={OrderId}, messageId={MessageId}",
            orderId, message.messageId);
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
}
