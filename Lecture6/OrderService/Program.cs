using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Contracts;
var builder = WebApplication.CreateBuilder(args);

//  Configure MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentCompletedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        cfg.ReceiveEndpoint("order-queue", e =>
        {
            e.Bind("payment_exchange", c =>
            {
                c.ExchangeType = "fanout";
                c.Durable = true; // Ensure durability matches
            });
            e.ConfigureConsumer<PaymentCompletedConsumer>(context);
        });
    });
});

//  Configure In-Memory Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("OrderDB"));

//  Register HTTP Client & CatalogServiceClient
builder.Services.AddHttpClient();
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
    _ = client.PostAsJsonAsync("http://localhost:5123/api/payments", new Payment(order.Id, order.Amount));

    Console.WriteLine($"[Order Service] Payment request sent for Order {order.Id}.");

    return Results.Accepted($"/api/orders/{order.Id}", order);
});

app.Run();

//  MassTransit Consumer to Process Payment Completed Event
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

//  Models
public record Order
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
}
public record Payment(int OrderId, decimal Amount);


//  Database Context
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
    public DbSet<Order> Orders { get; set; } = null!;
}

//  Catalog Service Client
public class CatalogServiceClient
{
    private readonly HttpClient _httpClient;
    public CatalogServiceClient(IHttpClientFactory httpClientFactory) => _httpClient = httpClientFactory.CreateClient();

    public async Task<bool> CheckBookExists(int bookId)
    {
        var response = await _httpClient.GetAsync($"http://localhost:5243/api/books/{bookId}");
        return response.IsSuccessStatusCode;
    }
}
