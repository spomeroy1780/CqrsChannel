using System.Collections.Concurrent;
using ConsoleExMediator.Domain.Entities;
using ConsoleExMediator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ConsoleExMediator.Infrastructure.Services;

/// <summary>
/// High-performance implementation of IInventoryService
/// Single Responsibility: Manages inventory operations
/// Open/Closed: Can extend with database persistence without modifying this class
/// 
/// Performance Optimizations:
/// - ConcurrentDictionary eliminates most locking (lock-free reads)
/// - Interlocked operations for atomic updates
/// - Batch operations for reserving multiple items atomically
/// - No allocations in hot path (value checks)
/// - Thread-safe without global locks
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly ConcurrentDictionary<string, int> _inventory;
    private readonly ILogger<InventoryService> _logger;

    // Metrics for monitoring
    private long _reservationCount;
    private long _releaseCount;

    public InventoryService(ILogger<InventoryService> logger)
    {
        _logger = logger;
        _inventory = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["LAPTOP-001"] = 50,
            ["MOUSE-042"] = 100,
            ["PHONE-001"] = 200,
            ["WATCH-001"] = 75,
            ["BAND-001"] = 150
        };
        
        _logger.LogInformation("InventoryService initialized with {SkuCount} SKUs", _inventory.Count);
    }

    public bool IsAvailable(string sku, int quantity)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            _logger.LogWarning("IsAvailable called with null or empty SKU");
            return false;
        }

        if (quantity <= 0)
        {
            _logger.LogWarning("IsAvailable called with invalid quantity {Quantity} for SKU {Sku}", quantity, sku);
            return false;
        }

        // Lock-free read from ConcurrentDictionary
        bool available = _inventory.TryGetValue(sku, out var currentQuantity) && currentQuantity >= quantity;
        
        _logger.LogDebug("Inventory check for SKU {Sku}: Available={Available}, Requested={Quantity}, InStock={InStock}", 
            sku, available, quantity, currentQuantity);
            
        return available;
    }

    public void Reserve(string sku, int quantity)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            _logger.LogError("Reserve called with null or empty SKU");
            return;
        }

        if (quantity <= 0)
        {
            _logger.LogError("Reserve called with invalid quantity {Quantity} for SKU {Sku}", quantity, sku);
            return;
        }

        _logger.LogDebug("Attempting to reserve {Quantity} units of SKU {Sku}", quantity, sku);
        
        // Optimistic concurrency - retry loop with max attempts
        int maxAttempts = 100;
        int attempt = 0;

        while (attempt < maxAttempts)
        {
            attempt++;
            
            if (!_inventory.TryGetValue(sku, out int currentQuantity))
            {
                _logger.LogError("SKU {Sku} not found in inventory", sku);
                return;
            }

            if (currentQuantity < quantity)
            {
                _logger.LogWarning("Insufficient inventory for SKU {Sku}. Available: {CurrentQuantity}, Required: {Quantity}", 
                    sku, currentQuantity, quantity);
                return;
            }

            var newQuantity = currentQuantity - quantity;

            // Atomic compare-and-swap
            if (_inventory.TryUpdate(sku, newQuantity, currentQuantity))
            {
                long newCount = Interlocked.Increment(ref _reservationCount);
                _logger.LogInformation("Reserved {Quantity} units of SKU {Sku}. New inventory: {NewQuantity} (Reservation #{Count})", 
                    quantity, sku, newQuantity, newCount);
                break;
            }
            
            // Retry if another thread modified the value
            if (attempt >= maxAttempts)
            {
                _logger.LogError("Failed to reserve inventory for SKU {Sku} after {Attempts} attempts due to high contention", 
                    sku, maxAttempts);
                return;
            }
        }
    }

    public void Release(string sku, int quantity)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            _logger.LogError("Release called with null or empty SKU");
            return;
        }
        
        if (quantity <= 0)
        {
            _logger.LogError("Release called with invalid quantity {Quantity} for SKU {Sku}", quantity, sku);
            return;
        }
        
        _logger.LogDebug("Releasing {Quantity} units of SKU {Sku}", quantity, sku);

        // Use AddOrUpdate for atomic increment
        _inventory.AddOrUpdate(
            key: sku,
            addValue: quantity, // If key doesn't exist, add with this value
            (_, current) => current + quantity); // If exists, increment

        Interlocked.Increment(ref _releaseCount);
    }

    public void ReserveItems(IEnumerable<OrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        List<OrderItem> itemList = [.. items];

        // Two-phase commit: First check all items, then reserve
        // This prevents partial reservations

        // Phase 1: Validate all items are available
        foreach (OrderItem item in itemList)
        {
            if (!IsAvailable(item.Sku, item.Quantity))
            {
                _logger.LogInformation("Insufficient inventory for {item.ProductName}", item.ProductName);
                return;
            }
        }

        // Phase 2: Reserve all items (compensating transaction on failure)
        List<OrderItem> reservedItems = [];
        try
        {
            foreach (OrderItem item in itemList)
            {
                Reserve(item.Sku, item.Quantity);
                reservedItems.Add(item);
            }
        }
        catch
        {
            // Rollback: Release any items that were successfully reserved
            foreach (var item in reservedItems)
            {
                Release(item.Sku, item.Quantity);
            }
            return;
        }
    }

    public void ReleaseItems(IEnumerable<OrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Parallel release for better throughput
        Parallel.ForEach(items, item =>
        {
            Release(item.Sku, item.Quantity);
        });
    }

    /// <summary>
    /// Get current inventory levels (for monitoring/reporting)
    /// </summary>
    public Dictionary<string, int> GetInventorySnapshot()
    {
        return new Dictionary<string, int>(_inventory);
    }

    /// <summary>
    /// Get metrics for monitoring
    /// </summary>
    public (long Reservations, long Releases) GetMetrics()
    {
        return (Interlocked.Read(ref _reservationCount), Interlocked.Read(ref _releaseCount));
    }
}
