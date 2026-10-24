namespace ApexPost.Web.Filters;

public sealed class ActionPerformanceOptions
{
    /// <summary>The action duration at which a warning is emitted.</summary>
    public int WarningThresholdMilliseconds { get; set; } = 500;
}
