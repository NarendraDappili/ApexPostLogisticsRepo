using ApexPost.Courier.Offline.Harness;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;

// The courier device, simulated in a console: a local Couchbase Lite store that works with no network, and a fake
// server for the outbox. Usage: dotnet run --project src/ApexPost.Courier.Offline.Harness [--data <folder>] [--courier <id>]
HarnessOptions options;
try
{
    options = HarnessOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

var output = Console.Out;
CourierDatabase.LimitEngineConsoleLogToErrors();
CourierDatabase database;
try
{
    database = new CourierDatabase(new CourierDatabaseOptions { Directory = options.DataDirectory }, new LineLogger<CourierDatabase>(output));
}
catch (CourierDatabaseInUseException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

using (database)
using (var monitor = new ScanChangeMonitor(database, new LineLogger<ScanChangeMonitor>(output)))
{
    var repository = new CouchbaseScanRepository(database, new LineLogger<CouchbaseScanRepository>(output));
    var server = new FakeShipmentUploader();
    var outbox = new OutboxService(repository, server, logger: new LineLogger<OutboxService>(output));
    var console = new CourierConsole(Console.In, output, repository, monitor, outbox, server, options.CourierId, TimeProvider.System);
    await console.RunAsync();
}

return 0;
