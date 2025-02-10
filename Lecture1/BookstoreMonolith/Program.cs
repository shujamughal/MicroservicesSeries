using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Use In-Memory Database (no SQL Server needed!)
builder.Services.AddDbContext<BookstoreDbContext>(options =>
    options.UseInMemoryDatabase("BookstoreMonolith"));

var app = builder.Build();

app.MapGet("/api/books", async (BookstoreDbContext db) => await db.Books.ToListAsync());

app.MapPost("/api/books", async (Book book, BookstoreDbContext db) =>
{
    db.Books.Add(book);
    await db.SaveChangesAsync();
    return Results.Created($"/api/books/{book.Id}", book);
});

// Seed sample data after application starts
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookstoreDbContext>();
    if (!db.Books.Any())
    {
        db.Books.Add(new Book { Id = 1, Title = "Microservices in .NET", Price = 49.99m });
        db.SaveChanges();
    }
}

app.Run();

// Database Models
public class BookstoreDbContext : DbContext
{
    public BookstoreDbContext(DbContextOptions<BookstoreDbContext> options) : base(options) { }
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