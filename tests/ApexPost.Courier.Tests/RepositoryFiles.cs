namespace ApexPost.Courier.Tests;

/// <summary>Committed files the tests read: the API contracts and the app's own source.</summary>
internal static class RepositoryFiles
{
    /// <summary>The repository root: the directory above the test output that holds ApexPost.slnx.</summary>
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApexPost.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("ApexPost.slnx was not found above " + AppContext.BaseDirectory);
    }
}
