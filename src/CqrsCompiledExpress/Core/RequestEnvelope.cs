namespace CqrsCompiledExpress.Core;

internal readonly struct RequestEnvelope
{
    public readonly object Request;
    public readonly Type RequestType;
    public readonly object? Tcs;

    public RequestEnvelope(object req, Type rt, object? tcs)
    {
        Request = req;
        RequestType = rt;
        Tcs = tcs;
    }
}
