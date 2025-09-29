# CQRS Express

Ultra-fast CQRS mediator built with compiled expression trees for direct handler invocation.  
This repo includes a NuGet library (`CqrsExpress`) and benchmarks proving its speed against MediatR and MessagePipe.

## Benchmark Results

### Single Operation Performance
| Method                | Mean     | Allocated | Ratio vs MediatR |
|-----------------------|---------:|----------:|----------------:|
| ExpressDirectSync     |  4.09 ns |     32 B  |       **76x**   |
| ExpressDirectAsync    |  9.17 ns |     32 B  |       **34x**   |
| MessagePipe           | 15.03 ns |    104 B  |       **21x**   |
| ExpressServiceLocator | 262.8 ns |    280 B  |        1.2x     |
| MediatR               | 309.9 ns |   1456 B  |        1.0x     |

### Load Testing Performance (Operations/Second)
| Framework | 500 ops | 1000 ops | 2000 ops | 5000 ops | Memory Efficiency |
|-----------|---------|----------|----------|----------|-------------------|
| **ExpressDirectSync** | **185k/s** | **185k/s** | **181k/s** | **171k/s** | **35x less memory** |
| **ExpressDirectAsync** | **172k/s** | **85k/s** | **83k/s** | **52k/s** | **22x less memory** |
| **ExpressServiceLocator** | **91k/s** | **45k/s** | **24k/s** | **7.1k/s** | **22x less memory** |
| MessagePipe | **84k/s** | **40k/s** | **19k/s** | **6.4k/s** | **11x less memory** |
| MediatR | **5.7k/s** | **2.8k/s** | **1.4k/s** | **492/s** | Baseline |

*(.NET 10, Apple M1 Ultra, macOS Sequoia - Load test with concurrent batch processing)*

**Key Insights:**
- **ExpressDirectSync** maintains **171,000+ operations/second** even at 5000 concurrent operations
- **35x more memory efficient** than MediatR under load (19.55KB vs 687KB for 500 ops)
- **ExpressDirectAsync** delivers 52,000+ ops/sec at high load with excellent memory efficiency
- Performance advantage increases dramatically under heavy load scenarios
- **MediatR drops to just 492 ops/sec** at 5000 concurrent operations vs Express's 171k ops/sec

## Install
```bash
dotnet add package CqrsExpress
```

## Quick Start

```csharp
// In Program.cs or Startup.cs - One line setup with auto-discovery!
services.AddExpressMediatorWithAutoDiscovery();
```

```csharp
// Usage in your controllers
public class UserController : ControllerBase
{
    private readonly ExpressMediator _mediator;

    public UserController(ExpressMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{id}")]
    public async Task<UserDto> GetUser(int id)
    {
        var query = new GetUserQuery(id);
        return await _mediator.Send<UserDto>(query);
    }
}
```

## Key Features

- **Ultra-fast execution**: 4.09ns per query (synchronous), 9.17ns (asynchronous)
- **Exceptional load performance**: 171,000+ operations/second under heavy concurrent load (5000 ops)
- **Zero reflection**: Uses compiled expression trees for direct handler invocation
- **Auto-discovery**: Automatic handler registration across assemblies
- **Enterprise observability**: Built-in ActivitySource and Meter support (.NET 10 optimized)
- **Memory efficient**: 35x less memory allocation than MediatR under load (19.55KB vs 687KB)
- **76x faster than MediatR**: Direct execution without boxing/unboxing overhead
- **Linear scalability**: Maintains performance under extreme concurrent load (347x faster than MediatR at 5000 ops)

## Documentation

For detailed setup options and advanced usage, see [QuickStart Guide](docs/QuickStart.md).
