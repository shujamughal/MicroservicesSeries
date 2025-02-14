using Microsoft.AspNetCore.Connections;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var consumerTask = Task.Run(async () =>
{
    var factory = new ConnectionFactory() { HostName = "localhost" };
    await using var connection = await factory.CreateConnectionAsync("notification-service-connection");
    await using var channel = await connection.CreateChannelAsync();

    // 🟢 Bind to Exchange
    await channel.ExchangeDeclareAsync("payment_exchange", ExchangeType.Fanout);
    await channel.QueueDeclareAsync("notification_queue", durable: false, exclusive: false, autoDelete: false, arguments: null);
    await channel.QueueBindAsync("notification_queue", "payment_exchange", "");

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += async (model, ea) =>
    {
        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        Console.WriteLine($"[Notification Service] Sending email: {message}");
    };

    await channel.BasicConsumeAsync(queue: "notification_queue", autoAck: true, consumer: consumer);
    Console.WriteLine("[Notification Service] Waiting for messages...");
    await Task.Delay(-1);
});

app.Run();
await consumerTask;
