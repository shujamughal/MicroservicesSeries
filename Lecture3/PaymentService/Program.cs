using RabbitMQ.Client;
using System.Text;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/api/payments", async (Payment payment) =>
{
    try
    {
        Console.WriteLine($"[Payment Service] Processing payment for Order {payment.OrderId}...");

        // Simulate processing time (e.g., credit card processing delay)
        await Task.Delay(10000);

        var factory = new ConnectionFactory() { HostName = "localhost" };
        await using var connection = await factory.CreateConnectionAsync("payment-service-connection");
        await using var channel = await connection.CreateChannelAsync();

        // Declare the queue (direct queue binding, no exchange)
        await channel.QueueDeclareAsync("payment_completed", durable: false, exclusive: false, autoDelete: false, arguments: null);

        string message = $"Order {payment.OrderId} completed with amount {payment.Amount}";
        var body = Encoding.UTF8.GetBytes(message);

        // Create BasicProperties using new operator (required in RabbitMQ.Client 7.0.0)
        var properties = new BasicProperties();

        // Publish the message asynchronously to the "payment_completed" queue
        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: "payment_completed",
            mandatory: false,
            basicProperties: properties,
            body: body
        );

        Console.WriteLine($"[Payment Service] Payment completed for Order {payment.OrderId}, published to RabbitMQ.");

        return Results.Accepted();  // Return 202 Accepted
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing payment: {ex.Message}");
        return Results.Problem("An error occurred while processing the payment.");
    }
});

app.Run();

public record Payment(int OrderId, decimal Amount);
