using ConsoleExMediator.Domain.Entities;

namespace ConsoleExMediator.Domain.Services;

/// <summary>
/// Domain service for inventory management
/// Interface Segregation: Clear contract for inventory operations
/// Dependency Inversion: Abstracts inventory logic
/// </summary>
public interface IInventoryService
{
    bool IsAvailable(string sku, int quantity);
    void Reserve(string sku, int quantity);
    void Release(string sku, int quantity);
    void ReserveItems(IEnumerable<OrderItem> items);
    void ReleaseItems(IEnumerable<OrderItem> items);
}
