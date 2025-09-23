using CqrsCompiledExpress.Contracts;

namespace CqrsBenchmarks.ChannelsImp;

public record ChannelQuery(int Id) : IQuery<UserDto>;
