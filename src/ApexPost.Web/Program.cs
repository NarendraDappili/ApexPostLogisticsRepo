using ApexPost.Application.Inventory;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Notifications;
using ApexPost.Application.Pricing;
using ApexPost.Application.Reports;
using ApexPost.Application.Repositories;
using ApexPost.Application.Shipping;
using ApexPost.Application.Transactions;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Pricing;
using ApexPost.Infrastructure.Repositories;
using ApexPost.Infrastructure.Auditing;
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
builder.Services.AddSingleton<IBranchRepository, JsonBranchRepository>();
builder.Services.AddSingleton<ICounterRepository, JsonCounterRepository>();
builder.Services.AddSingleton<IStaffMemberRepository, JsonStaffMemberRepository>();
builder.Services.AddSingleton<IProductRepository, JsonProductRepository>();
builder.Services.AddSingleton<IInventoryRepository, JsonInventoryRepository>();
builder.Services.AddSingleton<IShipmentRepository, JsonShipmentRepository>();
builder.Services.AddSingleton<IMoneyOrderRepository, JsonMoneyOrderRepository>();
builder.Services.AddSingleton<ICounterTransactionRepository, JsonCounterTransactionRepository>();
builder.Services.AddSingleton<IShippingRateProvider, JsonShippingRateProvider>();
builder.Services.AddSingleton<IAuditStore, JsonAuditStore>();
builder.Services.AddScoped<AuditActionFilter>();
builder.Services.AddScoped<ValidationActionFilter>();
builder.Services.AddScoped<PerformanceActionFilter>();
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
builder.Services.AddSingleton<LocalStaffAuthenticator>();

var inventoryThreshold = builder.Configuration.GetValue<int?>("ApexPost:Inventory:LowStockThreshold") ?? 10;
var notificationRecipient = builder.Configuration["ApexPost:Inventory:LowStockRecipient"] ?? "operations-alerts@example.test";
var highValueThreshold = builder.Configuration.GetValue<decimal?>("ApexPost:MoneyOrders:HighValueThreshold") ?? 1000m;
var taxRate = builder.Configuration.GetValue<decimal?>("ApexPost:Transactions:TaxRate") ?? 0m;
builder.Services.AddSingleton(new InventoryServiceOptions { LowStockThreshold = inventoryThreshold, LowStockRecipient = notificationRecipient });
builder.Services.AddSingleton<IMoneyOrderPolicy>(new MoneyOrderPolicy(new Money(highValueThreshold)));
builder.Services.AddSingleton<ITaxPolicy>(new FixedRateTaxPolicy(taxRate));
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<ShippingWorkflowService>();
builder.Services.AddSingleton<MoneyOrderWorkflowService>();
builder.Services.AddSingleton<CounterTransactionWorkflowService>();
builder.Services.AddSingleton<BranchReportService>();

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
