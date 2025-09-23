using CqrsCompiledExpress.DependencyInjection;
using CqrsCompiledExpress.Mediator;
using CqrsCompiledExpress.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsCompiledExpress.Tests;

public class CompiledExpressMediatorTests
{
    private readonly CompiledExpressMediator _mediator;

    public CompiledExpressMediatorTests()
    {
        var services = new ServiceCollection();
        services.AddCompiledExpressMediator();
        services.AddTransient<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddTransient<IQueryHandler<TestQuery, string>, TestQueryHandler>();
        var serviceProvider = services.BuildServiceProvider();
        _mediator = new CompiledExpressMediator(serviceProvider);
    }

    [Fact]
    public async Task Send_Command_Should_Invoke_Handler()
    {
        var command = new TestCommand();
        await _mediator.Send(command);
        Assert.True(TestCommandHandler.WasCalled);
    }

    [Fact]
    public async Task Send_Query_Should_Invoke_Handler_And_Return_Result()
    {
        var query = new TestQuery();
        var result = await _mediator.Send<string>(query);
        Assert.Equal("TestResult", result);
        Assert.True(TestQueryHandler.WasCalled);
    }

    public record TestCommand() : ICommand;

    public class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public static bool WasCalled { get; private set; } = false;

        public ValueTask HandleAsync(TestCommand command, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    public record TestQuery() : IQuery<string>;

    public class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public static bool WasCalled { get; private set; } = false;

        public ValueTask<string> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult("TestResult");
        }
    }

}
