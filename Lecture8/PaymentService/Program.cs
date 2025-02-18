using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Contracts; // Import shared message contract

var builder = WebApplication.CreateBuilder(args);

//  Configure MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        // Declare the exchange as durable
        cfg.Message<PaymentCompleted>(config =>
        {
            config.SetEntityName("payment_exchange"); // Exchange name
        });

        cfg.Publish<PaymentCompleted>(p =>
        {
            p.ExchangeType = "fanout"; // Ensure fanout type
            p.Durable = true; // Ensure it's durable
        });
    });
});

// Configure In-Memory Database for Payment Service
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseInMemoryDatabase("PaymentDB"));

builder.Services.AddHttpClient(); // To communicate with Catalog Service

var app = builder.Build();

app.MapGet("/api/payments", async (PaymentDbContext db) => await db.Payments.ToListAsync());

//  Payment Processing API
app.MapPost("/api/payments", async (Payment payment, PaymentDbContext db, IHttpClientFactory httpClientFactory, IPublishEndpoint publishEndpoint) =>
{
    Console.WriteLine($"[Payment Service] Processing payment for Order {payment.OrderId} with book id  {payment.BookId}...");

    var client = httpClientFactory.CreateClient();

    // Fetch latest book details from Catalog Service via API Gateway
    var book = await client.GetFromJsonAsync<Book>($"http://localhost:5005/catalog/books/{payment.BookId}");
    if (book == null)
        return Results.NotFound("Book not found!");

    // Ensure the amount matches the latest book price
    if (payment.Amount != book.Price * payment.Quantity)
        return Results.BadRequest("Price mismatch detected!");

    // Store payment record in the database
    db.Payments.Add(payment);
    await db.SaveChangesAsync();

    // Simulate payment processing delay
    await Task.Delay(3000);

    //  Publish Payment Completed event via MassTransit
    await publishEndpoint.Publish(new PaymentCompleted(payment.OrderId, payment.Amount));

    Console.WriteLine($"[Payment Service] Payment completed for Order {payment.OrderId}, event published to Exchange.");

    return Results.Accepted();
});

app.Run();

//  Define Database Context
public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }
    public DbSet<Payment> Payments { get; set; } = null!;
}

//  Define Models
public record Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int BookId { get; set; } // Now storing BookId for reference
    public int Quantity { get; set; } // Needed to validate the correct payment amount
    public decimal Amount { get; set; }
}

public record Book(int Id, string Title, decimal Price);
