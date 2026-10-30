using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class Branch : IEquatable<Branch>
{
    public const string UnassignedRegion = "Unassigned";
    public const string UnconfiguredOpeningHours = "Not configured";

    public Branch(BranchCode code, string name, string region = UnassignedRegion, string openingHours = UnconfiguredOpeningHours)
    {
        ArgumentNullException.ThrowIfNull(code);

        Code = code;
        Name = name;
        Region = region;
        OpeningHours = openingHours;
        IsOpen = true;
    }

    public BranchCode Code { get; }

    // The field keyword keeps validation in the setter without a hand-written backing field.
    public string Name { get; private set => field = RequiredText(value, nameof(Name)); }

    public string Region { get; private set => field = RequiredText(value, nameof(Region)); }

    public string OpeningHours { get; private set => field = RequiredText(value, nameof(OpeningHours)); }

    public bool IsOpen { get; private set; }

    public bool CanAcceptTransactions => IsOpen;

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void UpdateDetails(string name, string region, string openingHours)
    {
        // Validate everything before assigning so a failed update leaves the branch unchanged.
        RequiredText(name, nameof(name));
        RequiredText(region, nameof(region));
        RequiredText(openingHours, nameof(openingHours));
        Name = name;
        Region = region;
        OpeningHours = openingHours;
    }

    public bool Equals(Branch? other) => other is not null && Code == other.Code;

    public override bool Equals(object? obj) => obj is Branch other && Equals(other);

    public override int GetHashCode() => Code.GetHashCode();

    private static string RequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
