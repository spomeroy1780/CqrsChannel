using CqrsExpress.Pipeline;
namespace CqrsExpress.Core;

public sealed class ExpressMediatorOptions
{
    internal readonly List<Type> GlobalPipelines = new();
    internal readonly Dictionary<Type, List<Type>> PerTypePipelines = new();

    public void AddGlobalPipeline<T>() where T : class, IRequestPipeline
        => GlobalPipelines.Add(typeof(T));

    public void AddPerTypePipeline<TRequest>(Type pipelineType)
        => AddPerTypePipeline(typeof(TRequest), pipelineType);

    public void AddPerTypePipeline(Type requestType, Type pipelineType)
    {
        if (!PerTypePipelines.TryGetValue(requestType, out var list))
            PerTypePipelines[requestType] = list = new List<Type>();

        list.Add(pipelineType);
    }
}
