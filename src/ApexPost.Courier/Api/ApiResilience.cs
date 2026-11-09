using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace ApexPost.Courier.Api;

/// <summary>
/// The ApexPostApi:Resilience configuration section. The defaults are the values RESILIENCE.md explains; tests
/// shorten them.
/// </summary>
internal sealed class ApiResilienceOptions
{
    public const string Section = "ApexPostApi:Resilience";

    /// <summary>Retries after the first attempt, so a call is tried at most four times.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>The first retry waits about this long; each later one about twice as long, with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long one attempt may take before it is abandoned (and retried).</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a call may take in all, retries and waits included.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The circuit opens when at least this share of the recent attempts failed...</summary>
    public double BreakerFailureRatio { get; set; } = 0.5;

    /// <summary>...and there were at least this many of them...</summary>
    public int BreakerMinimumThroughput { get; set; } = 4;

    /// <summary>...within this window.</summary>
    public TimeSpan BreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before one trial call may go through.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The resilience pipeline of <see cref="ApexApiClient"/> (RESILIENCE.md), from the outside in:
/// a total timeout, the retry, the circuit breaker, and a timeout per attempt. So each retry gets its own time
/// limit, every attempt counts towards the breaker, and while the breaker is open a call fails at once instead of
/// waiting for a server that is down. Every retry, timeout and circuit change is logged, without the request body or
/// the token.
/// </summary>
internal static class ApiResilience
{
    public const string PipelineName = "apexpost-api";

    /// <summary>
    /// Marks a request that is safe to send twice although its method is not idempotent: a scan upload, which the
    /// server de-duplicates by scan ID. Other POSTs are never retried.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SafeToRetry = new("ApexPost.SafeToRetry");

    public static IHttpClientBuilder AddApexApiResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(PipelineName, static (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<ApiResilienceOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ApiResilience).FullName!);
            var availability = context.ServiceProvider.GetRequiredService<ServerAvailability>();

            pipeline
                .AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Name = "total-timeout",
                    Timeout = options.TotalTimeout,
                    OnTimeout = args =>
                    {
                        logger.LogWarning("The API call was abandoned after {Timeout} in all", args.Timeout);
                        return default;
                    },
                })
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = options.RetryBaseDelay,
                    ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome) && IsSafeToRetry(args.Context.GetRequestMessage())),
                    OnRetry = args =>
                    {
                        var request = args.Context.GetRequestMessage();
                        logger.LogWarning(
                            "Retry {Retry} of {MaxRetries} for {Method} {Path} in {Delay} ms, after {Failure}",
                            args.AttemptNumber + 1, options.MaxRetryAttempts, request?.Method, request?.RequestUri?.AbsolutePath, (int)args.RetryDelay.TotalMilliseconds, Describe(args.Outcome));
                        return default;
                    },
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = options.BreakerFailureRatio,
                    MinimumThroughput = options.BreakerMinimumThroughput,
                    SamplingDuration = options.BreakerSamplingDuration,
                    BreakDuration = options.BreakDuration,
                    ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
                    OnOpened = args =>
                    {
                        availability.Opened(args.BreakDuration);
                        logger.LogWarning("Circuit opened for {BreakSeconds:0.#} s after {Failure}: working offline", args.BreakDuration.TotalSeconds, Describe(args.Outcome));
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        availability.HalfOpened();
                        logger.LogInformation("Circuit half-open: the next call tries the server again");
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        availability.Closed();
                        logger.LogInformation("Circuit closed: the server answers again");
                        return default;
                    },
                })
                .AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Name = "attempt-timeout",
                    Timeout = options.AttemptTimeout,
                    OnTimeout = args =>
                    {
                        logger.LogWarning("An attempt was abandoned after {Timeout}", args.Timeout);
                        return default;
                    },
                });
        });
        return builder;
    }

    /// <summary>
    /// Worth another try: the network failed, an attempt timed out, or the server answered 408 or 5xx. Never any
    /// other 4xx: the same request would get the same answer.
    /// </summary>
    internal static bool IsTransientFailure(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException or TimeoutRejectedException } => true,
        { Result: { } response } => response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500,
        _ => false,
    };

    /// <summary>GET, HEAD, PUT and DELETE are idempotent by definition; a POST only when marked <see cref="SafeToRetry"/>.</summary>
    internal static bool IsSafeToRetry(HttpRequestMessage? request) =>
        request is not null
        && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete
            || (request.Options.TryGetValue(SafeToRetry, out var safe) && safe));

    private static string Describe(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: TimeoutRejectedException } => "a timeout",
        { Exception: BrokenCircuitException } => "an open circuit",
        { Exception: { } exception } => exception.GetType().Name,
        { Result: { } response } => $"{(int)response.StatusCode} {response.StatusCode}",
        _ => "no answer",
    };
}
