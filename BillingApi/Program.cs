using BillingApi.Database;
using BillingApi.Models;
using BillingApi.Repositories;
using BillingApi.Services;
using Stripe;
using System.Security.Claims;

try { DotNetEnv.Env.TraversePath().Load(); } catch { }

var builder = WebApplication.CreateBuilder(args);

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
var connStr = builder.Configuration.GetConnectionString("Billing")
    ?? throw new InvalidOperationException("Missing ConnectionStrings__Billing");

DatabaseInitializer.Initialize(connStr);

builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<PricingService>();
builder.Services.AddSingleton<QuotaService>();
builder.Services.AddSingleton(new TenantRepository(connStr));
builder.Services.AddSingleton(new UsageRepository(connStr));
builder.Services.AddSingleton(new PlanRepository(connStr));
builder.Services.AddSingleton(new WebhookRepository(connStr));
builder.Services.AddSingleton(new SubscriptionRepository(connStr));
builder.Services.AddHttpClient();

var app = builder.Build();

// ---------- HELPERS ----------

string? ExtractToken(HttpContext http)
{
    var header = http.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer "))
        return null;
    return header["Bearer ".Length..];
}


int? GetTenantId(ClaimsPrincipal? principal)
{
    var claim = principal?.FindFirst("tenantId")?.Value;
    return int.TryParse(claim, out var id) ? id : null;
}

// ---------- ROOT ----------

app.MapGet("/", () => Results.Ok(new { message = "Billing API is running" }));

// ---------- AUTH ----------

app.MapPost("/auth/register", (AuthRequest req, TenantRepository repo, AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required" });

    if (repo.GetByEmail(req.Email) is not null)
        return Results.Conflict(new { error = "Email already registered" });

    var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
    var tenant = repo.Create(req.Email, hash);
    var token = auth.GenerateToken(tenant.Id, tenant.Email);

    return Results.Created("/auth/login", new { token, tenantId = tenant.Id });
});

app.MapPost("/auth/login", (AuthRequest req, TenantRepository repo, AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required" });

    var tenant = repo.GetByEmail(req.Email);
    if (tenant is null)
        return Results.Json(new { error = "Invalid credentials" }, statusCode: 401);

    if (!BCrypt.Net.BCrypt.Verify(req.Password, tenant.PasswordHash))
        return Results.Json(new { error = "Invalid credentials" }, statusCode: 401);

    var token = auth.GenerateToken(tenant.Id, tenant.Email);
    return Results.Ok(new { token, tenantId = tenant.Id });
});

// ---------- METERING ----------

app.MapPost("/meter", (MeterRequest req, HttpContext http,
    AuthService auth, UsageRepository usage, PlanRepository plans, TenantRepository tenants) =>
{
    // 1. Provjeri token
    var token = ExtractToken(http);
    if (token is null)
        return Results.Json(new { error = "Access token required" }, statusCode: 401);

    var principal = auth.ValidateToken(token);
    var tenantId = GetTenantId(principal);
    if (tenantId is null)
        return Results.Json(new { error = "Invalid token" }, statusCode: 401);

    // 2. Validacija inputa
    if (string.IsNullOrWhiteSpace(req.UsageType) || string.IsNullOrWhiteSpace(req.IdempotencyKey))
        return Results.BadRequest(new { error = "usage_type and idempotency_key are required" });

    if (req.Quantity <= 0)
        return Results.BadRequest(new { error = "quantity must be positive" });

    // 3. Provjeri kvotu
    var tenant = tenants.GetById(tenantId.Value);
    if (tenant is null)
        return Results.Json(new { error = "Tenant not found" }, statusCode: 401);

    var limits = plans.GetLimits(tenant.PlanId);
    if (limits is null)
        return Results.Json(new { error = "Plan not found" }, statusCode: 500);

    var currentUsage = usage.GetMonthlyUsage(tenantId.Value);

    // Odredi koji limit gledamo na osnovu tipa
    // "api_call" gleda api_call_limit, "token" gleda token_limit
    var currentAmount = currentUsage.GetValueOrDefault(req.UsageType, 0);
    var limit = req.UsageType == "api_call" ? limits.Value.apiCallLimit : limits.Value.tokenLimit;

    // Ako bi ovaj zahtjev prebacio limit → odbij
    if (currentAmount + req.Quantity > limit)
    {
        var statusCode = req.UsageType == "token" ? 402 : 429;
        return Results.Json(new
        {
            error = $"Quota exceeded for {req.UsageType}",
            used = currentAmount,
            limit = limit,
            requested = req.Quantity
        }, statusCode: statusCode);
    }

    // 4. Zapiši event (idempotentno)
    var recorded = usage.TryRecord(tenantId.Value, req.UsageType, req.Quantity, req.IdempotencyKey);

    return Results.Ok(new
    {
        recorded,
        message = recorded ? "Usage recorded" : "Duplicate — already recorded",
        used = currentAmount + (recorded ? req.Quantity : 0),
        limit
    });
});

