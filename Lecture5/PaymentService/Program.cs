using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Contracts; //  Import shared message contract

var builder = WebApplication.CreateBuilder(args);

//  Configure MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost"); // RabbitMQ connection
    });
});

var app = builder.Build();

app.MapPost("/api/payments", async (Payment payment, IPublishEndpoint publishEndpoint) =>
{
    Console.WriteLine($"[Payment Service] Processing payment for Order {payment.OrderId}...");

    await Task.Delay(3000); // Simulate payment processing delay

    //  Publish Payment Completed event via MassTransit
    await publishEndpoint.Publish(new PaymentCompleted(payment.OrderId, payment.Amount));

    Console.WriteLine($"[Payment Service] Payment completed for Order {payment.OrderId}, event published.");

    return Results.Accepted();
});

app.Run();

// Define local request model
public record Payment(int OrderId, decimal Amount);
