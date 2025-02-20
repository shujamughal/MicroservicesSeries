using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Contracts; //  Import shared message contract

var builder = WebApplication.CreateBuilder(args);

//  Configure MassTransit to listen to Exchange
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentCompletedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        cfg.ReceiveEndpoint("notification-queue", e =>
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


var app = builder.Build();
app.Run();

//  MassTransit Consumer to Process Payment Completed Event
public class PaymentCompletedConsumer : IConsumer<PaymentCompleted>
{
    public async Task Consume(ConsumeContext<PaymentCompleted> context)
    {
        var message = context.Message;
        Console.WriteLine($"[Notification Service] Sending email: Payment for Order {message.OrderId} completed.");
    }
}
