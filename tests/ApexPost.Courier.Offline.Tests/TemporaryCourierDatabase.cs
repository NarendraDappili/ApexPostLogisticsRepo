using ApexPost.Courier.Offline.Storage;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>
/// A courier database in its own temporary folder, so tests never share a database file (an embedded database is
/// opened by one owner at a time). The folder is deleted when the test ends.
/// </summary>
internal sealed class TemporaryCourierDatabase : IDisposable
{
    public TemporaryCourierDatabase()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apexpost-courier-tests", Guid.NewGuid().ToString("N"));
        Database = Open();
    }

    public string Directory { get; }

    public CourierDatabase Database { get; private set; }

    /// <summary>Closes the database and opens the same files again, as an app restart would.</summary>
    public CourierDatabase Reopen()
    {
        Database.Dispose();
        Database = Open();
        return Database;
    }

    public void Dispose()
    {
        Database.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless; it must not fail the test.
        }
    }

    private CourierDatabase Open() => new(new CourierDatabaseOptions { Directory = Directory });
}
