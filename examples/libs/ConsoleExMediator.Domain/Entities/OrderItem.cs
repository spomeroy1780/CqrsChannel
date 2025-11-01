namespace ConsoleExMediator.Domain.Entities;

/// <summary>
/// Value object representing an order line item
/// Single Responsibility: Encapsulates order item data and calculations
/// </summary>
public sealed class OrderItem
{
    public string Sku { get; private set; }
    public string ProductName { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    // For serialization
    private OrderItem() 
    {
        Sku = string.Empty;
        ProductName = string.Empty;
    }

    public OrderItem(string sku, string productName, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required", nameof(sku));
        
        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name is required", nameof(productName));
        
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        
        if (unitPrice <= 0)
            throw new ArgumentException("Unit price must be positive", nameof(unitPrice));

        Sku = sku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public decimal GetTotalPrice() => Quantity * UnitPrice;
}
