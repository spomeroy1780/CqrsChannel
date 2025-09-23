# CqrsCompiledExpress Quick Start

## All-in-One Setup Options

### Option 1: Auto-Discovery (Easiest - One Line!)
```csharp
// In Program.cs or Startup.cs
services.AddCompiledExpressMediatorWithAutoDiscovery();
```
**What it does:**
- Registers CompiledExpressMediator singleton
- Automatically discovers all handlers in your assembly + referenced assemblies
- Registers startup filter for handler compilation
- Zero configuration required!

### Option 2: Specify Assemblies
```csharp
// In Program.cs or Startup.cs
services.AddCompiledExpressMediatorWithHandlers(
    typeof(Program).Assembly,           // Current assembly
    typeof(MyHandlers).Assembly        // Specific assembly
);
```

### Option 3: Basic Registration (Manual Handler Registration)
```csharp
// In Program.cs or Startup.cs
services.AddCompiledExpressMediator();

// Then manually register your handlers
services.AddTransient<IQueryHandler<GetUserQuery, UserDto>, GetUserHandler>();
services.AddTransient<ICommandHandler<CreateUserCommand>, CreateUserHandler>();
```

## Usage in Controllers/Services

```csharp
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

    [HttpPost]
    public async Task CreateUser(CreateUserCommand command)
    {
        await _mediator.Send(command);
    }
}
```

## Underlying Architecture Foundation

**CqrsCompiledExpress uses compiled expression trees for direct handler execution.**

### What It Actually Uses:
1. **Direct Service Resolution** - Uses `IServiceProvider.GetService<T>()` for handler lookup
2. **Compiled Expression Trees** - `System.Linq.Expressions` for zero-reflection execution
3. **ConcurrentDictionary Caching** - `ConcurrentDictionary<Type, Func<...>>` for handler caches
4. **Direct Method Invocation** - Calls handler methods directly via compiled delegates
5. **Microsoft.Extensions.ObjectPool** - For resource pooling of Task lists
6. **System.Diagnostics** - ActivitySource and Meter for observability
7. **Aggressive Inlining** - `MethodImplOptions.AggressiveInlining` for maximum performance

### Core Pattern:
```
Request → Service Resolution → Compiled Expression Tree → Direct Handler Execution
```

## Performance Features
- **4.09ns** per query (synchronous execution) - 74x faster than MediatR
- **9.17ns** per query (asynchronous execution) - 34x faster than MediatR
- **167,000+ operations/second** under concurrent load (5000 operations)
- **32x more memory efficient** than MediatR (32B vs 1456B per operation)
- **Zero reflection** at runtime (compiled expression trees)
- **Linear scalability** under load with maintained performance characteristics
- **Enterprise observability** built-in (.NET 10 TagList optimizations)
- **Memory management** with automatic cleanup and resource pooling

## Running Benchmarks

To validate the performance claims on your hardware:

```bash
cd tests/CqrsBenchmarks

# Run standard single-operation benchmarks
dotnet run -c Release standard

# Run load testing benchmarks (500, 1000, 2000, 5000 operations)
dotnet run -c Release load

# Run all benchmarks
dotnet run -c Release all
```

**Note:** The "Failed to set up high priority" warning is cosmetic and doesn't affect benchmark accuracy. For maximum precision, run with `sudo dotnet run -c Release load`.

## Recommended Approach
**Use `AddCompiledExpressMediatorWithAutoDiscovery()` for maximum convenience** - it handles everything automatically!