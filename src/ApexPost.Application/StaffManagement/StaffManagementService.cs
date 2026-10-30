using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.StaffManagement;

public sealed record StaffDetailsRequest(
    [property: NotBlank(48)] string StaffId,
    [property: NotBlank(160)] string Name,
    [property: NotBlank(254)] string Email,
    StaffRole Role,
    [property: NotBlank(48)] string BranchCode);

public sealed class StaffMemberNotFoundException(string message) : NotFoundException(message);

/// <summary>Adds, updates, deactivates and reactivates staff. Passwords are hashed before they reach the repository.</summary>
public sealed class StaffManagementService
{
    public const int MinimumPasswordLength = 10;
    private readonly IStaffMemberRepository _staff;
    private readonly IBranchRepository _branches;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public StaffManagementService(IStaffMemberRepository staff, IBranchRepository branches, IPasswordHasher passwordHasher, IUnitOfWork unitOfWork)
    {
        _staff = staff ?? throw new ArgumentNullException(nameof(staff));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<IReadOnlyCollection<StaffMember>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await _staff.GetAllAsync(cancellationToken).ConfigureAwait(false)).OrderBy(member => member.Id.Value, StringComparer.Ordinal).ToArray();

    public async Task<StaffMember> GetAsync(StaffId staffId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staffId);
        return await _staff.FindByIdAsync(staffId, cancellationToken).ConfigureAwait(false)
            ?? throw new StaffMemberNotFoundException("The staff member was not found.");
    }

    public async Task<StaffMember> CreateAsync(StaffDetailsRequest request, string initialPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestValidator.Validate(request);
        EnsurePasswordPolicy(initialPassword);
        var id = new StaffId(request.StaffId);
        var branchCode = await ExistingBranchAsync(request.BranchCode, cancellationToken).ConfigureAwait(false);
        if (await _staff.FindByIdAsync(id, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("A staff member with this ID already exists.");
        }

        var member = new StaffMember(id, request.Name, request.Role, branchCode, request.Email);
        member.SetPasswordHash(_passwordHasher.Hash(initialPassword));
        _staff.Add(member);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return member;
    }

    /// <summary>Updates staff details and, when <paramref name="newPassword"/> is supplied, resets the password.</summary>
    public async Task<StaffMember> UpdateAsync(StaffDetailsRequest request, string? newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestValidator.Validate(request);
        if (!string.IsNullOrEmpty(newPassword)) EnsurePasswordPolicy(newPassword);
        var member = await GetAsync(new StaffId(request.StaffId), cancellationToken).ConfigureAwait(false);
        var branchCode = await ExistingBranchAsync(request.BranchCode, cancellationToken).ConfigureAwait(false);

        member.UpdateDetails(request.Name, request.Email, request.Role, branchCode);
        if (!string.IsNullOrEmpty(newPassword)) member.SetPasswordHash(_passwordHasher.Hash(newPassword));
        _staff.Update(member);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return member;
    }

    public async Task<StaffMember> SetActiveAsync(StaffId staffId, bool active, StaffId actingStaffId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actingStaffId);
        if (!active && staffId == actingStaffId) throw new InvalidOperationException("You cannot deactivate your own account.");
        var member = await GetAsync(staffId, cancellationToken).ConfigureAwait(false);
        if (active) member.Activate(); else member.Deactivate();
        _staff.Update(member);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return member;
    }

    private async Task<BranchCode> ExistingBranchAsync(string rawBranchCode, CancellationToken cancellationToken)
    {
        var branchCode = new BranchCode(rawBranchCode);
        return await _branches.FindByCodeAsync(branchCode, cancellationToken).ConfigureAwait(false) is null
            ? throw new InvalidOperationException("The home branch was not found.")
            : branchCode;
    }

    private static void EnsurePasswordPolicy(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength || password.Length > 256)
        {
            throw new ArgumentException($"A password must be between {MinimumPasswordLength} and 256 characters.", nameof(password));
        }
    }
}
