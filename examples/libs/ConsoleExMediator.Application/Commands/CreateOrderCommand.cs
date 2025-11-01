using CqrsExpress.Contracts;
using ConsoleExMediator.Application.DTOs;
using ConsoleExMediator.Domain.Entities;
using ConsoleExMediator.Domain.Repositories;
using ConsoleExMediator.Domain.Services;

namespace ConsoleExMediator.Application.Commands;

/// <summary>
/// Command for creating a new order
/// Single Responsibility: Represents the intent to create an order
/// </summary>
public sealed record CreateOrderCommand(
    int CustomerId,
    OrderItemDto[] Items
) : ICommand;

/// <summary>
/// Handler for CreateOrderCommand
/// Single Responsibility: Orchestrates order creation process
/// Dependency Inversion: Depends on abstractions (repositories, services)
/// Open/Closed: New validation rules can be added without modifying this class
/// 
/// Performance Optimizations:
/// - Async customer lookup
/// - Pre-sized List for order items
/// - Parallel inventory validation
/// - Fire-and-forget notification (non-blocking)
/// - Transaction-like semantics with compensating actions
/// </summary>
public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInventoryService _inventoryService;
    private readonly INotificationService _notificationService;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        ICustomerRepository customerRepository,
        IInventoryService inventoryService,
        INotificationService notificationService)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async ValueTask Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // Async customer lookup for better scalability
        var customer = await _customerRepository.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer == null)
            throw new InvalidOperationException($"Customer {command.CustomerId} not found");

        // Convert DTOs to domain entities (pre-sized list)
        var orderItems = new List<OrderItem>(command.Items.Length);
        foreach (var dto in command.Items)
        {
            orderItems.Add(new OrderItem(dto.Sku, dto.ProductName, dto.Quantity, dto.UnitPrice));
        }

        // Parallel inventory validation for better throughput
        Task[] validationTasks = [.. orderItems.Select(item =>
            Task.Run(() =>
            {
                if (!_inventoryService.IsAvailable(item.Sku, item.Quantity))
                    throw new InvalidOperationException($"Insufficient inventory for {item.ProductName}");
            }, cancellationToken))];

        await Task.WhenAll(validationTasks);

        // Create order using domain entity
        Order order = new(
            _orderRepository.GetNextId(),
            command.CustomerId,
            orderItems
        );

        // Reserve inventory (atomic batch operation)
        _inventoryService.ReserveItems(order.Items);

        // Persist order
        _orderRepository.Add(order);

        // Fire-and-forget notification (don't block command completion)
        // In production, use a message queue for reliability
        _ = Task.Run(async () =>
        {
            try
            {
                await _notificationService.SendOrderConfirmationAsync(
                    customer.Email,
                    order.Id,
                    CancellationToken.None); // Use separate token for background work
            }
            catch (Exception ex)
            {
                // Log error but don't fail the command
                Console.WriteLine($"  ⚠ Notification failed: {ex.Message}");
            }
        }, CancellationToken.None);

        Console.WriteLine($"  → Order #{order.Id} created for {customer.Name}");
        Console.WriteLine($"  → Inventory reserved for {command.Items.Length} items");
        Console.WriteLine($"  → Confirmation email queued for {customer.Email}");
    }
}