app.MapPost("/api/ask", (AskRequest req, HttpContext http,
    AuthService auth, UsageRepository usage, PlanRepository plans, TenantRepository tenants, QuotaService quota) =>
{
    var token = ExtractToken(http);
    if (token is null)
        return Results.Json(new { error = "Access token required" }, statusCode: 401);

    var principal = auth.ValidateToken(token);
    var tenantId = GetTenantId(principal);

    if (tenantId is null)
        return Results.Json(new { error = "Invalid token" }, statusCode: 401);

    var tenant = tenants.GetById(tenantId.Value);
    if (tenant is null)
        return Results.Json(new { error = "Tenant not found" }, statusCode: 401);

    var limits = plans.GetLimits(tenant.PlanId);
    if (limits is null)
        return Results.Json(new { error = "Plan not found" }, statusCode: 500);

    var currentUsage = usage.GetMonthlyUsage(tenantId.Value);
    var callsUsed = currentUsage.GetValueOrDefault("api_call", 0);
    var answer = $"This is a simulated AI response to: {req.Question}";

    // GOTCHA #1 (cached input): when the caller reuses a previously sent context
    // (req.ReuseContext = true), the prompt prefix is served from cache and is
    // billed at the cheaper cached rate instead of the normal input rate.
    var promptTokens = req.Question.Split(' ').Length * 2;
    var inputTokens = req.ReuseContext ? 0 : promptTokens;
    var cachedInputTokens = req.ReuseContext ? promptTokens : 0;

    // GOTCHA #2 (reasoning): the model "thinks" before answering. Those reasoning
    // tokens are part of the output total, billed at the output rate — not a
    // separate additive category.
    var answerTokens = answer.Split(' ').Length * 2;
    var reasoningTokens = answerTokens / 2;
    var outputTokens = answerTokens + reasoningTokens;

    var totalTokens = inputTokens + cachedInputTokens + outputTokens;
    var tokensUsed = currentUsage.GetValueOrDefault("token", 0);

    var decision = quota.Check(
  callsUsed, limits.Value.apiCallLimit, callsRequested: 1,
  tokensUsed, limits.Value.tokenLimit, tokensRequested: totalTokens);

    if (decision == QuotaResult.ApiCallLimitExceeded)
        return Results.Json(new
        {
            error = "API call quota exceeded",
            used = callsUsed,
            limit = limits.Value.apiCallLimit
        }, statusCode: 429);

    if (decision == QuotaResult.TokenLimitExceeded)
        return Results.Json(new
        {
            error = "Token quota exceeded",
            used = tokensUsed,
            limit = limits.Value.tokenLimit
        }, statusCode: 402);

    var requestId = Guid.NewGuid().ToString();
    usage.TryRecord(tenantId.Value, "api_call", 1, $"{requestId}-call");
    usage.TryRecord(tenantId.Value, "token", totalTokens, $"{requestId}-token",
        inputTokens, cachedInputTokens, outputTokens);

    return Results.Ok(new
    {
        answer,
        tokensUsed = new { input = inputTokens, cached = cachedInputTokens, output = outputTokens, total = totalTokens }
    });
});

app.MapGet("/usage", (HttpContext http, AuthService auth, UsageRepository usage, PlanRepository plans, TenantRepository tenants, PricingService pricing) =>
{
    var token = ExtractToken(http);
    if (token is null)
        return Results.Json(new { error = "Access token required" }, statusCode: 401);

    var principal = auth.ValidateToken(token);
    var tenantId = GetTenantId(principal);
    if (tenantId is null)
        return Results.Json(new { error = "Invalid token" }, statusCode: 401);

    var tenant = tenants.GetById(tenantId.Value);
    if (tenant is null)
        return Results.Json(new { error = "Tenant not found" }, statusCode: 401);

    var limits = plans.GetLimits(tenant.PlanId);
    if (limits is null)
        return Results.Json(new { error = "Plan not found" }, statusCode: 500);

    var currentUsage = usage.GetMonthlyUsage(tenantId.Value);
    var callsUsed = currentUsage.GetValueOrDefault("api_call", 0);
    var tokensUsed = currentUsage.GetValueOrDefault("token", 0);

    // Price each token category correctly: cached input is cheaper, and reasoning
    // is already folded into the output total by /api/ask.
    var tokenBreakdown = usage.GetMonthlyTokenBreakdown(tenantId.Value);
    var cost = pricing.CalculateCost(
        apiCalls: callsUsed,
        inputTokens: tokenBreakdown.inputTokens,
        cachedInputTokens: tokenBreakdown.cachedInputTokens,
        outputTokens: tokenBreakdown.outputTokens);

    var planInfo = plans.GetById(tenant.PlanId);
    return Results.Ok(new
    {
        plan = planInfo?.Name ?? tenant.PlanId.ToString(),
        period = DateTime.UtcNow.ToString("yyyy-MM"),
        usage = new
        {
            api_calls = new { used = callsUsed, limit = limits.Value.apiCallLimit },
            tokens = new { used = tokensUsed, limit = limits.Value.tokenLimit }
        },
        estimatedCost = cost
    });

});

