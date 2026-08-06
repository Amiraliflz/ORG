using Application.Data;
using Application.Services;
using Application.Services.Auth;
using Application.Services.MrShooferORS;
using Application.Services.Payment;
using Application.Services.Seo;
using Kavenegar;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading.RateLimiting;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddSingleton<DirectionsRepository, DirectionsRepository>();
builder.Services.AddSingleton<DirectionsTravelTimeCalculator>();

// Configure MrShooferAPIClient via IHttpClientFactory — connection pooling prevents socket exhaustion
// UseCookies=false avoids CookieContainer domain lookup crashes on some macOS/dev hosts (GetDomainName: -1)
builder.Services.AddHttpClient<MrShooferAPIClient>((serviceProvider, client) =>
{
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["MrShoofer:ApiBaseUrl"] ?? "https://ors.shoofer.taxi");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = false,
    // Avoid Cursor/dev HTTP_PROXY sandboxes breaking ORS calls
    UseProxy = false,
    Proxy = null,
    // ORS edge rejects some default negotiations; pin modern TLS
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
        | System.Security.Authentication.SslProtocols.Tls13
});

builder.Services.AddHttpClient<CustomerServiceSmsSender>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddTransient<Application.Services.TicketIssuer>();
builder.Services.AddScoped<Application.Services.CustomerBalanceService>();

// Register Payment Service with Dependency Inversion Principle
// This allows easy swapping to other payment providers (IDPay, Sep, etc.)
builder.Services.AddHttpClient<IPaymentService, ZarinpalService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
});

var mvcBuilder = builder.Services
  .AddControllersWithViews()
  .AddJsonOptions(opts =>
  {
    // Ensure Persian characters are not escaped in JSON responses
    opts.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
  });

if (builder.Environment.IsDevelopment())
{
  mvcBuilder.AddRazorRuntimeCompilation();
}


builder.Services.TryAddTransient<IOtpLogin, SmsIrOtp>();

// Configure EF Core to use PostgreSQL via Npgsql and read the proper connection string per environment
var connStringName = builder.Environment.IsDevelopment() ? "development" : "production";
var pgsqlConnString = builder.Configuration.GetConnectionString(connStringName);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(pgsqlConnString);
});

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
  options.Password.RequiredLength = 6;
  options.Password.RequireDigit = false;
  options.Password.RequireNonAlphanumeric = false;
  options.Password.RequireUppercase = false;
  options.Password.RequiredUniqueChars = 0;
  options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
  options.AddPolicy("Agency", policy =>
      policy.RequireClaim("Role", "Agency"));

  options.AddPolicy("Admin", policy =>
      policy.RequireClaim("Role", "Admin"));

  options.AddPolicy("Customer", policy =>
      policy.RequireClaim("Role", "Customer"));
});

builder.Services.ConfigureApplicationCookie(options =>
{
  options.AccessDeniedPath = "/Auth/AccessDenied";
  options.Cookie.Name = "YourAppCookieName";
  options.Cookie.HttpOnly = true;
  options.ExpireTimeSpan = TimeSpan.FromDays(75);
  options.LoginPath = "/Auth/Login";

  options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter;
  options.SlidingExpiration = true;
});

// Configure rate limiting
builder.Services.AddRateLimiter(options =>
{
  options.AddPolicy("ContactUsPolicy", context =>
      RateLimitPartition.GetFixedWindowLimiter(
          partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
          factory: partition => new FixedWindowRateLimiterOptions
          {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2
          }));
});


var app = builder.Build();

// SEO generated catalogs resolve against wwwroot/json/Seo/
SeoDataPaths.Configure(app.Environment.WebRootPath);

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
  app.UseDeveloperExceptionPage();
}
else
{
  app.UseExceptionHandler("/Home/Error");
  app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");

// Ensure Zarinpal receives the origin domain as referrer (avoids the "میان‌پی" interstitial)
app.Use(async (context, next) =>
{
  context.Response.Headers["Referrer-Policy"] = "strict-origin";
  await next();
});

// Auto-set partner_brand cookie when request comes through the ISIC-branded port (X-Partner header set by nginx)
app.Use(async (context, next) =>
{
    var partner = context.Request.Headers["X-Partner"].FirstOrDefault();
    if (!string.IsNullOrEmpty(partner) && !context.Request.Cookies.ContainsKey("partner_brand"))
    {
        context.Response.Cookies.Append("partner_brand", partner.ToLower(), new CookieOptions
        {
            HttpOnly = false,
            Secure   = false,
            SameSite = SameSiteMode.Lax,
            Expires  = DateTimeOffset.UtcNow.AddDays(30)
        });
    }
    await next();
});


app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Explicit SEO routes (must stay above the AgencyArea catch-all)
app.MapControllerRoute(
    name: "seo-sitemap-index",
    pattern: "sitemap.xml",
    defaults: new { controller = "Seo", action = "SitemapIndex" });
app.MapControllerRoute(
    name: "seo-sitemap-pages",
    pattern: "sitemap-pages.xml",
    defaults: new { controller = "Seo", action = "SitemapPages" });
app.MapControllerRoute(
    name: "seo-sitemap-routes",
    pattern: "sitemap-routes.xml",
    defaults: new { controller = "Seo", action = "SitemapRoutes" });
app.MapControllerRoute(
    name: "seo-sitemap-cities",
    pattern: "sitemap-cities.xml",
    defaults: new { controller = "Seo", action = "SitemapCities" });

app.MapAreaControllerRoute(
    name: "seo-routes-hub",
    areaName: "AgencyArea",
    pattern: "routes",
    defaults: new { controller = "Routes", action = "Index" });

app.MapAreaControllerRoute(
    name: "seo-routes-detail",
    areaName: "AgencyArea",
    pattern: "routes/{slug}",
    defaults: new { controller = "Routes", action = "Detail" });

app.MapAreaControllerRoute(
    name: "seo-cities-hub",
    areaName: "AgencyArea",
    pattern: "cities",
    defaults: new { controller = "Cities", action = "Index" });

app.MapAreaControllerRoute(
    name: "seo-cities-detail",
    areaName: "AgencyArea",
    pattern: "cities/{slug}",
    defaults: new { controller = "Cities", action = "Detail" });

app.MapAreaControllerRoute(
    name: "admin",
    areaName: "Admin",
    pattern: "Admin/{controller=Home}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "agency",
    areaName: "AgencyArea",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
