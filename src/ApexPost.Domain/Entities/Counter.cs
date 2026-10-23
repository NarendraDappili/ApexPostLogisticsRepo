using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class Counter : IEquatable<Counter>
{
    public Counter(CounterCode code, BranchCode branchCode)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(branchCode);

        Code = code;
        BranchCode = branchCode;
        IsOpen = true;
    }

    public CounterCode Code { get; }

    public BranchCode BranchCode { get; }

    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public bool Equals(Counter? other) =>
        other is not null && Code == other.Code && BranchCode == other.BranchCode;

    public override bool Equals(object? obj) => obj is Counter other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Code, BranchCode);
}
