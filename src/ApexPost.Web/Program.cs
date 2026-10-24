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
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

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
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();

public partial class Program;
