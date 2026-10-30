using ApexPost.Application.Repositories;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.BranchManagement;

public sealed record BranchDetailsRequest(
    [property: NotBlank(48)] string BranchCode,
    [property: NotBlank(160)] string Name,
    [property: NotBlank(80)] string Region,
    [property: NotBlank(120)] string OpeningHours);

public sealed class BranchNotFoundException(string message) : InvalidOperationException(message);

/// <summary>Adds, updates, closes and reopens branches. A new branch is created with its counter terminals.</summary>
public sealed class BranchManagementService
{
    public const int MaximumCounterCount = 20;
    private readonly IBranchRepository _branches;
    private readonly ICounterRepository _counters;

    public BranchManagementService(IBranchRepository branches, ICounterRepository counters)
    {
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    }

    public async Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await _branches.GetAllAsync(cancellationToken).ConfigureAwait(false)).OrderBy(branch => branch.Code.Value, StringComparer.Ordinal).ToArray();

    public async Task<Branch> GetAsync(BranchCode branchCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        return await _branches.FindByCodeAsync(branchCode, cancellationToken).ConfigureAwait(false)
            ?? throw new BranchNotFoundException("The branch was not found.");
    }

    public async Task<Branch> CreateAsync(BranchDetailsRequest request, int counterCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestValidator.Validate(request);
        if (counterCount is < 1 or > MaximumCounterCount)
        {
            throw new ArgumentOutOfRangeException(nameof(counterCount), $"A branch needs between 1 and {MaximumCounterCount} counters.");
        }

        var code = new BranchCode(request.BranchCode);
        if (await _branches.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("A branch with this code already exists.");
        }

        var branch = new Branch(code, request.Name, request.Region, request.OpeningHours);
        await _branches.SaveAsync(branch, cancellationToken).ConfigureAwait(false);
        for (var number = 1; number <= counterCount; number++)
        {
            await _counters.SaveAsync(new Counter(new CounterCode($"C-{number:00}"), code), cancellationToken).ConfigureAwait(false);
        }

        return branch;
    }

    public async Task<Branch> UpdateAsync(BranchDetailsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestValidator.Validate(request);
        var branch = await GetAsync(new BranchCode(request.BranchCode), cancellationToken).ConfigureAwait(false);
        branch.UpdateDetails(request.Name, request.Region, request.OpeningHours);
        await _branches.SaveAsync(branch, cancellationToken).ConfigureAwait(false);
        return branch;
    }

    public async Task<Branch> SetOpenAsync(BranchCode branchCode, bool open, CancellationToken cancellationToken = default)
    {
        var branch = await GetAsync(branchCode, cancellationToken).ConfigureAwait(false);
        if (open) branch.Open(); else branch.Close();
        await _branches.SaveAsync(branch, cancellationToken).ConfigureAwait(false);
        return branch;
    }
}
