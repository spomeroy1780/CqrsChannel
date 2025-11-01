namespace ConsoleExMediator.Application.DTOs;

/// <summary>
/// Data Transfer Objects for Order operations
/// Single Responsibility: Transfer order data across boundaries
/// </summary>
public sealed record OrderSummaryDto(
    int OrderId,
    string Status,
    decimal TotalAmount,
    int ItemCount
);

public sealed record OrderDetailsDto(
    int OrderId,
    string Status,
    string CustomerName,
    DateTime OrderDate,
    List<OrderItemDto> Items,
    decimal TotalAmount,
    string? TrackingNumber = null
);

public sealed record OrderItemDto(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice
)
{
    public decimal TotalPrice => Quantity * UnitPrice;
}
