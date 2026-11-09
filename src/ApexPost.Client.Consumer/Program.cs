using ApexPost.Client;
using ApexPost.Client.Consumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// A console program that uses the generated ApexPost client the way a partner would. Start the API first
// (dotnet run --project src/ApexPost.Api --launch-profile http), then: dotnet run --project src/ApexPost.Client.Consumer
// The API's address is ApexPostApi:BaseAddress in appsettings.json, or --ApexPostApi:BaseAddress=<url>.
// appsettings.json sits next to the program; read it from there whatever directory the program is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
var baseAddress = builder.Configuration["ApexPostApi:BaseAddress"]
    ?? throw new InvalidOperationException("ApexPostApi:BaseAddress is not configured.");
builder.Services.AddApexPostClient(new Uri(baseAddress));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<ShipmentTour>();

using var host = builder.Build();
return await host.Services.GetRequiredService<ShipmentTour>().RunAsync(Console.Out);
