namespace ApexPost.Infrastructure.Persistence;

/// <summary>Configuration for the directory containing ApexPost JSON data files.</summary>
public sealed class JsonRepositoryOptions
{
    public required string DataDirectory { get; init; }
}
