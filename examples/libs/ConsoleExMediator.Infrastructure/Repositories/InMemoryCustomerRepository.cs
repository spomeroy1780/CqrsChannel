using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ConsoleExMediator.Domain.Entities;
using ConsoleExMediator.Domain.Repositories;
using System.Threading.Tasks;

namespace ConsoleExMediator.Infrastructure.Repositories;

/// <summary>
/// High-performance in-memory implementation of ICustomerRepository
/// Single Responsibility: Manages customer data persistence
/// Liskov Substitution: Can be replaced with any ICustomerRepository implementation
/// 
/// Performance Optimizations:
/// - ConcurrentDictionary for thread-safe O(1) lookups
/// - IMemoryCache for frequently accessed customers (cache-aside pattern)
/// - ValueTask for async operations
/// - No LINQ in hot paths
/// </summary>
public sealed class InMemoryCustomerRepository : ICustomerRepository
{
    private readonly ConcurrentDictionary<int, Customer> _customers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryCustomerRepository> _logger;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public InMemoryCustomerRepository(ILogger<InMemoryCustomerRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _customers = new ConcurrentDictionary<int, Customer>();
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000 // Limit cache to 1000 entries
        });
        
        _logger.LogDebug("Initializing InMemoryCustomerRepository with cache (size limit: {CacheSize}, expiration: {Expiration})", 
            1000, _cacheExpiration);
        
        // Seed with sample data
        SeedData();
        
        _logger.LogInformation("InMemoryCustomerRepository initialized with {CustomerCount} seed customers", _customers.Count);
    }

    private void SeedData()
    {
        var customer1 = new Customer(1, "Sarah Johnson", "sarah.johnson@example.com", new DateTime(2022, 3, 15));
        var customer2 = new Customer(2, "Mike Chen", "mike.chen@example.com", new DateTime(2023, 1, 20));
        
        _customers.TryAdd(1, customer1);
        _customers.TryAdd(2, customer2);
    }

    public ValueTask<Customer> GetById(int id, CancellationToken cancellationToken = default)
    {
        // Check cache first (cache-aside pattern)
        string cacheKey = $"customer_{id}";
        if (_cache.TryGetValue(cacheKey, out Customer? cachedCustomer))
        {
            _logger.LogDebug("Cache hit for customer {CustomerId}", id);
            if (cachedCustomer != null)
                return new ValueTask<Customer>(cachedCustomer);
            else
                return default;
        }

        _logger.LogDebug("Cache miss for customer {CustomerId}", id);
        
        // Cache miss - get from storage
        if (_customers.TryGetValue(id, out Customer? customer))
        {
            _logger.LogDebug("Customer {CustomerId} found in storage, adding to cache", id);
            
            // Add to cache with expiration and size
            MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(_cacheExpiration)
                .SetSize(1); // Each entry counts as 1 toward SizeLimit
            
            _cache.Set(cacheKey, customer, cacheEntryOptions);
            return new ValueTask<Customer>(customer);
        }

        _logger.LogWarning("Customer {CustomerId} not found", id);
        return default!;
    }

    public async ValueTask<Customer> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        // Already optimized synchronous call - wrap in completed Task
        Customer customer = await GetById(id, cancellationToken);
        // This is only ok for in-memory - do NOT do this for real async I/O!
        return await Task.FromResult(customer);
    }

    public void Add(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (!_customers.TryAdd(customer.Id, customer))
            return;

        // Warm cache
        string cacheKey = $"customer_{customer.Id}";
        MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_cacheExpiration)
            .SetSize(1);
        
        _cache.Set(cacheKey, customer, cacheEntryOptions);
    }

    public void Update(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        _customers.AddOrUpdate(customer.Id, customer, (_, _) => customer);

        // Invalidate cache
        string cacheKey = $"customer_{customer.Id}";
        _cache.Remove(cacheKey);
    }

    /// <summary>
    /// Bulk operations for high-throughput scenarios
    /// </summary>
    public void AddRange(IEnumerable<Customer> customers)
    {
        ArgumentNullException.ThrowIfNull(customers);

        foreach (Customer customer in customers)
        {
            Add(customer);
        }
    }

    /// <summary>
    /// Get multiple customers by IDs efficiently (batched)
    /// </summary>
    public async ValueTask<List<Customer>> GetByIds(IEnumerable<int> ids)
    {
        List<Customer> customers = [];
        foreach (int id in ids)
        {
            Customer customer = await GetById(id);
            if (customer != null)
                customers.Add(customer);
        }
        return customers;
    }
}