app.MapPost("/billing/checkout", (HttpContext http,
    AuthService auth, TenantRepository tenants, IConfiguration config) =>
{
    var token = ExtractToken(http);
    if (token is null)
        return Results.Json(new { error = "Access token required" }, statusCode: 401);

    var principal = auth.ValidateToken(token);
    var tenantId = GetTenantId(principal);
    if (tenantId is null)
        return Results.Json(new { error = "Invalid token" }, statusCode: 401);

    var tenant = tenants.GetById(tenantId.Value);
    if (tenant is null)
        return Results.Json(new { error = "Tenant not found" }, statusCode: 401);

    var options = new Stripe.Checkout.SessionCreateOptions
    {
        Mode = "subscription",
        LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
        {
            new(){Price=config["Stripe:ProPriceId"], Quantity=1}
        },
        SuccessUrl = "http://localhost:5095/billing/success?session_id={CHECKOUT_SESSION_ID}",
        CancelUrl = "http://localhost:5095/billing/cancel",
        Metadata = new Dictionary<string, string>
        {
            { "tenantId", tenant.Id.ToString() }
        },
        SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
        {
            Metadata = new Dictionary<string, string> { { "tenantId", tenant.Id.ToString() } }
        }
    };

    var service = new Stripe.Checkout.SessionService();
    var session = service.Create(options);

    return Results.Ok(new { checkoutUrl = session.Url });
});

app.MapPost("/webhooks/stripe", async (HttpContext http,
   WebhookRepository webhooks, TenantRepository tenants, SubscriptionRepository subscriptions, IConfiguration config) =>
 {
     http.Request.EnableBuffering();
     var json = await new StreamReader(http.Request.Body).ReadToEndAsync();
     http.Request.Body.Position = 0;

     var signature = http.Request.Headers["Stripe-Signature"];
     var webhookSecret = config["Stripe:WebhookSecret"];

     Stripe.Event stripeEvent;
     try
     {
         stripeEvent = Stripe.EventUtility.ConstructEvent(json, signature, webhookSecret, throwOnApiVersionMismatch: false);
     }
     catch (Exception ex)
     {
         return Results.Json(new { error = "Invalid signature", detail = ex.Message }, statusCode: 400);
     }

     var isNew = webhooks.TryMarkProcessed(stripeEvent.Id);
     if (!isNew)
         return Results.Ok(new { status = "already processed" });

     switch (stripeEvent.Type)
     {
         case "checkout.session.completed":
             {
                 var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                 if (session?.Metadata.TryGetValue("tenantId", out var tenantIdStr) == true
                     && int.TryParse(tenantIdStr, out var tenantId)
                     && session.SubscriptionId is not null)
                 {
                     tenants.UpdatePlan(tenantId, planId: 2);
                     tenants.UpdateStatus(tenantId, "active");
                     subscriptions.Upsert(tenantId, session.SubscriptionId, stripeEvent.Id, "active");
                 }
                 break;
             }
         case "customer.subscription.updated":
             {
                 var subscription = stripeEvent.Data.Object as Stripe.Subscription;
                 if (subscription is null) break;

                 var tenantId = subscriptions.UpdateStatusBySubscriptionId(
                     subscription.Id, subscription.Status, stripeEvent.Id);

                 if (tenantId is not null)
                 {
                     switch (subscription.Status)
                     {
                         case "active":
                         case "trialing":
                             tenants.UpdatePlan(tenantId.Value, planId: 2);
                             tenants.UpdateStatus(tenantId.Value, "active");
                             break;
                         case "past_due":
                         case "unpaid":
                             tenants.UpdateStatus(tenantId.Value, "past_due");
                             break;
                         case "canceled":
                         case "incomplete_expired":
                             tenants.UpdatePlan(tenantId.Value, planId: 1);
                             tenants.UpdateStatus(tenantId.Value, "canceled");
                             break;
                         default:
                             tenants.UpdateStatus(tenantId.Value, subscription.Status);
                             break;
                     }
                 }
                 break;
             }
         case "customer.subscription.deleted":
             {
                 var subscription = stripeEvent.Data.Object as Stripe.Subscription;
                 if (subscription is null) break;

                 var tenantId = subscriptions.UpdateStatusBySubscriptionId(
                     subscription.Id, "canceled", stripeEvent.Id);

                 if (tenantId is not null)
                 {
                     tenants.UpdatePlan(tenantId.Value, planId: 1);
                     tenants.UpdateStatus(tenantId.Value, "canceled");
                 }
                 break;
             }
         default:
             break;
     }

     return Results.Ok(new { status = "processed" });
 });



app.Run();

public sealed record MeterRequest(string UsageType, int Quantity, string IdempotencyKey);
public sealed record AskRequest(string Question, bool ReuseContext = false);