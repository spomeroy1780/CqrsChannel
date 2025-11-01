using CqrsExpress.Contracts;
using ConsoleExMediator.Domain.Repositories;
using ConsoleExMediator.Domain.Services;
using Microsoft.Extensions.Logging;
using ConsoleExMediator.Domain.Entities;

namespace ConsoleExMediator.Application.Commands;

/// <summary>
/// Command for shipping an order
/// Single Responsibility: Represents the intent to ship an order
/// </summary>
public sealed record ShipOrderCommand(
    int OrderId,
    string TrackingNumber
) : ICommand;

/// <summary>
/// Handler for ShipOrderCommand
/// Single Responsibility: Orchestrates order shipping process
/// Dependency Inversion: Depends on abstractions
/// </summary>
public sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ShipOrderCommandHandler> _logger;

    public ShipOrderCommandHandler(
        IOrderRepository orderRepository,
        ICustomerRepository customerRepository,
        INotificationService notificationService,
        ILogger<ShipOrderCommandHandler> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask Handle(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        Order? order = _orderRepository.GetById(command.OrderId);

        if (order == null)
        {
            _logger.LogError("Order {OrderId} not found", command.OrderId);
            return;
        }

        _logger.LogInformation("Shipping order {OrderId} with tracking number {TrackingNumber}", command.OrderId, command.TrackingNumber);

        // Use domain logic for business rules
        order.Ship(command.TrackingNumber);

        _logger.LogInformation("Order {OrderId} marked as shipped", command.OrderId);

        // Persist changes
        _orderRepository.Update(order);

        _logger.LogInformation("Order {OrderId} updated in repository", command.OrderId);

        // Send notification
        Customer customer = await _customerRepository.GetById(order.CustomerId);

        _logger.LogInformation("Retrieved customer {CustomerId} for order {OrderId}", order.CustomerId, command.OrderId);
        
        if (customer != null)
        {
            await _notificationService.SendShippingNotificationAsync(
                customer.Email,
                order.Id,
                command.TrackingNumber,
                cancellationToken
            );

            Console.WriteLine($"  → Order #{command.OrderId} marked as shipped");
            Console.WriteLine($"  → Tracking number: {command.TrackingNumber}");
            Console.WriteLine($"  → Shipping notification sent to {customer.Email}");
        }
    }
}
