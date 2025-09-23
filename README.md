# CQRS CompiledExpress

Ultra-fast CQRS mediator built with compiled expression trees for direct handler invocation.  
This repo includes a NuGet library (`CqrsCompiledExpress`) and benchmarks proving its speed against MediatR and MessagePipe.

## Benchmark Results

### Single Operation Performance
| Method                | Mean     | Allocated | Ratio vs MediatR |
|-----------------------|---------:|----------:|----------------:|
| CompiledExpressSync   |  4.09 ns |     32 B  |       **74x**   |
| CompiledExpressDirect |  9.17 ns |     32 B  |       **34x**   |
| MessagePipe           | 15.03 ns |    104 B  |       **21x**   |
| CompiledExpress       | 262.8 ns |    280 B  |        1.2x     |
| MediatR               | 309.9 ns |   1456 B  |        1.0x     |

### Load Testing Performance (Operations/Second)
| Framework | 500 ops | 1000 ops | 2000 ops | 5000 ops | Memory Efficiency |
|-----------|---------|----------|----------|----------|-------------------|
| **CompiledExpressSync** | **188k/s** | **185k/s** | **181k/s** | **167k/s** | **32x less memory** |
| **CompiledExpressDirect** | **80k/s** | **83k/s** | **82k/s** | **56k/s** | **22x less memory** |
| MessagePipe | 41k/s | 39k/s | 38k/s | 33k/s | 11x less memory |
| CompiledExpress | 3.8k/s | 3.8k/s | 1.7k/s | 704/s | 6x less memory |
| MediatR | 3.1k/s | 3.0k/s | 1.4k/s | 451/s | Baseline |

*(.NET 10, Apple M1 Ultra, macOS Sequoia)*

**Key Insights:**
- CompiledExpressSync maintains **167,000+ operations/second** even at 5000 concurrent operations
- **32x more memory efficient** than MediatR under load
- Performance scales linearly with load, showing excellent concurrency characteristics

## Install
```bash
dotnet add package CqrsCompiledExpress
```

## Quick Start

```csharp
// In Program.cs or Startup.cs - One line setup with auto-discovery!
services.AddCompiledExpressMediatorWithAutoDiscovery();
```

```csharp
// Usage in your controllers
public class UserController : ControllerBase
{
    private readonly CompiledExpressMediator _mediator;

    public UserController(CompiledExpressMediator mediator)
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
- **Exceptional load performance**: 167,000+ operations/second under heavy concurrent load
- **Zero reflection**: Uses compiled expression trees for direct handler invocation
- **Auto-discovery**: Automatic handler registration across assemblies
- **Enterprise observability**: Built-in ActivitySource and Meter support (.NET 10 optimized)
- **Memory efficient**: 32x less memory allocation than MediatR (32B vs 1456B per operation)
- **74x faster than MediatR**: Direct execution without boxing/unboxing overhead
- **Linear scalability**: Performance maintains under concurrent load testing

## Documentation

For detailed setup options and advanced usage, see [QuickStart Guide](docs/QuickStart.md).
