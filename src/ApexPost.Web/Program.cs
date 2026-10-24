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
using ApexPost.Web.Services;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
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
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();

public partial class Program;
