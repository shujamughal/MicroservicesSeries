using MassTransit;
using Microsoft.EntityFrameworkCore;
using Contracts;
var builder = WebApplication.CreateBuilder(args);

// Use In-Memory Database (no SQL Server needed!)
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseInMemoryDatabase("CatalogDB"));
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        // Declare the exchange for book price updates
        cfg.Message<BookPriceUpdated>(config =>
        {
            config.SetEntityName("book_price_exchange"); // Exchange name
        });

        cfg.Publish<BookPriceUpdated>(p =>
        {
            p.ExchangeType = "fanout"; // Ensures all services listening will receive the event
            p.Durable = true; // Make sure it persists
        });
    });
});
var app = builder.Build();

app.MapGet("/api/books", async (CatalogDbContext db) => await db.Books.ToListAsync());

// New endpoint to fetch a book by ID
app.MapGet("/api/books/{id}", async (int id, CatalogDbContext db) =>
{
    var book = await db.Books.FindAsync(id);
    return book != null ? Results.Ok(book) : Results.NotFound();
});

app.MapPut("/api/books/{id}", async (int id, Book updatedBook, CatalogDbContext db,
                                    IPublishEndpoint publishEndpoint) =>
{
    var book = await db.Books.FindAsync(id);
    if (book == null) return Results.NotFound();

    book.Title = updatedBook.Title;
    book.Price = updatedBook.Price;
    await db.SaveChangesAsync();
    //  Publish an event to notify Order Service
    await publishEndpoint.Publish(new  BookPriceUpdated(book.Id, book.Price));
    Console.WriteLine($"[Catalog Service] Published BookPriceUpdated event for Book ID {book.Id}.");

    return Results.Ok(book);
});

app.MapPost("/api/books", async (Book book, CatalogDbContext db) =>
{
    db.Books.Add(book);
    await db.SaveChangesAsync();
    return Results.Created($"/api/books/{book.Id}", book);
});

// Seed sample data after application starts
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    if (!db.Books.Any())
    {
        db.Books.Add(new Book { Id = 1, Title = "Microservices in .NET", Price = 49.99m });
        db.SaveChanges();
    }
}

app.Run();

// Database Models
public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }
    public DbSet<Book> Books { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>().HasKey(b => b.Id);
    }
}

public class Book
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public decimal Price { get; set; }
}
