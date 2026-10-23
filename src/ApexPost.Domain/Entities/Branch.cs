using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class Branch : IEquatable<Branch>
{
    public Branch(BranchCode code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Code = code;
        Name = name.Trim();
        IsOpen = true;
    }

    public BranchCode Code { get; }

    public string Name { get; }

    public bool IsOpen { get; private set; }

    public bool CanAcceptTransactions => IsOpen;

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public bool Equals(Branch? other) => other is not null && Code == other.Code;

    public override bool Equals(object? obj) => obj is Branch other && Equals(other);

    public override int GetHashCode() => Code.GetHashCode();
}
