namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// The local runtime directory for files that are not business data: the JSON Lines audit log and the
/// ASP.NET Core data-protection keys. Business data lives in SQL Server.
/// </summary>
public sealed class JsonRepositoryOptions
{
    public required string DataDirectory { get; init; }
}
