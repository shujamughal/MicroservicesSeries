using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Configure In-Memory Database for orders
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("OrderDB"));

// Register HttpClient for external API calls (to Payment Service & Catalog Service)
builder.Services.AddHttpClient();

// Register CatalogServiceClient for order validation via Catalog Service
builder.Services.AddTransient<CatalogServiceClient>();

var app = builder.Build();

// API Endpoint: Create Order
app.MapPost("/api/orders", async (Order order, OrderDbContext db, CatalogServiceClient catalogClient, IHttpClientFactory httpClientFactory) =>
{
    bool bookExists = await catalogClient.CheckBookExists(order.BookId);
    if (!bookExists)
        return Results.NotFound("Book not found!");

    order.Status = "Pending";
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var client = httpClientFactory.CreateClient();
    _= client.PostAsJsonAsync("http://localhost:5123/api/payments", new Payment(order.Id, order.Amount));

    Console.WriteLine($"[Order Service] Payment request sent for Order {order.Id}.");

    return Results.Accepted($"/api/orders/{order.Id}", order);
});

// Start a background task to consume payment confirmations from RabbitMQ
var consumerTask  = Task.Run(async () => 
{
    var factory = new ConnectionFactory() { HostName = "localhost" };
    await using var connection = await factory.CreateConnectionAsync("order-service-connection");
    await using var channel = await connection.CreateChannelAsync();

    await channel.QueueDeclareAsync("payment_completed", durable: false, exclusive: false, autoDelete: false, arguments: null);

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += async (model, ea) =>
    {
        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        Console.WriteLine($"[Order Service] Received payment confirmation: {message}");

        int orderId = CatalogServiceClient.ExtractOrderId(message);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var order = await db.Orders.FindAsync(orderId);
        if (order != null)
        {
            order.Status = "Paid";
            await db.SaveChangesAsync();
            Console.WriteLine($"[Order Service] Order {order.Id} marked as Paid.");
        }
    };

    await channel.BasicConsumeAsync(queue: "payment_completed", autoAck: true, consumer: consumer);
    Console.WriteLine("[Order Service] Consumer is running. Waiting for messages...");
    await Task.Delay(-1);
});

app.Run();
await consumerTask;

// 🟢 Move class and record definitions to the END of the file

// Models and DbContext
public record Order
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
}

public record Payment(int OrderId, decimal Amount);

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
    public DbSet<Order> Orders { get; set; } = null!;
}

public class CatalogServiceClient
{
    private readonly HttpClient _httpClient;
    public CatalogServiceClient(IHttpClientFactory httpClientFactory) => _httpClient = httpClientFactory.CreateClient();

    public async Task<bool> CheckBookExists(int bookId)
    {
        var response = await _httpClient.GetAsync($"http://localhost:5243/api/books/{bookId}");
        return response.IsSuccessStatusCode;
    }

   public static int ExtractOrderId(string message)
    {
        var parts = message.Split(" ");
        foreach (var part in parts)
        {
            if (int.TryParse(part, out int orderId))
            {
                return orderId; // Return the first valid integer found in the message
            }
        }
        return 0; // Default to 0 if parsing fails
    }

}

