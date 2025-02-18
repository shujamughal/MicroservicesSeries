using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Contracts;

var builder = WebApplication.CreateBuilder(args);

// Configure MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentCompletedConsumer>();
    x.AddConsumer<BookPriceUpdatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        cfg.ReceiveEndpoint("order_service_queue", e =>
        {
            e.Bind("book_price_exchange", c =>
            {
                c.ExchangeType = "fanout";
                c.Durable = true; // Ensure persistence
            });

            e.ConfigureConsumer<BookPriceUpdatedConsumer>(context);
        });
        cfg.ReceiveEndpoint("order-queue", e =>
        {
            e.Bind("payment_exchange", c =>
            {
                c.ExchangeType = "fanout";
                c.Durable = true;
            });
            e.ConfigureConsumer<PaymentCompletedConsumer>(context);
        });
    });
});

// Configure In-Memory Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("OrderDB"));

// Register HTTP Client & CatalogServiceClient
builder.Services.AddHttpClient();
builder.Services.AddTransient<CatalogServiceClient>();

var app = builder.Build();

app.MapGet("/api/orders", async (OrderDbContext db) => await db.Orders.ToListAsync());

// API Endpoint: Create Order
app.MapPost("/api/orders", async (Order order, OrderDbContext db, CatalogServiceClient catalogClient, IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient();

    // Fetch latest book details from Catalog Service via API Gateway
    var book = await client.GetFromJsonAsync<Book>($"http://localhost:5005/catalog/books/{order.BookId}");
    if (book == null)
        return Results.NotFound("Book not found!");

    // Store BookTitle and BookPrice at the time of order creation
    order.BookTitle = book.Title;
    order.BookPrice = book.Price;
    order.Status = "Pending";

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    // Send payment request via API Gateway
    _ = client.PostAsJsonAsync("http://localhost:5005/payments", new Payment(order.Id, order.BookId, order.Quantity, order.Amount));

    Console.WriteLine($"[Order Service] Payment request sent for Order {order.Id}.");

    return Results.Accepted($"/api/orders/{order.Id}", order);
});

app.Run();

public class BookPriceUpdatedConsumer : IConsumer<BookPriceUpdated>
{
    private readonly OrderDbContext _db;

    public BookPriceUpdatedConsumer(OrderDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<BookPriceUpdated> context)
    {
        var message = context.Message;
        Console.WriteLine($"[Order Service] Received updated book price for BookId {message.BookId}: {message.NewPrice}");

        var orders = _db.Orders.Where(o => o.BookId == message.BookId).ToList();
        foreach (var order in orders)
        {
            order.BookPrice = message.NewPrice;
        }

        await _db.SaveChangesAsync();
        Console.WriteLine($"[Order Service] Updated {orders.Count} orders with new book price.");
    }
}


// MassTransit Consumer to Process Payment Completed Event
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

// Models
public record Order
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty; // Now storing Book Title
    public decimal BookPrice { get; set; } // Now storing Book Price
    public int Quantity { get; set; }
    public decimal Amount => BookPrice * Quantity; // Calculating Amount based on stored price
    public string Status { get; set; } = "Pending";
}

public record Payment(int OrderId, int BookId, int Quantity, decimal Amount);
public record Book(int Id, string Title, decimal Price); // Added Book Model

// Database Context
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
