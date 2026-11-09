using ApexPost.Courier;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// The ApexPost courier app, as a console program: scans are saved on the device first and uploaded later.
// Usage: dotnet run --project src/ApexPost.Courier [--Courier:DataDirectory=<folder>] [--ApexPostApi:BaseAddress=<url>]
CourierDatabase.LimitEngineConsoleLogToErrors();
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
    try
    {
        return await host.Services.GetRequiredService<CourierApp>().RunAsync();
    }
    catch (CourierDatabaseInUseException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
}
