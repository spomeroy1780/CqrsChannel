using CqrsExpress.Core;
using CqrsExpress.Core.Aot;
using CqrsExpress.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CqrsExpress.DependencyInjection;

/// <summary>
/// AOT-compatible service collection extensions for ExpressMediator.
/// </summary>
public static class AotExpressMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Register AOT-compatible ExpressMediator with manual handler registration.
    /// Use this for Native AOT scenarios where reflection is not available.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureRegistry">Configure handler registry with explicit registrations</param>
    /// <returns>Service collection for chaining</returns>
    /// <example>
    /// <code>
    /// services.AddAotExpressMediator(registry =>
    /// {
    ///     registry.RegisterQuery&lt;GetUserQuery, UserDto&gt;();
    ///     registry.RegisterCommand&lt;CreateUserCommand&gt;();
    ///     registry.RegisterEvent&lt;UserCreatedEvent&gt;();
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAotExpressMediator(
        this IServiceCollection services,
        Action<IAotHandlerRegistry> configureRegistry)
    {
        ArgumentNullException.ThrowIfNull(configureRegistry);

        // Register handler registry as singleton
        var registry = new AotHandlerRegistry();
        configureRegistry(registry);
        services.AddSingleton<IAotHandlerRegistry>(registry);

        // Register mediator as singleton
        services.AddSingleton<AotExpressMediator>();

        return services;
    }

    /// <summary>
    /// Register AOT-compatible ExpressMediator with pipeline support.
    /// Pipelines are fully compatible with AOT as they don't use reflection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureRegistry">Configure handler registry with explicit registrations</param>
    /// <param name="configurePipelines">Configure pipeline options</param>
    /// <returns>Service collection for chaining</returns>
    /// <example>
    /// <code>
    /// services.AddAotExpressMediatorWithPipelines(
    ///     registry =>
    ///     {
    ///         registry.RegisterQuery&lt;GetUserQuery, UserDto&gt;();
    ///     },
    ///     options =>
    ///     {
    ///         options.AddGlobalPipeline&lt;LoggingPipeline&gt;();
    ///         options.AddGlobalPipeline&lt;TimingPipeline&gt;();
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddAotExpressMediatorWithPipelines(
        this IServiceCollection services,
        Action<IAotHandlerRegistry> configureRegistry,
        Action<ExpressMediatorOptions>? configurePipelines = null)
    {
        ArgumentNullException.ThrowIfNull(configureRegistry);

        // Register handler registry as singleton
        var registry = new AotHandlerRegistry();
        configureRegistry(registry);
        services.AddSingleton<IAotHandlerRegistry>(registry);

        // Configure pipeline options
        var options = new ExpressMediatorOptions();
        configurePipelines?.Invoke(options);

        // Register pipelines as singletons for reuse
        foreach (var pipelineType in options.GlobalPipelines)
        {
            services.TryAddSingleton(pipelineType);
        }

        foreach (var pipelineTypes in options.PerTypePipelines.Values)
        {
            foreach (var pipelineType in pipelineTypes)
            {
                services.TryAddSingleton(pipelineType);
            }
        }

        // Register mediator with pipeline resolution
        services.AddSingleton<AotExpressMediator>(provider =>
        {
            var handlerRegistry = provider.GetRequiredService<IAotHandlerRegistry>();

            var globalPipelines = options.GlobalPipelines
                .Select(t => (IRequestPipeline)provider.GetRequiredService(t))
                .ToList();

            var perTypePipelines = new Dictionary<Type, IReadOnlyList<IRequestPipeline>>();
            foreach (var kvp in options.PerTypePipelines)
            {
                perTypePipelines[kvp.Key] = kvp.Value
                    .Select(t => (IRequestPipeline)provider.GetRequiredService(t))
                    .ToList();
            }

            return new AotExpressMediator(
                provider,
                handlerRegistry,
                globalPipelines.Count > 0 ? globalPipelines : null,
                perTypePipelines.Count > 0 ? perTypePipelines : null);
        });

        return services;
    }

    /// <summary>
    /// Register handlers using the registry builder pattern.
    /// Provides compile-time type safety and zero reflection.
    /// </summary>
    public static IServiceCollection AddAotHandlers(
        this IServiceCollection services,
        Action<IAotHandlerRegistry> configure)
    {
        var registry = services.BuildServiceProvider().GetRequiredService<IAotHandlerRegistry>();
        configure(registry);
        return services;
    }
}
