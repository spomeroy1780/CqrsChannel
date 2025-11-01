using CqrsExpress.Contracts;
using ConsoleExMediator.Application.DTOs;
using ConsoleExMediator.Domain.Repositories;
using ConsoleExMediator.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ConsoleExMediator.Application.Queries;

/// <summary>
/// Query for retrieving order history for a customer
/// Single Responsibility: Represents the intent to get order history
/// </summary>
public sealed record GetOrderHistoryQuery(int CustomerId) : IQuery<List<OrderSummaryDto>>;

/// <summary>
/// Handler for GetOrderHistoryQuery
/// Single Responsibility: Handles order history retrieval and transformation
/// Dependency Inversion: Depends on IOrderRepository abstraction
/// 
/// Performance Optimizations:
/// - Pre-sized List to avoid reallocation
/// - Direct loop instead of LINQ for better performance
/// - ValueTask for zero-allocation async
/// </summary>
public sealed class GetOrderHistoryQueryHandler : IQueryHandler<GetOrderHistoryQuery, List<OrderSummaryDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<GetOrderHistoryQueryHandler> _logger;

    public GetOrderHistoryQueryHandler(IOrderRepository orderRepository, ILogger<GetOrderHistoryQueryHandler> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<List<OrderSummaryDto>> Handle(GetOrderHistoryQuery query, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling GetOrderHistoryQuery for CustomerId: {CustomerId}", query.CustomerId);

        List<Order> orders = _orderRepository.GetByCustomerId(query.CustomerId);
        
        _logger.LogDebug("Found {OrderCount} orders for CustomerId: {CustomerId}", orders.Count, query.CustomerId);

        if (orders.Count == 0)
        {
            _logger.LogInformation("No orders found for CustomerId: {CustomerId}", query.CustomerId);
            return ValueTask.FromResult<List<OrderSummaryDto>>([]);
        }

        _logger.LogDebug("Mapping orders to OrderSummaryDto for CustomerId: {CustomerId}", query.CustomerId);

        // Pre-size the list to avoid reallocation during growth
        List<OrderSummaryDto> summaries = new(orders.Count);
        
        _logger.LogDebug("Initialized OrderSummaryDto list with capacity {Capacity}", orders.Count);

        // Direct loop is faster than LINQ for hot paths
        foreach (Order order in orders)
        {
            summaries.Add(new OrderSummaryDto(
                order.Id,
                order.Status.ToString(),
                order.GetTotalAmount(),
                order.GetItemCount()
            ));
        }

        return ValueTask.FromResult<List<OrderSummaryDto>>(summaries);
    }
}
