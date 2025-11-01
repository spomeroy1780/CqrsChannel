using CqrsExpress.Contracts;
using CqrsExpress.Core.Aot;
using CqrsExpress.DependencyInjection;
using CqrsExpress.Pipelines;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Register AOT-compatible mediator with pipelines
builder.Services.AddAotExpressMediatorWithPipelines(
    registry =>
    {
        // Register all handlers explicitly (required for AOT)
        registry.RegisterQuery<GetUserQuery, UserDto>();
        registry.RegisterQuery<GetAllUsersQuery, List<UserDto>>();
        registry.RegisterCommand<CreateUserCommand>();
        registry.RegisterEvent<UserCreatedEvent>(
            static (handler, @event, cancellationToken) => handler.Handle(@event, cancellationToken)
        );
    },
    options =>
    {
        // Add global pipelines
        options.AddGlobalPipeline<LoggingPipeline>();
        options.AddGlobalPipeline<TimingPipeline>();
    });

// Register handlers as singletons (best for AOT)
builder.Services.AddSingleton<IQueryHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
builder.Services.AddSingleton<IQueryHandler<GetAllUsersQuery, List<UserDto>>, GetAllUsersQueryHandler>();
builder.Services.AddSingleton<ICommandHandler<CreateUserCommand>, CreateUserCommandHandler>();
builder.Services.AddSingleton<IEventHandler<UserCreatedEvent>, UserCreatedEventHandler>();

// Register pipelines
builder.Services.AddSingleton<LoggingPipeline>();
builder.Services.AddSingleton<TimingPipeline>();

// Register domain services
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();

var app = builder.Build();
app.UseHttpsRedirection();
// Define endpoints
app.MapGet("/users/{id}", async (int id, AotExpressMediator mediator, CancellationToken cancellationToken) =>
{
    var query = new GetUserQuery(id);
    var user = await mediator.Send<GetUserQuery, UserDto>(query, cancellationToken);
    return user != null ? Results.Ok(user) : Results.NotFound();
});

app.MapGet("/users", async (AotExpressMediator mediator, CancellationToken cancellationToken) =>
{
    var query = new GetAllUsersQuery();
    var users = await mediator.Send<GetAllUsersQuery, List<UserDto>>(query, cancellationToken);
    return Results.Ok(users);
});

app.MapPost("/users", async (CreateUserRequest request, AotExpressMediator mediator, CancellationToken cancellationToken) =>
{
    CreateUserCommand command = new(request.Name, request.Email);
    await mediator.Send(command);
    
    // Publish event
    await mediator.Publish(new UserCreatedEvent(command.UserId, command.Name, command.Email, cancellationToken));
    
    return Results.Created($"/users/{command.UserId}", new { Id = command.UserId });
});

app.Run();

// ============================================================================
// Domain Models & DTOs
// ============================================================================

public record UserDto(int Id, string Name, string Email);
public record CreateUserRequest(string Name, string Email);

// ============================================================================
// Queries
// ============================================================================

public record GetUserQuery(int UserId) : IQuery<UserDto>;
public record GetAllUsersQuery : IQuery<List<UserDto>>;

// ============================================================================
// Commands
// ============================================================================

public record CreateUserCommand(string Name, string Email) : ICommand
{
    public int UserId { get; set; }
}

// ============================================================================
// Events
// ============================================================================

public record UserCreatedEvent(int UserId, string Name, string Email, CancellationToken CancellationToken) : IEvent;

// ============================================================================
// Handlers
// ============================================================================

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _repository;

    public GetUserQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdAsync(query.UserId, cancellationToken);
        return user ?? throw new KeyNotFoundException($"User {query.UserId} not found");
    }
}

public class GetAllUsersQueryHandler : IQueryHandler<GetAllUsersQuery, List<UserDto>>
{
    private readonly IUserRepository _repository;

    public GetAllUsersQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<List<UserDto>> Handle(GetAllUsersQuery query, CancellationToken cancellationToken)
    {
        return await _repository.GetAllAsync(cancellationToken);
    }
}

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _repository;

    public CreateUserCommandHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var userId = await _repository.CreateAsync(command.Name, command.Email, cancellationToken);
        command.UserId = userId;
    }
}

public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    public ValueTask Handle(UserCreatedEvent @event, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[EVENT] User created: {@event.UserId} - {@event.Name} ({@event.Email})");
        return ValueTask.CompletedTask;
    }
}

// ============================================================================
// Repository
// ============================================================================

public interface IUserRepository
{
    ValueTask<UserDto> GetByIdAsync(int id, CancellationToken cancellationToken);
    ValueTask<List<UserDto>> GetAllAsync(CancellationToken cancellationToken);
    ValueTask<int> CreateAsync(string name, string email, CancellationToken cancellationToken);
}

public class InMemoryUserRepository : IUserRepository
{
    private readonly List<UserDto> _users = new();
    private int _nextId = 1;

    public ValueTask<UserDto> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        UserDto user = _users.First(u => u.Id == id);
        return ValueTask.FromResult(user);
    }

    public ValueTask<List<UserDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(_users.ToList());
    }

    public ValueTask<int> CreateAsync(string name, string email, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        _users.Add(new UserDto(id, name, email));
        return ValueTask.FromResult(id);
    }
}

// ============================================================================
// JSON Serialization Context for AOT
// ============================================================================

[System.Text.Json.Serialization.JsonSerializable(typeof(UserDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<UserDto>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CreateUserRequest))]
internal partial class AppJsonSerializerContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
