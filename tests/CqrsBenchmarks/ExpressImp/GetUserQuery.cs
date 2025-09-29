using System;

namespace CqrsBenchmarks.ExpressImp;

public record GetUserQuery(int Id) : CqrsExpress.Contracts.IQuery<UserDto>
{ }
