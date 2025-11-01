using ConsoleExMediator.Application.DTOs;
using ConsoleExMediator.Application.Queries;
using ConsoleExMediator.Domain.Repositories;
using ConsoleExMediator.Domain.Services;
using ConsoleExMediator.Infrastructure.Repositories;
using ConsoleExMediator.Infrastructure.Services;
using CqrsExpress.Core;
using CqrsExpress.DependencyInjection; // Add this using directive for the extension method
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();

builder.Services.AddSingleton<IInventoryService, InventoryService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();

builder.Services.AddExpressMediatorWithAutoDiscoveryAndPreCompilation();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/customers/{id}", async (
    int id,
    ExpressMediator mediator,
    CancellationToken cancellationToken) =>
{
    GetCustomerByIdQuery query = new(id);
    CustomerDto? customer = await mediator.Send(query, cancellationToken);
    return customer is not null ? Results.Ok(customer) : Results.NotFound();
});

app.Run();