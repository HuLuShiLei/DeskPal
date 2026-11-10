using DeskPal.Shared.Api;

namespace DeskPal.Server.Common;

public sealed record ServiceResult<T>(T? Value, int Status, string? Error)
{
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, StatusCodes.Status200OK, null);

    public static ServiceResult<T> Fail(int status, string error) => new(default, status, error);

    public IResult ToHttp() => IsSuccess ? Results.Ok(Value) : Results.Json(new ApiError(Error!), statusCode: Status);
}

public sealed record Unit
{
    public static readonly Unit Value = new();
}