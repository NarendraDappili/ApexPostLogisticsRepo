using ApexPost.Courier;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Screens;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// The ApexPost courier app, as a console program: scans are saved on the device first and uploaded later.
// Usage: dotnet run --project src/ApexPost.Courier [--Courier:DataDirectory=<folder>] [--ApexPostApi:BaseAddress=<url>]
CourierDatabase.LimitEngineConsoleLogToErrors();
// The screens use characters such as the dash in "Server unavailable – working offline"; the console's legacy code
// page would print them as question marks.
Console.OutputEncoding = System.Text.Encoding.UTF8;
IHost host;
try
{
    host = CourierAppHost.Create(args, Console.In, Console.Out);
}
catch (OptionsValidationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}

using (host)
{
    CourierApp app;
    try
    {
        app = host.Services.GetRequiredService<CourierApp>();
    }
    catch (CourierDatabaseInUseException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }

    // Automatic sync runs beside the menu. It is started on its own rather than with host.StartAsync(): the host's
    // console lifetime would take over Ctrl+C, which then could not interrupt the menu waiting for input.
    var autoSync = host.Services.GetRequiredService<AutoSyncService>();
    await autoSync.StartAsync(CancellationToken.None);
    try
    {
        return await app.RunAsync();
    }
    finally
    {
        await autoSync.StopAsync(CancellationToken.None);
    }
}
