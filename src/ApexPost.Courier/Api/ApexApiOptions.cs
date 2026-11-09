using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Api;

/// <summary>
/// The ApexPostApi configuration section: where the API is. appsettings.json points at the API's https launch profile
/// on this machine; the app's launch profiles and --ApexPostApi:BaseAddress=&lt;url&gt; point elsewhere (README.md).
/// </summary>
internal sealed class ApexApiOptions
{
    public const string Section = "ApexPostApi";

    public Uri? BaseAddress { get; set; }

    /// <summary>The base address with a trailing slash, so relative paths such as api/v2/... resolve below it.</summary>
    public Uri Root => BaseAddress!.AbsoluteUri.EndsWith('/') ? BaseAddress : new Uri(BaseAddress.AbsoluteUri + "/");
}

/// <summary>
/// Checks the address when the app starts: absolute, and HTTPS unless it is this machine. Every request carries the
/// courier's token, which must never cross a network in clear text (SECURITY.md).
/// </summary>
internal sealed class ApexApiOptionsValidator : IValidateOptions<ApexApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApexApiOptions options) => options.BaseAddress switch
    {
        null => ValidateOptionsResult.Fail($"{ApexApiOptions.Section}:BaseAddress is not configured. Set it in appsettings.json or with --{ApexApiOptions.Section}:BaseAddress=<url>."),
        { IsAbsoluteUri: false } => ValidateOptionsResult.Fail($"{ApexApiOptions.Section}:BaseAddress must be an absolute URL such as https://localhost:7245/."),
        var address when !AuthHeaderHandler.IsSafeForSecrets(address) =>
            ValidateOptionsResult.Fail($"{ApexApiOptions.Section}:BaseAddress must use https://. Plain http:// is allowed only for this machine (localhost), because every request carries the courier's token."),
        _ => ValidateOptionsResult.Success,
    };
}
