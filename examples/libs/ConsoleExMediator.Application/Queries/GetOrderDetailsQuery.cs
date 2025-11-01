using CqrsExpress.Contracts;
using ConsoleExMediator.Application.DTOs;
using ConsoleExMediator.Domain.Repositories;
using Microsoft.Extensions.Logging;
using ConsoleExMediator.Domain.Entities;

namespace ConsoleExMediator.Application.Queries;

/// <summary>
/// Query for retrieving detailed order information
/// Single Responsibility: Represents the intent to get order details
/// </summary>
public sealed record GetOrderDetailsQuery(int OrderId) : IQuery<OrderDetailsDto>;

/// <summary>
/// Handler for GetOrderDetailsQuery
/// Single Responsibility: Handles order detail retrieval and transformation
/// Dependency Inversion: Depends on repository abstractions
/// </summary>
public sealed class GetOrderDetailsQueryHandler : IQueryHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<GetOrderDetailsQueryHandler> _logger;

    public GetOrderDetailsQueryHandler(
        IOrderRepository orderRepository,
        ICustomerRepository customerRepository,
        ILogger<GetOrderDetailsQueryHandler> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<OrderDetailsDto> Handle(GetOrderDetailsQuery query, CancellationToken cancellationToken)
    {
        Order? order = _orderRepository.GetById(query.OrderId);

        _logger.LogDebug("Handling GetOrderDetailsQuery for OrderId: {OrderId}", query.OrderId);

        // Check if the order exists. Return default if not found.
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found", query.OrderId);
            return await ValueTask.FromResult<OrderDetailsDto>(default!);
        }

        _logger.LogDebug("Order {OrderId} found for CustomerId: {CustomerId}", order.Id, order.CustomerId);

        Customer customer = await _customerRepository.GetById(order.CustomerId, cancellationToken);

        _logger.LogDebug("Customer {CustomerId} found: {CustomerName}", customer?.Id, customer?.Name);

        List<OrderItemDto> items = [.. order.Items.Select(i => new OrderItemDto(
            i.Sku,
            i.ProductName,
            i.Quantity,
            i.UnitPrice
        ))];

        _logger.LogDebug("Mapped {ItemCount} order items for OrderId: {OrderId}", items.Count, order.Id);

        OrderDetailsDto dto = new(
            order.Id,
            order.Status.ToString(),
            customer?.Name ?? "Unknown",
            order.OrderDate,
            items,
            order.GetTotalAmount(),
            order.TrackingNumber
        );

        _logger.LogDebug("Order {OrderId} found with {ItemCount} items", order.Id, items.Count);

        return await ValueTask.FromResult<OrderDetailsDto>(dto);
    }
}