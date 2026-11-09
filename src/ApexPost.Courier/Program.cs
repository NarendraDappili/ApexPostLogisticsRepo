using ApexPost.Courier;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Screens;
using Microsoft.Extensions.DependencyInjection;

// The ApexPost courier app, as a console program: scans are saved on the device first and uploaded later.
// Usage: dotnet run --project src/ApexPost.Courier [--Courier:DataDirectory=<folder>]
CourierDatabase.LimitEngineConsoleLogToErrors();
using var host = CourierAppHost.Create(args, Console.In, Console.Out);
try
{
    return await host.Services.GetRequiredService<CourierApp>().RunAsync();
}
catch (CourierDatabaseInUseException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
