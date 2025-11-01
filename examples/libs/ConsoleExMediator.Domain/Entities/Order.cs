namespace ConsoleExMediator.Domain.Entities;

/// <summary>
/// Domain entity representing an order with business logic
/// Single Responsibility: Manages order lifecycle and business rules
/// </summary>
public sealed class Order
{
    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? TrackingNumber { get; private set; }
    public DateTime? ShippedDate { get; private set; }
    public string? CancellationReason { get; private set; }
    
    private readonly List<OrderItem> _items = new();
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    // For EF or serialization
    private Order() { }

    public Order(int id, int customerId, List<OrderItem> items)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("Order must have at least one item", nameof(items));

        Id = id;
        CustomerId = customerId;
        OrderDate = DateTime.UtcNow;
        Status = OrderStatus.Pending;
        _items = items;
    }

    public decimal GetTotalAmount() => _items.Sum(i => i.GetTotalPrice());

    public int GetItemCount() => _items.Count;

    public bool CanBeShipped() => Status == OrderStatus.Pending;

    public bool CanBeCancelled() => Status != OrderStatus.Shipped && Status != OrderStatus.Delivered;

    public void Ship(string trackingNumber)
    {
        if (!CanBeShipped())
            throw new InvalidOperationException($"Cannot ship order in {Status} status");

        if (string.IsNullOrWhiteSpace(trackingNumber))
            throw new ArgumentException("Tracking number is required", nameof(trackingNumber));

        Status = OrderStatus.Shipped;
        TrackingNumber = trackingNumber;
        ShippedDate = DateTime.UtcNow;
    }

    public void Cancel(string reason)
    {
        if (!CanBeCancelled())
            throw new InvalidOperationException($"Cannot cancel order - already {Status}");

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
    }
}

public enum OrderStatus
{
    Pending,
    Shipped,
    Delivered,
    Cancelled
}
