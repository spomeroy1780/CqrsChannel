using CqrsExpress.Core;
using CqrsExpress.Contracts;
using CqrsExpress.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsExpress.Tests;

public class ExpressMediatorTests
{
    private readonly ExpressMediator _mediator;

    public ExpressMediatorTests()
    {
        var services = new ServiceCollection();
        services.AddExpressMediator();
        services.AddTransient<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddTransient<IQueryHandler<TestQuery, string>, TestQueryHandler>();
        var serviceProvider = services.BuildServiceProvider();
        _mediator = new ExpressMediator(serviceProvider);
    }

    [Fact]
    public async Task Send_Command_Should_Invoke_Handler()
    {
        var command = new TestCommand();
        var handler = new TestCommandHandler();
        await _mediator.Send(command, handler, CancellationToken.None);
        Assert.True(handler.WasCalled);
    }

    [Fact]
    public async Task Send_Query_Should_Invoke_Handler_And_Return_Result()
    {
        var query = new TestQuery();
        var handler = new TestQueryHandler();
        var result = await _mediator.Send<TestQuery, string>(query, handler, CancellationToken.None);
        Assert.Equal("TestResult", result);
        Assert.True(handler.WasCalled);
    }

    public record TestCommand() : ICommand;

    public class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public bool WasCalled { get; private set; } = false;

        public ValueTask Handle(TestCommand command, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    public record TestQuery() : IQuery<string>;

    public class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public bool WasCalled { get; private set; } = false;

        public ValueTask<string> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult("TestResult");
        }
    }

}
