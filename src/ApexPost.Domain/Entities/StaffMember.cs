using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class StaffMember : IEquatable<StaffMember>
{
    public StaffMember(StaffId id, string name, StaffRole role, BranchCode assignedBranchCode, string? email = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(assignedBranchCode);
        EnsureDefined(role);

        Id = id;
        Name = name;
        Email = email;
        Role = role;
        AssignedBranchCode = assignedBranchCode;
        IsActive = true;
    }

    public StaffId Id { get; }

    public string Name
    {
        get;
        private set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Name));
            field = value.Trim();
        }
    }

    public string? Email { get; private set => field = value is null ? null : NormalizeEmail(value); }

    public StaffRole Role { get; private set; }

    public BranchCode AssignedBranchCode { get; private set; }

    /// <summary>An opaque, salted password hash. The domain never sees or stores a plain-text password.</summary>
    public string? PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void UpdateDetails(string name, string email, StaffRole role, BranchCode assignedBranchCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(assignedBranchCode);
        var normalizedEmail = NormalizeEmail(email);
        EnsureDefined(role);

        Name = name;
        Email = normalizedEmail;
        Role = role;
        AssignedBranchCode = assignedBranchCode;
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public bool CanProcessTransactionsAt(BranchCode branchCode)
    {
        ArgumentNullException.ThrowIfNull(branchCode);

        return IsActive && (Role != StaffRole.CounterClerk || AssignedBranchCode == branchCode);
    }

    public bool Equals(StaffMember? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is StaffMember other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();

    private static void EnsureDefined(StaffRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
    }

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (trimmed.Length > 254 || at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1 || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A staff email address must contain one '@' and no spaces.", nameof(email));
        }

        return trimmed.ToLowerInvariant();
    }
}
