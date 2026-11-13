using DeskPal.Shared.Api;

namespace DeskPal.Server.Common;

/// <param name="Error">One of <see cref="ApiErrorCodes"/>; null on success. The client turns it into text.</param>
public sealed record ServiceResult<T>(T? Value, int Status, string? Error, IReadOnlyDictionary<string, string>? Args = null)
{
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, StatusCodes.Status200OK, null);

    public static ServiceResult<T> Fail(int status, string code) => new(default, status, code);

    /// <summary>For codes whose message shows a limit, e.g. <see cref="ApiErrorCodes.RoomFull"/>.</summary>
    public static ServiceResult<T> Fail(int status, string code, int limit) =>
        new(default, status, code, new Dictionary<string, string> { [ApiErrorCodes.LimitArg] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    public IResult ToHttp() => IsSuccess ? Results.Ok(Value) : Results.Json(new ApiError(Error!, Args), statusCode: Status);
}

public sealed record Unit
{
    public static readonly Unit Value = new();
}