using ApexPost.Application.Abstractions;
using ApexPost.Application.BranchManagement;
using ApexPost.Application.Events;
using ApexPost.Application.Inventory;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Notifications;
using ApexPost.Application.Pricing;
using ApexPost.Application.Reports;
using ApexPost.Application.Repositories;
using ApexPost.Application.Shipping;
using ApexPost.Application.StaffManagement;
using ApexPost.Application.Transactions;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Pricing;
using ApexPost.Infrastructure.Repositories;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Security;
using ApexPost.Infrastructure.Simulation;
using ApexPost.Web.Services;
using ApexPost.Web.Security;
using ApexPost.Web.Filters;
using ApexPost.Application.Auditing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using ApexPost.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<AuditActionFilter>();
    options.Filters.AddService<ValidationActionFilter>();
    options.Filters.AddService<PerformanceActionFilter>();
});
builder.Services.Configure<ActionPerformanceOptions>(builder.Configuration.GetSection("ApexPost:Performance"));
builder.Services.AddProblemDetails();
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection("ApexPost:Maintenance"));
builder.Services.Configure<IntegrationSecurityOptions>(builder.Configuration.GetSection("ApexPost:Security:Integration"));
var approvedPartnerOrigin = builder.Configuration["ApexPost:Security:PartnerTrackingWidgetOrigin"];
builder.Services.AddCors(options => options.AddPolicy("PartnerTrackingWidget", policy =>
{
    if (Uri.TryCreate(approvedPartnerOrigin, UriKind.Absolute, out _)) policy.WithOrigins(approvedPartnerOrigin!).AllowAnyHeader().WithMethods("GET");
}));
var loginPermitLimit = builder.Configuration.GetValue<int?>("ApexPost:RateLimiting:LoginPermitLimit") ?? 5;
var integrationPermitLimit = builder.Configuration.GetValue<int?>("ApexPost:RateLimiting:IntegrationPermitLimit") ?? 60;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, loginPermitLimit), Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("partner-integration", context => RateLimitPartition.GetFixedWindowLimiter(
        "partner-integration", _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, integrationPermitLimit), Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "ApexPost.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = StaffCookieValidator.ValidateAsync;
        options.Events.OnRedirectToAccessDenied = AccessDeniedResponder.RespondAsync;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.BranchManagerOnly, policy => policy.RequireRole("BranchManager"));
    options.AddPolicy(AuthorizationPolicies.SameBranchOnly, policy => policy.RequireAuthenticatedUser().AddRequirements(new SameBranchRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, SameBranchAuthorizationHandler>();

var configuredDataDirectory = builder.Configuration["ApexPost:DataDirectory"] ?? "App_Data";
var dataDirectory = Path.IsPathRooted(configuredDataDirectory) ? configuredDataDirectory : Path.Combine(builder.Environment.ContentRootPath, configuredDataDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "data-protection-keys")));
builder.Services.AddSingleton(new JsonRepositoryOptions { DataDirectory = dataDirectory });
// One unit of work per request: repositories stage changes in it and each use case commits once.
builder.Services.AddScoped<JsonUnitOfWork>();
builder.Services.AddScoped<IUnitOfWork>(services => services.GetRequiredService<JsonUnitOfWork>());
builder.Services.AddScoped<IBranchRepository, JsonBranchRepository>();
builder.Services.AddScoped<ICounterRepository, JsonCounterRepository>();
builder.Services.AddScoped<IStaffMemberRepository, JsonStaffMemberRepository>();
builder.Services.AddScoped<IProductRepository, JsonProductRepository>();
builder.Services.AddScoped<IInventoryRepository, JsonInventoryRepository>();
builder.Services.AddScoped<IShipmentRepository, JsonShipmentRepository>();
builder.Services.AddScoped<IMoneyOrderRepository, JsonMoneyOrderRepository>();
builder.Services.AddScoped<ICounterTransactionRepository, JsonCounterTransactionRepository>();
builder.Services.AddSingleton<IShippingRateProvider, JsonShippingRateProvider>();
builder.Services.AddSingleton<IAuditStore, JsonAuditStore>();
builder.Services.AddScoped<AuditActionFilter>();
builder.Services.AddScoped<ValidationActionFilter>();
builder.Services.AddScoped<PerformanceActionFilter>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<LocalStaffAuthenticator>();
builder.Services.AddHostedService<DemoDataSeedHostedService>();

var inventoryThreshold = builder.Configuration.GetValue<int?>("ApexPost:Inventory:LowStockThreshold") ?? 10;
var notificationRecipient = builder.Configuration["ApexPost:Inventory:LowStockRecipient"] ?? "operations-alerts@example.test";
var highValueThreshold = builder.Configuration.GetValue<decimal?>("ApexPost:MoneyOrders:HighValueThreshold") ?? 1000m;
var moneyOrderFlatFee = builder.Configuration.GetValue<decimal?>("ApexPost:MoneyOrders:FlatFee") ?? 2.50m;
var moneyOrderFeeRate = builder.Configuration.GetValue<decimal?>("ApexPost:MoneyOrders:FeeRate") ?? 0.01m;
var taxRate = builder.Configuration.GetValue<decimal?>("ApexPost:Transactions:TaxRate") ?? 0m;
var courierLookupTimeout = builder.Configuration.GetValue<int?>("ApexPost:Courier:LookupTimeoutMilliseconds") ?? 2000;
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
builder.Services.AddSingleton(new NotificationRecipients { Inventory = notificationRecipient });
builder.Services.AddSingleton<IEventObserver, NotificationEventObserver>();
builder.Services.AddSingleton<IEventPublisher, EventPublisher>();
builder.Services.AddSingleton(new InventoryServiceOptions { LowStockThreshold = inventoryThreshold });
builder.Services.AddSingleton<IMoneyOrderPolicy>(new MoneyOrderPolicy(new Money(highValueThreshold), MoneyOrderPolicy.FlatPlusPercentageFee(new Money(moneyOrderFlatFee), moneyOrderFeeRate)));
builder.Services.AddSingleton<ITaxPolicy>(new FixedRateTaxPolicy(taxRate));
builder.Services.AddSingleton<IIdentityVerifier, SimulatedIdentityVerifier>();
builder.Services.AddSingleton(builder.Configuration.GetSection("ApexPost:Courier").Get<SimulatedCourierOptions>() ?? new SimulatedCourierOptions());
builder.Services.AddSingleton<ICourierTrackingClient, SimulatedCourierTrackingClient>();
builder.Services.AddSingleton(new CourierTrackingOptions { LookupTimeout = TimeSpan.FromMilliseconds(Math.Max(1, courierLookupTimeout)) });
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<ShippingWorkflowService>();
builder.Services.AddScoped<ParcelStatusService>();
builder.Services.AddScoped<MoneyOrderWorkflowService>();
builder.Services.AddScoped<CounterTransactionWorkflowService>();
builder.Services.AddScoped<BranchReportService>();
builder.Services.AddScoped<StaffManagementService>();
builder.Services.AddScoped<BranchManagementService>();
builder.Services.AddScoped<ReferenceDataLookup>();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ProblemDetailsExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseRouting();
app.UseMiddleware<MaintenanceModeMiddleware>();
app.UseCors();
app.UseMiddleware<IntegrationApiKeyMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<IpAllowlistMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();

public partial class Program;
