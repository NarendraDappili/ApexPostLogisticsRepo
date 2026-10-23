using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public class BranchAndCounterTests
{
    [Fact]
    public void Branch_starts_open_and_can_be_closed_and_reopened()
    {
        var branch = new Branch(new BranchCode("br-001"), " Central ");

        Assert.Equal("Central", branch.Name);
        Assert.True(branch.CanAcceptTransactions);

        branch.Close();
        Assert.False(branch.IsOpen);
        Assert.False(branch.CanAcceptTransactions);

        branch.Open();
        Assert.True(branch.IsOpen);
    }

    [Fact]
    public void Branches_with_same_code_compare_equal()
    {
        Assert.Equal(
            new Branch(new BranchCode("br-001"), "Central"),
            new Branch(new BranchCode("BR-001"), "Different name"));
    }

    [Fact]
    public void Branch_rejects_missing_name()
    {
        Assert.Throws<ArgumentException>(() => new Branch(new BranchCode("BR-001"), " "));
    }

    [Fact]
    public void Counter_tracks_its_branch_and_open_state()
    {
        var counter = new Counter(new CounterCode("counter-1"), new BranchCode("br-001"));

        Assert.Equal(new BranchCode("BR-001"), counter.BranchCode);
        Assert.True(counter.IsOpen);

        counter.Close();
        Assert.False(counter.IsOpen);
    }
}
