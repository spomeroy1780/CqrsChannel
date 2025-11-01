using CqrsExpress.Contracts;
using ConsoleExMediator.Application.DTOs;
using ConsoleExMediator.Domain.Repositories;
using Microsoft.Extensions.Logging;
using ConsoleExMediator.Domain.Entities;

namespace ConsoleExMediator.Application.Queries;

/// <summary>
/// Query for retrieving customer by ID
/// Single Responsibility: Represents the intent to get customer data
/// </summary>
public sealed record GetCustomerByIdQuery(int CustomerId) : IQuery<CustomerDto>;

/// <summary>
/// Handler for GetCustomerByIdQuery
/// Single Responsibility: Handles customer retrieval logic
/// Dependency Inversion: Depends on ICustomerRepository abstraction
/// </summary>
public sealed class GetCustomerByIdQueryHandler : IQueryHandler<GetCustomerByIdQuery, CustomerDto>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<GetCustomerByIdQueryHandler> _logger;

    public GetCustomerByIdQueryHandler(ICustomerRepository customerRepository, ILogger<GetCustomerByIdQueryHandler> logger)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<CustomerDto> Handle(GetCustomerByIdQuery query, CancellationToken cancellationToken)
    {
        Customer customer = await _customerRepository.GetById(query.CustomerId, cancellationToken);

        _logger.LogDebug("Handling GetCustomerByIdQuery for CustomerId: {CustomerId}", query.CustomerId);

        if (customer == null)
        {
            _logger.LogWarning("Customer {CustomerId} not found", query.CustomerId);
            return default!;
        }

        _logger.LogDebug("Customer {CustomerId} found: {CustomerName}", customer.Id, customer.Name);
        
        CustomerDto dto = new(
            customer.Id,
            customer.Name,
            customer.Email,
            customer.MemberSince
        );

        return dto;
    }
}
