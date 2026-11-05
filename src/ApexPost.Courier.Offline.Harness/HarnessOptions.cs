namespace ApexPost.Courier.Offline.Harness;

/// <summary>Command line: <c>--data &lt;folder&gt;</c> for the database folder, <c>--courier &lt;id&gt;</c> for the signed-in courier.</summary>
internal sealed record HarnessOptions(string DataDirectory, string CourierId)
{
    public const string DefaultCourierId = "CR-001";

    public static string DefaultDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPost", "CourierOffline");

    public static HarnessOptions Parse(IReadOnlyList<string> args)
    {
        var dataDirectory = DefaultDataDirectory;
        var courierId = DefaultCourierId;
        for (var i = 0; i < args.Count; i++)
        {
            var value = i + 1 < args.Count ? args[i + 1] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--data":
                    dataDirectory = Path.GetFullPath(value);
                    break;
                case "--courier":
                    courierId = value.Trim().ToUpperInvariant();
                    break;
                default:
                    throw new ArgumentException($"Unknown option {args[i]}. Use --data <folder> and --courier <id>.");
            }

            i++;
        }

        return new HarnessOptions(dataDirectory, courierId);
    }
}
