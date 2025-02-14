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

        await Task.Delay(3000); // Simulating async payment processing delay

        var factory = new ConnectionFactory() { HostName = "localhost" };
        await using var connection = await factory.CreateConnectionAsync("payment-service-connection");
        await using var channel = await connection.CreateChannelAsync();

        // Declare an Exchange (Fanout)
        await channel.ExchangeDeclareAsync("payment_exchange", ExchangeType.Fanout);

        // Publish message to Exchange
        string message = $"Order {payment.OrderId} completed with amount {payment.Amount}";
        var body = Encoding.UTF8.GetBytes(message);
        var properties = new BasicProperties();

        await channel.BasicPublishAsync(
            exchange: "payment_exchange",
            routingKey: "",
            mandatory: false,
            basicProperties: properties,
            body: body
        );

        Console.WriteLine($"[Payment Service] Payment completed for Order {payment.OrderId}, message sent to Exchange.");

        return Results.Accepted();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing payment: {ex.Message}");
        return Results.Problem("An error occurred while processing the payment.");
    }
});

app.Run();

public record Payment(int OrderId, decimal Amount);

