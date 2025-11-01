# CqrsExpress Real-World Example: E-Commerce Order Management

This example demonstrates a complete, real-world implementation of **CqrsExpress** using an e-commerce order management system.

## What This Example Shows

### ✅ Complete CQRS Pattern Implementation
- **Queries** (read operations) for retrieving customer and order data
- **Commands** (write operations) for creating, shipping, and canceling orders
- Proper separation of read and write concerns

### ✅ Dependency Injection with ServiceCollection
- Full Microsoft.Extensions.DependencyInjection setup
- Auto-discovery of handlers using `AddExpressMediatorWithAutoDiscovery()`
- Domain service dependencies (repositories, email service, inventory service)

### ✅ Real-World Business Logic
- Customer management
- Order creation with inventory validation
- Order shipping with tracking numbers
- Order cancellation with inventory release
- Email notifications

### ✅ Best Practices Demonstrated
- Record-based DTOs for immutability
- Proper error handling and validation
- Domain service separation
- Repository pattern
- Business rule enforcement

## Running the Example

```bash
cd examples/consoleExMediator
dotnet run
```

## Code Structure

### Queries (Read Operations)

1. **GetCustomerByIdQuery** - Retrieves customer information
   ```csharp
   var query = new GetCustomerByIdQuery(CustomerId: 1);
   var customer = await mediator.Send<GetCustomerByIdQuery, CustomerDto>(query);
   ```

2. **GetOrderHistoryQuery** - Lists all orders for a customer
   ```csharp
   var query = new GetOrderHistoryQuery(CustomerId: 1);
   var orders = await mediator.Send<GetOrderHistoryQuery, List<OrderSummaryDto>>(query);
   ```

3. **GetOrderDetailsQuery** - Gets detailed information about a specific order
   ```csharp
   var query = new GetOrderDetailsQuery(OrderId: 3);
   var details = await mediator.Send<GetOrderDetailsQuery, OrderDetailsDto>(query);
   ```

### Commands (Write Operations)

1. **CreateOrderCommand** - Creates a new order with inventory validation
   ```csharp
   var command = new CreateOrderCommand(
       CustomerId: 1,
       Items: new[] { new OrderItemDto("LAPTOP-001", "Dell XPS 15", 2, 1299.99m) }
   );
   await mediator.Send(command);
   ```

2. **ShipOrderCommand** - Ships an order and sends tracking notification
   ```csharp
   var command = new ShipOrderCommand(OrderId: 1, TrackingNumber: "1Z999AA1234567890");
   await mediator.Send(command);
   ```

3. **CancelOrderCommand** - Cancels an order and releases inventory
   ```csharp
   var command = new CancelOrderCommand(OrderId: 2, Reason: "Customer requested");
   await mediator.Send(command);
   ```

## Handler Implementation Pattern

### Query Handler Example
```csharp
public class GetCustomerByIdQueryHandler : IQueryHandler<GetCustomerByIdQuery, CustomerDto>
{
    private readonly ICustomerRepository _repository;

    public GetCustomerByIdQueryHandler(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<CustomerDto> Handle(GetCustomerByIdQuery query, CancellationToken cancellationToken)
    {
        var customer = _repository.GetById(query.CustomerId);
        // ... transform to DTO
        return ValueTask.FromResult(dto);
    }
}
```

### Command Handler Example
```csharp
public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;

    public CreateOrderCommandHandler(IOrderRepository orderRepository, IInventoryService inventoryService)
    {
        _orderRepository = orderRepository;
        _inventoryService = inventoryService;
    }

    public async ValueTask Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // Validate and create order
        // Reserve inventory
        // Send notifications
    }
}
```

## Key Features Demonstrated

### 1. Auto-Discovery Setup
```csharp
services.AddExpressMediatorWithAutoDiscovery();
```
Automatically finds and registers all handlers in your assembly and referenced assemblies.

### 2. Dependency Injection
All handlers receive their dependencies through constructor injection:
```csharp
services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
services.AddSingleton<IInventoryService, InventoryService>();
```

### 3. Business Validation
Commands include business rule validation:
- Inventory availability checks
- Order status validation before shipping
- Preventing cancellation of shipped orders

### 4. Error Handling
Proper exception handling for business rules:
```csharp
try
{
    await mediator.Send(cancelOrderCommand);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Failed: {ex.Message}");
}
```

## Expected Output

```
=== CqrsExpress Real-World Example: Order Management System ===

✓ CqrsExpress mediator initialized with auto-discovery

--- Scenario 1: Query Customer Information ---
Customer Found: Sarah Johnson
Email: sarah.johnson@example.com
Member Since: 3/15/2022

--- Scenario 2: Query Order History ---
Found 2 orders:
  Order #1: Pending - $999.99 (1 items)
  Order #2: Shipped - $499.97 (2 items)

--- Scenario 3: Create New Order ---
  → Order #3 created for Sarah Johnson
  → Inventory reserved for 2 items
  → Confirmation email sent to sarah.johnson@example.com
✓ Order created successfully!

[... more scenarios ...]
```

## Learning Points

1. **CQRS Pattern**: Clear separation between read (queries) and write (commands) operations
2. **Mediator Pattern**: Decoupled handler invocation through the ExpressMediator
3. **Dependency Injection**: Proper DI setup with ServiceCollection
4. **Domain-Driven Design**: Business logic encapsulated in handlers
5. **Type Safety**: Strongly-typed queries and commands using records
6. **Async/Await**: Proper asynchronous programming with ValueTask
7. **Error Handling**: Business rule validation and exception handling

## Adapting for Your Project

To use CqrsExpress in your own project:

1. Define your queries and commands as records implementing `IQuery<TResponse>` or `ICommand`
2. Create handlers implementing `IQueryHandler<TQuery, TResponse>` or `ICommandHandler<TCommand>`
3. Register CqrsExpress with `services.AddExpressMediatorWithAutoDiscovery()`
4. Inject `ExpressMediator` into your controllers or services
5. Call `mediator.Send()` to execute queries and commands

That's it! The mediator handles everything else automatically.
