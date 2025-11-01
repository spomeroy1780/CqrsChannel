using CqrsExpress.Contracts;
using ConsoleExMediator.Domain.Repositories;
using ConsoleExMediator.Domain.Services;

namespace ConsoleExMediator.Application.Commands;

/// <summary>
/// Command for canceling an order
/// Single Responsibility: Represents the intent to cancel an order
/// </summary>
public sealed record CancelOrderCommand(
    int OrderId,
    string Reason
) : ICommand;

/// <summary>
/// Handler for CancelOrderCommand
/// Single Responsibility: Orchestrates order cancellation process
/// Dependency Inversion: Depends on abstractions
/// </summary>
public sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInventoryService _inventoryService;
    private readonly INotificationService _notificationService;

    public CancelOrderCommandHandler(
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

    public async ValueTask Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = _orderRepository.GetById(command.OrderId);
        if (order == null)
            throw new KeyNotFoundException($"Order {command.OrderId} not found");

        // Use domain logic for business rules validation
        order.Cancel(command.Reason);

        // Release inventory
        _inventoryService.ReleaseItems(order.Items);

        // Persist changes
        _orderRepository.Update(order);

        // Send notification
        var customer = await _customerRepository.GetById(order.CustomerId);
        if (customer != null)
        {
            await _notificationService.SendCancellationNotificationAsync(
                customer.Email,
                order.Id,
                command.Reason,
                cancellationToken
            );
        }

        Console.WriteLine($"  → Order #{command.OrderId} cancelled");
        Console.WriteLine($"  → Reason: {command.Reason}");
        Console.WriteLine($"  → Inventory released for {order.Items.Count} items");
    }
}
