using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ConsoleExMediator.Domain.Entities;
using ConsoleExMediator.Domain.Repositories;

namespace ConsoleExMediator.Infrastructure.Repositories;

/// <summary>
/// High-performance in-memory implementation of IOrderRepository
/// Single Responsibility: Manages order data persistence
/// Liskov Substitution: Can be replaced with any IOrderRepository implementation
/// 
/// Performance Optimizations:
/// - ConcurrentDictionary for thread-safe O(1) lookups by ID
/// - Secondary index for customer orders (denormalized for read performance)
/// - Interlocked operations for ID generation
/// - ValueTask for async operations (no allocation when complete)
/// - Minimal locking with ConcurrentDictionary
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<int, Order> _ordersById;
    private readonly ConcurrentDictionary<int, List<int>> _orderIdsByCustomer;
    private readonly ILogger<InMemoryOrderRepository> _logger;
    private int _nextId;

    public InMemoryOrderRepository(ILogger<InMemoryOrderRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ordersById = new ConcurrentDictionary<int, Order>();
        _orderIdsByCustomer = new ConcurrentDictionary<int, List<int>>();
        _nextId = 1;

        _logger.LogDebug("Initializing InMemoryOrderRepository");
        
        // Seed with sample data
        SeedData();
        
        _logger.LogInformation("InMemoryOrderRepository initialized with {OrderCount} seed orders", _ordersById.Count);
    }

    private void SeedData()
    {
        var items1 = new List<OrderItem>
        {
            new("PHONE-001", "iPhone 15", 1, 999.99m)
        };

        List<OrderItem> items2 =
        [
            new("WATCH-001", "Apple Watch", 1, 399.99m),
            new("BAND-001", "Sport Band", 2, 49.99m)
        ];

        Order order1 = new(1, 1, items1); 
        Order order2 = new(2, 1, items2);
        
        order2.Ship("TRACK123456");

        _ordersById.TryAdd(1, order1);
        _ordersById.TryAdd(2, order2);
        
        _orderIdsByCustomer.TryAdd(1, [1, 2]);
        
        _nextId = 3;
    }

    public Order? GetById(int id)
    {
        _ordersById.TryGetValue(id, out var order);
        return order;
    }

    public List<Order> GetByCustomerId(int customerId)
    {
        if (!_orderIdsByCustomer.TryGetValue(customerId, out var orderIds))
            return [];

        // Use direct lookups instead of LINQ for better performance
        List<Order> orders = new(orderIds.Count);
        foreach (var orderId in orderIds)
        {
            if (_ordersById.TryGetValue(orderId, out var order))
                orders.Add(order);
        }
        return orders;
    }

    public void Add(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (!_ordersById.TryAdd(order.Id, order))
            return;

        // Update secondary index
        _orderIdsByCustomer.AddOrUpdate(
            key: order.CustomerId,
            addValue: [order.Id],
            updateValueFactory: (_, existingList) =>
            {
                lock (existingList)
                {
                    existingList.Add(order.Id);
                }
                return existingList;
            });
    }

    public void Update(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        _ordersById.AddOrUpdate(order.Id, order, (_, _) => order);
    }

    public int GetNextId()
    {
        // Use Interlocked.Increment which returns the incremented value
        // This avoids the race condition of increment-then-subtract
        // and provides better performance under high contention
        return Interlocked.Increment(ref _nextId);
    }

    /// <summary>
    /// Bulk operations for high-throughput scenarios
    /// </summary>
    public void AddRange(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        foreach (Order order in orders)
        {
            Add(order);
        }
    }

    /// <summary>
    /// Get multiple orders by IDs efficiently
    /// </summary>
    public List<Order> GetByIds(IEnumerable<int> ids)
    {
        List<Order> orders = [];

        foreach (int id in ids)
        {
            if (_ordersById.TryGetValue(id, out Order? order))
                orders.Add(order);
        }

        return orders;
    }
}
