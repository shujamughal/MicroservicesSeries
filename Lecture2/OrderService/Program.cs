using Microsoft.EntityFrameworkCore;
using OrderService;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Configure In-Memory Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("OrderDB"));

// Register HttpClient for external API calls
builder.Services.AddHttpClient<CatalogServiceClient>()
    .AddPolicyHandler(GetRetryPolicy());
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(retryAttempt));
}


var app = builder.Build();

// API Endpoints
app.MapPost("/api/orders", async (Order order, OrderDbContext db, CatalogServiceClient catalogClient) =>
{
    var bookExists = await catalogClient.CheckBookExists(order.BookId);
    if (!bookExists) return Results.NotFound("Book not found!");

    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/api/orders/{order.Id}", order);
});

app.Run();

// Database Context
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
    public DbSet<Order> Orders => Set<Order>();
}

// Order Model
public class Order
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int Quantity { get; set; }
}
