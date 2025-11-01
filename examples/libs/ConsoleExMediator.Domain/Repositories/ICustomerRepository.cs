using ConsoleExMediator.Domain.Entities;

namespace ConsoleExMediator.Domain.Repositories;

/// <summary>
/// Repository interface for Customer aggregate
/// Dependency Inversion: Depend on abstraction, not implementation
/// Interface Segregation: Focused interface for customer operations
/// </summary>
public interface ICustomerRepository
{
    ValueTask<Customer> GetById(int id, CancellationToken cancellationToken = default);
    ValueTask<Customer> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    void Add(Customer customer);
    void Update(Customer customer);
}
