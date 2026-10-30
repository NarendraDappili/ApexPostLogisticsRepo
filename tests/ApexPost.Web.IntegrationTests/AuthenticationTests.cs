using System.Net;
using System.Text.RegularExpressions;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApexPost.Web.Middleware;
using ApexPost.Application.Auditing;

namespace ApexPost.Web.IntegrationTests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Unauthenticated_protected_route_redirects_to_login()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/CounterTransactions/New");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Active_staff_member_can_sign_in_with_a_hashed_password()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var response = await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Branch/Dashboard", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), header => header.Contains("httponly", StringComparison.OrdinalIgnoreCase) && header.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(AuthWebApplicationFactory.TestPassword, await factory.ReadDataFileAsync("staff-members.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_or_inactive_staff_cannot_sign_in()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var invalid = await SignInAsync(client, "STF-CLERK-01", "wrong-password");
        Assert.Equal(System.Net.HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Sign-in failed", await invalid.Content.ReadAsStringAsync());

        var inactive = await SignInAsync(client, "STF-INACTIVE-01", AuthWebApplicationFactory.TestPassword);
        Assert.Equal(System.Net.HttpStatusCode.OK, inactive.StatusCode);
        Assert.Contains("Sign-in failed", await inactive.Content.ReadAsStringAsync());

        var unknown = await SignInAsync(client, "STF-NOBODY", AuthWebApplicationFactory.TestPassword);
        Assert.Equal(System.Net.HttpStatusCode.OK, unknown.StatusCode);
        Assert.Contains("Sign-in failed", await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cookie_is_rejected_when_staff_becomes_inactive()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        await factory.DeactivateClerkAsync();
        var response = await client.GetAsync("/CounterTransactions/New");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Clerk_cannot_access_another_branch_or_privileged_report()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var otherBranch = await client.GetAsync("/Catalogue?branchCode=BR-SOUTH");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, otherBranch.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", otherBranch.Headers.Location?.AbsolutePath);

        var reports = await client.GetAsync("/Reports");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, reports.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", reports.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Workflow_screens_use_the_signed_in_clerks_branch_and_role_appropriate_navigation()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var dashboard = await client.GetStringAsync("/Branch/Dashboard");
        var catalogue = await client.GetStringAsync("/Catalogue");
        var shipping = await client.GetStringAsync("/Shipping/Create");
        var moneyOrders = await client.GetStringAsync("/MoneyOrders/Create");
        var basket = await client.GetStringAsync("/CounterTransactions/New");

        Assert.Contains("value=\"BR-NORTH\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Demo North Branch", dashboard, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", catalogue, StringComparison.Ordinal);
        Assert.Contains("Demo Retail Envelope", catalogue, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", shipping, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", moneyOrders, StringComparison.Ordinal);
        Assert.Contains("Counter basket", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain(">Reports</a>", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain(">Staff</a>", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"ClerkId\"", basket, StringComparison.Ordinal);
        // Prices, fees and verification outcomes are never form fields.
        Assert.DoesNotContain("UnitPrice", basket, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Fee\"", moneyOrders, StringComparison.Ordinal);
        Assert.DoesNotContain("VerificationOutcome", moneyOrders, StringComparison.Ordinal);
        Assert.Contains("name=\"RetailLines[4].ProductId\"", basket, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Branch_manager_cannot_load_reports_for_another_branch_by_direct_url()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);

        var response = await client.GetAsync("/Reports?branchCode=BR-SOUTH");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Checkout_post_with_several_retail_lines_redirects_and_reduces_stock()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var transactionId = $"TX-WEB-{Guid.NewGuid():N}";

        var response = await CheckoutAsync(client, transactionId, [("PRD-001", 2), ("PRD-001", 1)]);

        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        Assert.Equal("/CounterTransactions/New", response.Headers.Location?.OriginalString);
        var confirmation = await client.GetStringAsync(response.Headers.Location
            ?? throw new InvalidOperationException("Successful checkout did not return a redirect location."));
        Assert.True(confirmation.Contains($"Transaction {transactionId.ToUpperInvariant()} was paid. Total: 30.00.", StringComparison.Ordinal), confirmation);
        Assert.Equal(97, await factory.GetNorthBranchStockAsync());
    }

    [Fact]
    public async Task Checkout_rejects_a_post_without_antiforgery_token()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var response = await client.PostAsync("/CounterTransactions/New", new FormUrlEncodedContent([
            new("TransactionId", "TX-WEB-002"),
            new("BranchCode", "BR-NORTH"),
            new("CounterCode", "C-01"),
            new("PaymentMethod", "0"),
            new("RetailLines[0].ProductId", "PRD-001"),
            new("RetailLines[0].Quantity", "1")
        ]));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100, await factory.GetNorthBranchStockAsync());
    }

    [Fact]
    public async Task A_shipment_created_for_a_transaction_is_billed_in_the_basket_at_the_configured_rate()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var transactionId = $"TX-WEB-{Guid.NewGuid():N}";
        var shipmentId = $"SHP-WEB-{Guid.NewGuid():N}";
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await CreateShipmentAsync(client, shipmentId, transactionId, weight: "4")).StatusCode);

        var response = await CheckoutAsync(client, transactionId, [], shipmentIds: shipmentId);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Total: 12.00.", await client.GetStringAsync("/CounterTransactions/New"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shipping_weight_limit_and_unverified_high_value_money_order_errors_are_shown()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var shippingResponse = await CreateShipmentAsync(client, "SHP-WEB-TOO-HEAVY", "TX-WEB-HEAVY", weight: "6");
        var shippingPage = await shippingResponse.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, shippingResponse.StatusCode);
        Assert.True(shippingPage.Contains("configured service-level weight limit", StringComparison.OrdinalIgnoreCase), shippingPage);

        var issued = await CreateMoneyOrderAsync(client, "MO-WEB-VERIFY", "TX-WEB-VERIFY", "1500");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, issued.StatusCode);
        Assert.Equal("/MoneyOrders/Verify/MO-WEB-VERIFY", issued.Headers.Location?.OriginalString);

        var checkout = await CheckoutAsync(client, "TX-WEB-VERIFY", [], moneyOrderIds: "MO-WEB-VERIFY");
        Assert.Equal(System.Net.HttpStatusCode.OK, checkout.StatusCode);
        Assert.Contains("requires successful same-transaction verification", await checkout.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Eligible_shipment_post_redirects_to_label_with_configured_charge()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var shipmentId = $"SHP-WEB-{Guid.NewGuid():N}";

        var response = await CreateShipmentAsync(client, shipmentId, "TX-WEB-LABEL", weight: "4");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Shipping/Label/", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var label = await client.GetStringAsync(response.Headers.Location
            ?? throw new InvalidOperationException("Successful shipment did not redirect to its label."));
        Assert.Contains("12.00", label, StringComparison.Ordinal);
        Assert.Contains(shipmentId.ToUpperInvariant(), label, StringComparison.Ordinal);
        Assert.Contains("TX-WEB-LABEL", label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task High_value_money_order_is_verified_in_a_separate_step_and_completed_in_the_basket()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var orderId = $"MO-WEB-{Guid.NewGuid():N}".ToUpperInvariant();
        var transactionId = $"TX-WEB-{Guid.NewGuid():N}".ToUpperInvariant();
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await CreateMoneyOrderAsync(client, orderId, transactionId, "1500")).StatusCode);

        var token = await GetAntiforgeryTokenAsync(client, $"/MoneyOrders/Verify/{orderId}");
        var verified = await client.PostAsync("/MoneyOrders/Verify", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token),
            new("MoneyOrderId", orderId),
            new("DocumentType", "Passport"),
            new("DocumentNumber", "SYNTH1234")
        ]));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, verified.StatusCode);
        var page = await client.GetStringAsync(verified.Headers.Location!);
        Assert.Contains("Identity verification for " + orderId + ": Verified.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTH1234", page, StringComparison.Ordinal);
        var saved = await factory.FindMoneyOrderAsync(orderId);
        Assert.Equal(IdentityVerificationOutcome.Verified, saved.IdentityVerificationOutcome);
        Assert.Equal("****1234", saved.IdentityVerification?.MaskedDocumentReference.Value);
        Assert.Equal(17.50m, saved.Fee.Amount);
        Assert.DoesNotContain("SYNTH1234", await factory.ReadDataFileAsync("money-orders.json"), StringComparison.Ordinal);

        var checkout = await CheckoutAsync(client, transactionId, [], moneyOrderIds: orderId);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, checkout.StatusCode);
        Assert.Contains("Total: 1517.50.", await client.GetStringAsync("/CounterTransactions/New"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_show_empty_states_for_a_branch_without_activity()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedEmptyReportBranchAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-EMPTY-MANAGER", AuthWebApplicationFactory.TestPassword);

        var response = await client.GetAsync("/Reports?day=2020-01-01");
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Daily sales: 2020-01-01", page, StringComparison.Ordinal);
        Assert.Contains("No paid transactions were recorded", page, StringComparison.Ordinal);
        Assert.Contains("No products are at or below the low-stock threshold", page, StringComparison.Ordinal);
        Assert.Contains("No shipments are recorded for this branch", page, StringComparison.Ordinal);
        Assert.Contains("No money orders are recorded for this branch", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clerk_receives_forbidden_response_for_void_endpoint()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var response = await client.PostAsync("/CounterTransactions/Void", new FormUrlEncodedContent([]));
        var refund = await client.PostAsync("/CounterTransactions/Refund", new FormUrlEncodedContent([]));

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, refund.StatusCode);
    }

    [Fact]
    public async Task Branch_manager_can_void_a_paid_sale_from_the_default_configuration_and_stock_is_returned()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await CheckoutAsync(clerk, "TX-WEB-VOID", [("PRD-001", 4)])).StatusCode);
        Assert.Equal(96, await factory.GetNorthBranchStockAsync());

        using var manager = factory.CreateClient();
        await SignInAsync(manager, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(manager, "/CounterTransactions/Manage");
        var response = await manager.PostAsync("/CounterTransactions/Void", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("TransactionId", "TX-WEB-VOID"), new("Reason", "Keyed at the wrong counter") ]));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/CounterTransactions/Manage", response.Headers.Location?.OriginalString);
        var page = await manager.GetStringAsync("/CounterTransactions/Manage");
        Assert.Contains("Transaction TX-WEB-VOID voided.", page, StringComparison.Ordinal);
        Assert.Contains("Voided (Keyed at the wrong counter)", page, StringComparison.Ordinal);
        Assert.Equal(100, await factory.GetNorthBranchStockAsync());
    }

    [Fact]
    public async Task Branch_manager_is_authorized_to_branch_report()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);
        var response = await client.GetAsync("/Reports");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_can_add_and_deactivate_staff_and_other_roles_are_denied()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var denied = await clerk.GetAsync("/StaffAdmin");
        Assert.StartsWith("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(admin, "/StaffAdmin/Create");
        var created = await admin.PostAsync("/StaffAdmin/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", "STF-NEW-01"), new("Name", "New Clerk"),
            new("Email", "new.clerk@example.test"), new("Role", "0"), new("BranchCode", "BR-NORTH"), new("Password", "new-clerk-password") ]));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, created.StatusCode);

        using var newClerk = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await SignInAsync(newClerk, "STF-NEW-01", "new-clerk-password")).StatusCode);

        token = await GetAntiforgeryTokenAsync(admin, "/StaffAdmin");
        var deactivated = await admin.PostAsync("/StaffAdmin/Deactivate/STF-NEW-01", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, deactivated.StatusCode);
        Assert.Contains("Inactive", await admin.GetStringAsync("/StaffAdmin"), StringComparison.Ordinal);
        Assert.StartsWith("/Account/Login", (await newClerk.GetAsync("/CounterTransactions/New")).Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Administrator_can_add_and_close_a_branch()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(admin, "/BranchAdmin/Create");

        var created = await admin.PostAsync("/BranchAdmin/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("BranchCode", "BR-HARBOUR"), new("Name", "Harbour Branch"),
            new("Region", "Coast"), new("OpeningHours", "Mon-Fri 09:00-17:00"), new("CounterCount", "2") ]));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, created.StatusCode);

        token = await GetAntiforgeryTokenAsync(admin, "/BranchAdmin");
        var closed = await admin.PostAsync("/BranchAdmin/Close/BR-HARBOUR", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, closed.StatusCode);
        var list = await admin.GetStringAsync("/BranchAdmin");
        Assert.Contains("Harbour Branch", list, StringComparison.Ordinal);
        Assert.Contains("Branch BR-HARBOUR is now closed.", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrator_can_view_a_branch_they_created_and_pick_it_from_the_branch_dropdown()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        await factory.SeedSouthBranchAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01", AuthWebApplicationFactory.TestPassword);

        var dashboard = await admin.GetAsync("/Branch/Dashboard?branchCode=BR-SOUTH");
        var page = await dashboard.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("Demo South Branch", page, StringComparison.Ordinal);
        Assert.Contains("<option selected=\"selected\" value=\"BR-SOUTH\">", page, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", page, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await admin.GetAsync("/Catalogue?branchCode=BR-SOUTH")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await admin.GetAsync("/Reports?branchCode=BR-SOUTH")).StatusCode);
    }

    [Fact]
    public async Task Clerk_branch_dropdown_lists_only_the_home_branch_and_forms_offer_stored_values()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        await factory.SeedSouthBranchAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var dashboard = await client.GetStringAsync("/Branch/Dashboard");
        var basket = await client.GetStringAsync("/CounterTransactions/New");
        var shipping = await client.GetStringAsync("/Shipping/Create");

        Assert.DoesNotContain("value=\"BR-SOUTH\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("<option value=\"C-01\">C-01</option>", basket, StringComparison.Ordinal);
        Assert.Contains("<option value=\"PRD-001\">PRD-001 - Demo Retail Envelope (10.00)</option>", basket, StringComparison.Ordinal);
        Assert.Contains("<option value=\"ZONE-01\">ZONE-01</option>", shipping, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_records_successful_authenticated_action_with_correlation_and_no_sensitive_input()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/CounterTransactions/New");
        request.Headers.Add("X-Correlation-Id", "audit-success-1");
        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var record = (await factory.GetAuditRecordsAsync()).Last(entry => entry.CorrelationId == "audit-success-1");
        Assert.Equal("STF-CLERK-01", record.StaffId);
        Assert.Equal("CounterClerk", record.Role);
        Assert.Equal("BR-NORTH", record.BranchCode);
        Assert.Equal("CounterTransactions", record.Controller);
        Assert.Equal("New", record.Action);
        Assert.Equal(200, record.StatusCode);
        Assert.Equal("Succeeded", record.Outcome);
        Assert.DoesNotContain("password", record.RouteValues.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("document", record.RouteValues.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_model_returns_validation_view_and_is_audited_without_password()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        var response = await client.PostAsync("/Account/Login?documentNumber=FULL-DOCUMENT-1234&apiKey=not-an-audit-key", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", ""), new("Password", "do-not-audit-this-password") ]));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("required", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var record = (await factory.GetAuditRecordsAsync()).Last(entry => entry.Controller == "Account" && entry.Action == "Login");
        Assert.Equal("Failed", record.Outcome);
        var serializedRecord = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain("do-not-audit-this-password", serializedRecord, StringComparison.Ordinal);
        Assert.DoesNotContain("FULL-DOCUMENT-1234", serializedRecord, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-audit-key", serializedRecord, StringComparison.Ordinal);
        var logs = string.Join(Environment.NewLine, factory.LogMessages);
        Assert.DoesNotContain("do-not-audit-this-password", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("FULL-DOCUMENT-1234", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-audit-key", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Performance_filter_warns_when_threshold_is_zero()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        await client.GetAsync("/CounterTransactions/New");

        Assert.Contains(factory.LogMessages, message => message.Contains("MVC action CounterTransactions/New took", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string staffId, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", staffId), new("Password", password) ]));
    }

    private static async Task<HttpResponseMessage> CheckoutAsync(HttpClient client, string transactionId, IReadOnlyList<(string ProductId, int Quantity)> retailLines, string shipmentIds = "", string moneyOrderIds = "")
    {
        var token = await GetAntiforgeryTokenAsync(client, "/CounterTransactions/New");
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("TransactionId", transactionId),
            new("BranchCode", "BR-NORTH"),
            new("CounterCode", "C-01"),
            new("PaymentMethod", "0"),
            new("ShipmentIds", shipmentIds),
            new("MoneyOrderIds", moneyOrderIds)
        };
        for (var index = 0; index < retailLines.Count; index++)
        {
            fields.Add(new($"RetailLines[{index}].ProductId", retailLines[index].ProductId));
            fields.Add(new($"RetailLines[{index}].Quantity", retailLines[index].Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return await client.PostAsync("/CounterTransactions/New", new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> CreateShipmentAsync(HttpClient client, string shipmentId, string transactionId, string weight)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Shipping/Create");
        return await client.PostAsync("/Shipping/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token),
            new("ShipmentId", shipmentId),
            new("TransactionId", transactionId),
            new("OriginBranchCode", "BR-NORTH"),
            new("DestinationZoneCode", "ZONE-01"),
            new("ShipmentType", "1"),
            new("ServiceLevel", "0"),
            new("WeightKilograms", weight),
            new("LengthCentimetres", "10"),
            new("WidthCentimetres", "10"),
            new("HeightCentimetres", "10")
        ]));
    }

    private static async Task<HttpResponseMessage> CreateMoneyOrderAsync(HttpClient client, string orderId, string transactionId, string amount)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/MoneyOrders/Create");
        return await client.PostAsync("/MoneyOrders/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token),
            new("MoneyOrderId", orderId),
            new("BranchCode", "BR-NORTH"),
            new("Amount", amount),
            new("Sender", "Synthetic Sender"),
            new("Recipient", "Synthetic Recipient"),
            new("TransactionContextId", transactionId)
        ]));
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        => await GetAntiforgeryTokenAsync(client, "/Account/Login");

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var page = await client.GetStringAsync(path);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string TestPassword = "test-only-password";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "ApexPost-WebTests", Guid.NewGuid().ToString("N"));
    private readonly TestLoggerProvider _loggerProvider = new();

    public IReadOnlyCollection<string> LogMessages => _loggerProvider.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting is visible to values Program.cs reads before Build(); ConfigureAppConfiguration is not,
        // and without this the suite would write into the web project's own App_Data folder.
        builder.UseSetting("ApexPost:DataDirectory", _dataDirectory);
        builder.UseSetting("ApexPost:RateLimiting:LoginPermitLimit", "100");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApexPost:DemoSeed:Enabled"] = "false",
            ["ApexPost:Performance:WarningThresholdMilliseconds"] = "0"
        }));
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddProvider(_loggerProvider); });
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<IntegrationSecurityOptions>(options => options.AllowedIps = ["127.0.0.1", "::1"]);
            services.RemoveAll<IShippingRateProvider>();
            services.AddSingleton<IShippingRateProvider>(new TestShippingRateProvider());
            // A lower PBKDF2 work factor keeps the suite fast; production uses the default.
            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(10_000));
            // The test server has no socket, so give requests the loopback address a local browser would have.
            services.AddTransient<IStartupFilter, LoopbackClientStartupFilter>();
        });
    }

    public new HttpClient CreateClient() => base.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var branches = scope.ServiceProvider.GetRequiredService<IBranchRepository>();
        var counters = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
        var products = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await branches.SaveAsync(new Branch(new BranchCode("BR-NORTH"), "Demo North Branch"));
        await counters.SaveAsync(new Counter(new CounterCode("C-01"), new BranchCode("BR-NORTH")));
        await staff.SaveAsync(WithPassword(new StaffMember(new StaffId("STF-CLERK-01"), "Test Clerk", StaffRole.CounterClerk, new BranchCode("BR-NORTH")), hasher));
        var inactive = WithPassword(new StaffMember(new StaffId("STF-INACTIVE-01"), "Inactive Clerk", StaffRole.CounterClerk, new BranchCode("BR-NORTH")), hasher);
        inactive.Deactivate();
        await staff.SaveAsync(inactive);
        await staff.SaveAsync(WithPassword(new StaffMember(new StaffId("STF-MANAGER-01"), "Test Manager", StaffRole.BranchManager, new BranchCode("BR-NORTH")), hasher));
        await staff.SaveAsync(WithPassword(new StaffMember(new StaffId("STF-ADMIN-01"), "Test Administrator", StaffRole.Administrator, new BranchCode("BR-NORTH"), "admin@example.test"), hasher));
        await products.SaveAsync(new Product(new ProductId("PRD-001"), "Demo Retail Envelope", new Money(10m)));
        await inventory.SaveAsync(new BranchInventoryItem(new BranchCode("BR-NORTH"), new ProductId("PRD-001"), 100));
    }

    public async Task SeedSouthBranchAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBranchRepository>()
            .SaveAsync(new Branch(new BranchCode("BR-SOUTH"), "Demo South Branch"));
    }

    public async Task SeedEmptyReportBranchAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBranchRepository>()
            .SaveAsync(new Branch(new BranchCode("BR-EMPTY"), "Empty Report Branch"));
        await scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>()
            .SaveAsync(WithPassword(new StaffMember(new StaffId("STF-EMPTY-MANAGER"), "Empty Report Manager", StaffRole.BranchManager, new BranchCode("BR-EMPTY")), scope.ServiceProvider.GetRequiredService<IPasswordHasher>()));
    }

    public async Task<int> GetNorthBranchStockAsync()
    {
        using var scope = Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        return (await inventory.FindAsync(new BranchCode("BR-NORTH"), new ProductId("PRD-001")))?.Quantity
            ?? throw new InvalidOperationException("North-branch test stock was not seeded.");
    }

    public async Task<MoneyOrder> FindMoneyOrderAsync(string id)
    {
        using var scope = Services.CreateScope();
        var moneyOrders = scope.ServiceProvider.GetRequiredService<IMoneyOrderRepository>();
        return await moneyOrders.FindByIdAsync(new MoneyOrderId(id))
            ?? throw new InvalidOperationException("The test money order was not persisted.");
    }

    public async Task DeactivateClerkAsync()
    {
        using var scope = Services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
        var clerk = await staff.FindByIdAsync(new StaffId("STF-CLERK-01")) ?? throw new InvalidOperationException("Test clerk was not seeded.");
        clerk.Deactivate();
        await staff.SaveAsync(clerk);
    }

    public async Task<IReadOnlyList<AuditRecord>> GetAuditRecordsAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditStore>().ReadAsync();
    }

    public Task<string> ReadDataFileAsync(string fileName) => File.ReadAllTextAsync(Path.Combine(_dataDirectory, fileName));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private static StaffMember WithPassword(StaffMember member, IPasswordHasher hasher)
    {
        member.SetPasswordHash(hasher.Hash(TestPassword));
        return member;
    }

    private sealed class TestShippingRateProvider : IShippingRateProvider
    {
        public ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ShippingRate>>([
                new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, new ZoneCode("ZONE-01"), new Money(12m), new ParcelWeight(5m))
            ]);
    }

    private sealed class LoopbackClientStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
    public IReadOnlyCollection<string> Messages => _messages.ToArray();
    public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);
    public void Dispose() { }

    private sealed class TestLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
