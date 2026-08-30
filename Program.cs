using Application.Data;
using Application.Services;
using Application.Services.Auth;
using Application.Services.MrShooferORS;
using Application.Services.Ops;
using Application.Services.Payment;
using Application.Services.Seo;
using Kavenegar;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<LogBufferService>();
builder.Host.UseSerilog((context, services, configuration) =>
{
    var buffer = services.GetRequiredService<LogBufferService>();
    var logPath = context.HostingEnvironment.IsDevelopment()
        ? Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "app-.log")
        : "/var/log/org/app-.log";

    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .WriteTo.Console()
        .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
        .WriteTo.Sink(new DatabaseLogSink(buffer));
});

builder.Services.AddResponseCaching();
builder.Services.AddResponseCompression(options =>
{
  options.EnableForHttps = true;
  options.Providers.Add<BrotliCompressionProvider>();
  options.Providers.Add<GzipCompressionProvider>();
  options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
  [
    "application/javascript",
    "application/json",
    "image/svg+xml",
    "image/x-icon"
  ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// Trust reverse-proxy proto/host so HTTPS redirects + HSTS see the public scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
  // Do not take X-Forwarded-Host — CDN rewriting Host to .com would hide .ir
  // and stop the 301 Google needs for the mrshoofer.ir Search Console property.
  options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
  options.KnownNetworks.Clear();
  options.KnownProxies.Clear();
});

// Add services to the container.
builder.Services.Configure<Application.Services.Seo.SeoOptions>(
  builder.Configuration.GetSection(Application.Services.Seo.SeoOptions.SectionName));

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddSingleton<DirectionsRepository, DirectionsRepository>();
builder.Services.AddScoped<DirectionsTravelTimeCalculator>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Application.Services.MapBook.MapBookGeoCache>();
builder.Services.AddScoped<Application.Services.MapBook.RoadSnapService>();
builder.Services.AddScoped<Application.Services.MapBook.BuildingPlaqueService>();
builder.Services.AddSingleton<Application.Services.MapBook.PublicVenueService>();

builder.Services.Configure<Application.Services.Neshan.NeshanOptions>(
  builder.Configuration.GetSection(Application.Services.Neshan.NeshanOptions.SectionName));
builder.Services.AddHttpClient<Application.Services.Neshan.NeshanApiClient>((sp, client) =>
{
  var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Services.Neshan.NeshanOptions>>().Value;
  var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "https://api.neshan.org" : opts.BaseUrl.TrimEnd('/') + "/";
  client.BaseAddress = new Uri(baseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
  UseCookies = false,
  UseProxy = false,
  Proxy = null
});

builder.Services.AddScoped<Application.Services.TravelTime.ITravelTimeSyncService, Application.Services.TravelTime.TravelTimeSyncService>();
builder.Services.AddHostedService<Application.Services.TravelTime.TravelTimeSyncHostedService>();

builder.Services.AddSingleton<Application.Services.Homepage.IHomepageCatalogCache, Application.Services.Homepage.HomepageCatalogCache>();
builder.Services.AddHostedService<Application.Services.Homepage.HomepageCatalogSyncHostedService>();

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
builder.Services.AddScoped<Application.Services.LoyaltyService>();
builder.Services.AddScoped<IBusinessEventLogger, BusinessEventLogger>();
builder.Services.AddScoped<PlatformAnalyticsService>();
builder.Services.AddScoped<OpsStatusService>();
builder.Services.AddSingleton<IServiceRestarter, ServiceRestarter>();
builder.Services.AddSingleton<IOpsMobileTokenService, OpsMobileTokenService>();
builder.Services.AddHostedService<LogPersistenceWorker>();
builder.Services.AddHostedService<HealthSnapshotWorker>();
builder.Services.AddHttpClient("OpsHealthCheck", c => c.Timeout = TimeSpan.FromSeconds(8));

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

#if DEBUG
if (builder.Environment.IsDevelopment())
{
  mvcBuilder.AddRazorRuntimeCompilation();
}
#endif


builder.Services.TryAddTransient<IOtpLogin, SmsIrOtp>();

// Configure EF Core to use PostgreSQL via Npgsql and read the proper connection string per environment
var connStringName = builder.Environment.IsDevelopment() ? "development" : "production";
var pgsqlConnString = builder.Configuration.GetConnectionString(connStringName);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(pgsqlConnString);
});

builder.Services.AddHealthChecks()
    .AddNpgSql(pgsqlConnString!, name: "database", timeout: TimeSpan.FromSeconds(5));

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
  options.Cookie.SameSite = SameSiteMode.Lax;
  options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
        diagnosticContext.Set("RequestMethod", httpContext.Request.Method);
        diagnosticContext.Set("UserId", httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
    };
});

SeoDefaults.Configure(app.Configuration);

// SEO generated catalogs resolve against wwwroot/json/Seo/
SeoDataPaths.Configure(app.Environment.WebRootPath);

app.UseForwardedHeaders();
app.UseRateLimiter();

app.UseMiddleware<Application.Services.Seo.CanonicalHostMiddleware>();

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

// Arvan terminates TLS; origin is HTTP. Always advertise HSTS on public responses
// (UseHsts alone may not emit when the CDN/proxy chain omits forwarded proto).
app.Use(async (context, next) =>
{
  await next();
  if (app.Environment.IsDevelopment()) return;
  if (context.Response.HasStarted) return;
  if (context.Response.StatusCode is < 200 or >= 400) return;
  if (context.Response.Headers.ContainsKey("Strict-Transport-Security")) return;
  context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
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


// HTTP→HTTPS is done at Arvan. Origin is HTTP :80/:8080; UseHttpsRedirection
// would 301 https://mrshoofer.com/ to itself (ERR_TOO_MANY_REDIRECTS).
if (app.Environment.IsDevelopment())
{
  app.UseHttpsRedirection();
}
app.UseResponseCaching();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
  OnPrepareResponse = ctx =>
  {
    var path = ctx.Context.Request.Path.Value ?? "";
    var hasVersion = ctx.Context.Request.QueryString.HasValue;
    if (app.Environment.IsDevelopment())
    {
      // Avoid stale CSS/JS while iterating locally (browser ignored file edits with max-age=86400).
      // Keep the large Neshan Mapbox vendor + static MapBook JSON warm across reloads.
      var isMapBookVendor = path.Contains("NeshanMapboxGl", StringComparison.OrdinalIgnoreCase)
        || path.Contains("@neshan-maps-platform", StringComparison.OrdinalIgnoreCase);
      var isMapBookData = path.StartsWith("/data/iran/", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
      if (isMapBookVendor || isMapBookData)
      {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
        return;
      }
      if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
          || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
      {
        ctx.Context.Response.Headers.CacheControl = "no-cache";
        return;
      }
    }
    if (hasVersion || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
    {
      ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
    else if (path.StartsWith("/data/", StringComparison.OrdinalIgnoreCase)
             && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
      ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
    }
    else if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
             || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
    {
      ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
    }
  }
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

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
app.MapControllerRoute(
    name: "seo-sitemap-guides",
    pattern: "sitemap-guides.xml",
    defaults: new { controller = "Seo", action = "SitemapGuides" });

app.MapAreaControllerRoute(
    name: "seo-routes-hub",
    areaName: "AgencyArea",
    pattern: "routes",
    defaults: new { controller = "Routes", action = "Index" });

app.MapAreaControllerRoute(
    name: "seo-routes-guide",
    areaName: "AgencyArea",
    pattern: "routes/{slug}/guide",
    defaults: new { controller = "Routes", action = "Guide" });

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

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
