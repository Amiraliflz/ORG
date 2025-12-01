using Application.Data;
using Application.Services;
using Application.Services.Auth;
using Application.Services.MrShooferORS;
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



//await smssender.SendCustomerTicket_issued("محمد صدرا".Replace(' ', '\u200C'), "درستکار".Replace(' ', '\u200C'), "Z4EIU", "https://9qbnlgl9-5055.euw.devtunnels.ms/ReserveInfo?reference=Z4EIU", "09902063015");


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddSingleton<DirectionsRepository, DirectionsRepository>();
builder.Services.AddSingleton<DirectionsTravelTimeCalculator>();  

builder.Services.AddTransient<MrShooferAPIClient, MrShooferAPIClient>(c => new MrShooferAPIClient(new HttpClient(), "https://mrbilit.mrshoofer.ir"));


builder.Services.AddTransient<CustomerServiceSmsSender>();

builder.Services
  .AddControllersWithViews()
  .AddJsonOptions(opts =>
  {
    // Ensure Persian characters are not escaped in JSON responses
    opts.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
  });

builder.Services.TryAddTransient<IOtpLogin, KavehNeagerOtp>();


var sqlite_connstring = "Data Source=/home/ubuntu/mrshoofer_org/Mrshoofer_org.db";
if (builder.Environment.IsDevelopment())
{
  sqlite_connstring = builder.Configuration.GetConnectionString("development");
}


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqlite_connstring));


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

app.UseRateLimiter();


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Home/Error");
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}


app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();



app.MapControllerRoute(
    name: "agency",
    pattern: "{controller=Home}/{action=Index}/{id?}",
    defaults: new { area = "AgencyArea" });

// Configure routing for Admin and Agency areas
app.MapControllerRoute(
    name: "admin",
    pattern: "Admin/{controller=Home}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

// Configure default routing
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();
