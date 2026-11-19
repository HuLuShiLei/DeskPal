using System.Net.Http;
using DeskPal.Client.Core.Updates;
using Velopack.Sources;

namespace DeskPal.Client.Services;

public sealed class UpdateRateLimitedException(DateTimeOffset? resetAt)
    : Exception("GitHub API rate limit exceeded")
{
    public DateTimeOffset? ResetAt { get; } = resetAt;

    public static UpdateRateLimitedException? Find(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is UpdateRateLimitedException limited)
            {
                return limited;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (Find(inner) is { } found)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Velopack 只用 DownloadString 请求 GitHub API 的 Release 列表；安装包和清单走 browser_download_url，不占额度。
/// 基类失败后会把网址改成小写再请求一次，对 API 来说白白多用一次额度，这里不重试。
/// </summary>
public sealed class GithubApiDownloader : HttpClientFileDownloader
{
    public override async Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        using var client = CreateHttpClient(headers, timeout);
        using var response = await client.GetAsync(url).ConfigureAwait(false);
        var retryAfter = response.Headers.RetryAfter;
        var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) ? values.FirstOrDefault() : null;
        if (UpdateCheckThrottle.IsRateLimited((int)response.StatusCode, remaining, retryAfter is not null))
        {
            throw new UpdateRateLimitedException(ResetAt(response, retryAfter));
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static DateTimeOffset? ResetAt(HttpResponseMessage response, System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return DateTimeOffset.UtcNow + delta;
        }

        if (retryAfter?.Date is { } date)
        {
            return date;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            && long.TryParse(values.FirstOrDefault(), out var seconds)
            && seconds is > 0 and < 253402300800)
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return null;
    }
}