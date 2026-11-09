namespace ApexPost.Courier;

/// <summary>The <c>Courier</c> configuration section.</summary>
internal sealed class CourierAppOptions
{
    public const string Section = "Courier";

    /// <summary>
    /// The folder of the device's scan database. Empty means <see cref="DefaultDataDirectory"/>. It is not the Part A
    /// harness folder: an embedded database belongs to one process at a time (OFFLINE-DESIGN.md).
    /// </summary>
    public string? DataDirectory { get; set; }

    public static string DefaultDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPost", "Courier");

    public string ResolveDataDirectory() =>
        string.IsNullOrWhiteSpace(DataDirectory) ? DefaultDataDirectory : Path.GetFullPath(DataDirectory);
}
