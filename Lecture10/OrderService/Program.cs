using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Contracts;
using Polly.Extensions.Http;
using Polly;
using MassTransit.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure MassTransit with RabbitMQ
// ✅ Configure MassTransit with RabbitMQ (Updated to Use Error Queue Instead of DLX)
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentCompletedConsumer>();
    x.AddConsumer<BookPriceUpdatedConsumer>();
    x.AddConsumer<ErrorQueueConsumer>(); // 🔹 New consumer to process error queue messages

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        // ✅ Configure Main Queue
        cfg.ReceiveEndpoint("order_service_queue", e =>
        {
            e.Bind("book_price_exchange", c =>
            {
                c.ExchangeType = "fanout";
                c.Durable = true;
            });

            // ✅ Use MassTransit Built-in Error Queue Instead of DLX
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5))); // Retry 3 times before failing
            e.ConfigureConsumer<BookPriceUpdatedConsumer>(context);
        });

        // ✅ Process Messages from Error Queue Instead of DLQ
        cfg.ReceiveEndpoint("order_service_queue_error", e =>
        {
            e.ConfigureConsumer<ErrorQueueConsumer>(context); // 🔹 New consumer for error messages
        });
    });
});
// ✅ Configure In-Memory Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("OrderDB"));

// ✅ Configure Polly Retry Policy for HTTP Clients
builder.Services.AddHttpClient("CatalogClient", client =>
{
client.BaseAddress = new Uri("http://localhost:5005/catalog/");
})
.AddPolicyHandler(GetRetryPolicy());

builder.Services.AddHttpClient("PaymentClient", client =>
{
client.BaseAddress = new Uri("http://localhost:5005/payments");
})
.AddPolicyHandler(GetRetryPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
return HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

builder.Services.AddTransient<CatalogServiceClient>();

var app = builder.Build();

app.MapGet("/api/orders", async (OrderDbContext db) => await db.Orders.ToListAsync());

// ✅ API Endpoint: Create Order
app.MapPost("/api/orders", async (Order order, OrderDbContext db, CatalogServiceClient catalogClient, IHttpClientFactory httpClientFactory) =>
{
var client = httpClientFactory.CreateClient("CatalogClient");

Book? book = null;

try
{
// ✅ Fetch latest book details from Catalog Service via API Gateway
book = await client.GetFromJsonAsync<Book>($"books/{order.BookId}");

if (book == null)
return Results.NotFound("Book not found!");
}
catch (HttpRequestException ex)
{
Console.WriteLine($"[Order Service] Catalog Service is unavailable: {ex.Message}");
return Results.Problem("Catalog Service is currently unavailable. Please try again later.");
}

// ✅ Store BookTitle and BookPrice at the time of order creation
order.BookTitle = book.Title;
order.BookPrice = book.Price;
order.Status = "Pending";

db.Orders.Add(order);
await db.SaveChangesAsync();

// ✅ Fire-and-Forget Payment Request (Retries in Background)
var paymentClient = httpClientFactory.CreateClient("PaymentClient");
_ = Task.Run(async () =>
{
await Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
    .ExecuteAsync(async () =>
{
await paymentClient.PostAsJsonAsync("", new Payment(order.Id, order.BookId, order.Quantity, order.Amount));
Console.WriteLine($"[Order Service] Payment request sent for Order {order.Id}.");
});
});

return Results.Accepted($"/api/orders/{order.Id}", order);
});

app.Run();


// ✅ Dead Letter Queue Consumer (Now Correctly Processes DLQ Messages)
// ✅ Consumer to Handle Messages That Failed All Retry Attempts (Error Queue)
public class ErrorQueueConsumer : IConsumer<Fault<BookPriceUpdated>>
{
    public async Task Consume(ConsumeContext<Fault<BookPriceUpdated>> context)
    {
        Console.WriteLine($"[ERROR QUEUE] Processing failed message: {context.Message.Message.BookId} - Error: {context.Message.Exceptions[0].Message}");
    }
}


// ✅ Book Price Updated Consumer (Simulated Failure for DLQ Testing)
public class BookPriceUpdatedConsumer : IConsumer<BookPriceUpdated>
{
    private readonly OrderDbContext _db;

    public BookPriceUpdatedConsumer(OrderDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<BookPriceUpdated> context)
    {
        Console.WriteLine($"[Order Service] Simulating failure for BookId {context.Message.BookId}");

        throw new Exception("Simulated Processing Failure");
    }
}

// ✅ Payment Completed Consumer
public class PaymentCompletedConsumer : IConsumer<PaymentCompleted>
{
    private readonly OrderDbContext _db;

    public PaymentCompletedConsumer(OrderDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<PaymentCompleted> context)
    {
        var message = context.Message;
        Console.WriteLine($"[Order Service] Received payment confirmation for Order {message.OrderId}.");

        var order = await _db.Orders.FindAsync(message.OrderId);
        if (order != null)
        {
            order.Status = "Paid";
            await _db.SaveChangesAsync();
            Console.WriteLine($"[Order Service] Order {order.Id} marked as Paid.");
        }
    }
}

// ✅ Models
public record Order
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public decimal BookPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Amount => BookPrice * Quantity;
    public string Status { get; set; } = "Pending";
}

public record Payment(int OrderId, int BookId, int Quantity, decimal Amount);
public record Book(int Id, string Title, decimal Price); // ✅ Added Book Model

// ✅ Database Context
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
    public DbSet<Order> Orders { get; set; } = null!;
}


// Catalog Service Client
public class CatalogServiceClient
{
    private readonly HttpClient _httpClient;
    public CatalogServiceClient(IHttpClientFactory httpClientFactory) => _httpClient = httpClientFactory.CreateClient();

    public async Task<bool> CheckBookExists(int bookId)
    {
        var response = await _httpClient.GetAsync($"http://localhost:5005/catalog/books/{bookId}");
        return response.IsSuccessStatusCode;
    }
}
