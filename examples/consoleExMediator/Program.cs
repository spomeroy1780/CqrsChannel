using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CqrsExpress.DependencyInjection;
using CqrsExpress.Core;
using ConsoleExMediator.Domain.Repositories;
using ConsoleExMediator.Domain.Services;
using ConsoleExMediator.Infrastructure.Repositories;
using ConsoleExMediator.Infrastructure.Services;
using ConsoleExMediator.Application.Queries;
using ConsoleExMediator.Application.Commands;
using ConsoleExMediator.Application.DTOs;

// Setup ServiceCollection with proper dependency injection
var services = new ServiceCollection();

// Configure Logging with best practices
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
    
    // Add filtering for fine-grained control
    builder.AddFilter("Microsoft", LogLevel.Warning);
    builder.AddFilter("System", LogLevel.Warning);
    builder.AddFilter("ConsoleExMediator", LogLevel.Debug);
});

// Register CqrsExpress with auto-discovery
// This will automatically find and register all query and command handlers
services.AddExpressMediatorWithAutoDiscoveryAndPreCompilation();

// Register domain repositories (Dependency Inversion Principle)
// High-level modules depend on abstractions, not implementations
services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
services.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();

// Register domain services (Interface Segregation Principle)
// Focused interfaces for specific concerns
services.AddSingleton<IInventoryService, InventoryService>();
services.AddSingleton<INotificationService, NotificationService>();

ServiceProvider serviceProvider = services.BuildServiceProvider();
ExpressMediator mediator = serviceProvider.GetRequiredService<ExpressMediator>();

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Application started successfully");

logger.LogInformation("CqrsExpress mediator initialized");
logger.LogInformation("Domain repositories and services registered");
logger.LogInformation("All handlers auto-discovered and registered");

Console.WriteLine("✓ CqrsExpress mediator initialized");
Console.WriteLine("✓ Domain repositories and services registered");
Console.WriteLine("✓ All handlers auto-discovered and registered\n");

// ============================================================================
// Scenario 1: Query customer information
// ============================================================================
Console.WriteLine("--- Scenario 1: Query Customer Information ---");
var customerQuery = new GetCustomerByIdQuery(CustomerId: 1);
var customer = await mediator.Send<GetCustomerByIdQuery, CustomerDto>(customerQuery);

if (customer != null)
{
    Console.WriteLine($"Customer Found: {customer.Name}");
    Console.WriteLine($"Email: {customer.Email}");
    Console.WriteLine($"Member Since: {customer.MemberSince:d}\n");
}
else
{
    Console.WriteLine("Customer not found.\n");
}

// ============================================================================
// Scenario 2: Query customer order history
// ============================================================================
Console.WriteLine("--- Scenario 2: Query Order History ---");
var orderHistoryQuery = new GetOrderHistoryQuery(CustomerId: 1);
var orders = await mediator.Send<GetOrderHistoryQuery, List<OrderSummaryDto>>(orderHistoryQuery);

Console.WriteLine($"Found {orders?.Count} orders:");
if (orders != null)
{
    foreach (var order in orders)
    {
        Console.WriteLine($"  Order #{order.OrderId}: {order.Status} - ${order.TotalAmount:F2} ({order.ItemCount} items)");
    }
}
Console.WriteLine();

// ============================================================================
// Scenario 3: Create a new order (Command)
// ============================================================================
Console.WriteLine("--- Scenario 3: Create New Order ---");
var createOrderCommand = new CreateOrderCommand(
    CustomerId: 1,
    Items: new[]
    {
        new OrderItemDto("LAPTOP-001", "Dell XPS 15", 2, 1299.99m),
        new OrderItemDto("MOUSE-042", "Logitech MX Master 3", 1, 99.99m)
    }
);

await mediator.Send(createOrderCommand);
Console.WriteLine("✓ Order created successfully!\n");

// ============================================================================
// Scenario 4: Query specific order details
// ============================================================================
Console.WriteLine("--- Scenario 4: Query Order Details ---");
var orderDetailQuery = new GetOrderDetailsQuery(OrderId: 3);
var orderDetails = await mediator.Send<GetOrderDetailsQuery, OrderDetailsDto?>(orderDetailQuery);

if (orderDetails == null)
{
    Console.WriteLine("Order not found.\n");
}
else
{

    Console.WriteLine($"Order #{orderDetails.OrderId}");
    Console.WriteLine($"Status: {orderDetails.Status}");
    Console.WriteLine($"Customer: {orderDetails.CustomerName}");
    Console.WriteLine($"Order Date: {orderDetails.OrderDate:f}");
    Console.WriteLine($"Items:");

    foreach (var item in orderDetails.Items)
    {
        Console.WriteLine($"  - {item.ProductName} (SKU: {item.Sku}) x{item.Quantity} @ ${item.UnitPrice:F2}");
    }
    Console.WriteLine($"Total: ${orderDetails.TotalAmount:F2}\n");
}
// ============================================================================
// Scenario 5: Update order status (Command)
// ============================================================================
Console.WriteLine("--- Scenario 5: Ship Order ---");
var shipOrderCommand = new ShipOrderCommand(OrderId: 1, TrackingNumber: "1Z999AA1234567890");
await mediator.Send(shipOrderCommand);
Console.WriteLine("✓ Order shipped!\n");

// ============================================================================
// Scenario 6: Cancel order (Command with validation)
// ============================================================================
Console.WriteLine("--- Scenario 6: Cancel Order ---");
try
{
    var cancelOrderCommand = new CancelOrderCommand(OrderId: 2, Reason: "Customer requested cancellation");
    await mediator.Send(cancelOrderCommand);
    Console.WriteLine("✓ Order cancelled successfully!\n");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"✗ Failed to cancel order: {ex.Message}\n");
}

Console.WriteLine("=== Example Complete ===");
Console.WriteLine("\nSOLID Principles Demonstrated:");
Console.WriteLine("✓ Single Responsibility - Each class has one clear purpose");
Console.WriteLine("✓ Open/Closed - Extensible without modification");
Console.WriteLine("✓ Liskov Substitution - Implementations are interchangeable");
Console.WriteLine("✓ Interface Segregation - Focused, client-specific interfaces");
Console.WriteLine("✓ Dependency Inversion - Depend on abstractions, not concretions");
