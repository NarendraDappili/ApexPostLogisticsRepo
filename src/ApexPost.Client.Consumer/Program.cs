using ApexPost.Client;
using ApexPost.Client.Consumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// A console program that uses the generated ApexPost client the way a partner would. Start the API first
// (dotnet run --project src/ApexPost.Api --launch-profile http), then: dotnet run --project src/ApexPost.Client.Consumer
// The API's address is ApexPostApi:BaseAddress in appsettings.json, or --ApexPostApi:BaseAddress=<url>. The API
// requires a bearer token: --ApexPostApi:AccessToken=<token>, or, against a Development API, one from /dev/token.
// appsettings.json sits next to the program; read it from there whatever directory the program is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
var baseAddress = new Uri(builder.Configuration["ApexPostApi:BaseAddress"]
    ?? throw new InvalidOperationException("ApexPostApi:BaseAddress is not configured."));
var courierId = builder.Configuration["ApexPostApi:CourierId"]
    ?? throw new InvalidOperationException("ApexPostApi:CourierId is not configured.");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(AccessTokens.DevelopmentTokenClient, client => client.BaseAddress = baseAddress);
builder.Services.AddSingleton<AccessTokens>();
builder.Services.AddApexPostClient(baseAddress)
    .WithBearerToken((services, cancellationToken) => services.GetRequiredService<AccessTokens>().GetAsync(cancellationToken));
builder.Services.AddTransient<ShipmentTour>();

using var host = builder.Build();
return await host.Services.GetRequiredService<ShipmentTour>().RunAsync(Console.Out, courierId: courierId);
