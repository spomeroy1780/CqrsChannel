using ConsoleExMediator.Domain.Entities;

namespace ConsoleExMediator.Domain.Repositories;

/// <summary>
/// Repository interface for Order aggregate
/// Dependency Inversion: Depend on abstraction, not implementation
/// Interface Segregation: Focused interface for order operations
/// </summary>
public interface IOrderRepository
{
    Order? GetById(int id);
    List<Order> GetByCustomerId(int customerId);
    void Add(Order order);
    void Update(Order order);
    int GetNextId();
}
