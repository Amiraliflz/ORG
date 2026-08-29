using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var conn = Environment.GetEnvironmentVariable("ORG_CONN")
  ?? "Host=89.42.199.39;Port=5432;Database=ORG;Username=root;Password=qazwsx";

var failed = 0;
void Pass(string msg) => Console.WriteLine($"PASS  {msg}");
void Fail(string msg) { Console.WriteLine($"FAIL  {msg}"); failed++; }

// --- 1) Real MVC form binding (same as DiscountCodesController.Create) ---
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
  EnvironmentName = Environments.Development
});
builder.WebHost.UseTestServer();
builder.Services.AddControllers().AddApplicationPart(typeof(BindController).Assembly);
builder.Services.AddRouting();
var app = builder.Build();
app.MapControllers();
await app.StartAsync();
var client = app.GetTestClient();

async Task<bool?> PostBindAsync(params KeyValuePair<string, string>[] fields)
{
  var content = new FormUrlEncodedContent(fields);
  var res = await client.PostAsync("/bind", content);
  var json = await res.Content.ReadAsStringAsync();
  if (!res.IsSuccessStatusCode)
  {
    Fail($"bind HTTP {(int)res.StatusCode}: {json}");
    return null;
  }
  using var doc = System.Text.Json.JsonDocument.Parse(json);
  return doc.RootElement.GetProperty("allowMultipleUsePerUser").GetBoolean();
}

var broken = await PostBindAsync(
  new KeyValuePair<string, string>("allowMultipleUsePerUser", "false"),
  new KeyValuePair<string, string>("allowMultipleUsePerUser", "true"));
if (broken == false)
  Pass("old order (false,true) binds false — reproduces the bug");
else
  Fail($"expected old order false, got {broken}");

var fixedOrder = await PostBindAsync(
  new KeyValuePair<string, string>("allowMultipleUsePerUser", "true"),
  new KeyValuePair<string, string>("allowMultipleUsePerUser", "false"));
if (fixedOrder == true)
  Pass("new order (true,false) binds true");
else
  Fail($"expected new order true, got {fixedOrder}");

var uncheckedOnly = await PostBindAsync(
  new KeyValuePair<string, string>("allowMultipleUsePerUser", "false"));
if (uncheckedOnly == false)
  Pass("unchecked (hidden only) binds false");
else
  Fail($"expected unchecked false, got {uncheckedOnly}");

await app.StopAsync();

var createPath = "/Users/amirali/ORG/Areas/Admin/Views/DiscountCodes/Create.cshtml";
var html = await File.ReadAllTextAsync(createPath);
var chkIdx = html.IndexOf("id=\"chkMultiUse\"", StringComparison.Ordinal);
var hidIdx = html.IndexOf("type=\"hidden\" name=\"allowMultipleUsePerUser\"", StringComparison.Ordinal);
if (chkIdx >= 0 && hidIdx >= 0 && chkIdx < hidIdx)
  Pass("Create.cshtml: checkbox appears before hidden false");
else
  Fail($"Create.cshtml order wrong (chk={chkIdx}, hid={hidIdx})");

var opts = new DbContextOptionsBuilder<MiniDb>().UseNpgsql(conn).Options;
await using var db = new MiniDb(opts);

var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var multiCode = $"FT_MULTI_{stamp}";
var onceCode = $"FT_ONCE_{stamp}";
var phone = "09120000001";

try
{
  var multi = new Disc
  {
    Code = multiCode,
    DiscountPercent = 10,
    IsActive = true,
    AllowMultipleUsePerUser = true,
    MaxUses = null,
    UsedCount = 0,
    CreatedAt = DateTime.Now,
    Description = "func-test multi"
  };
  var once = new Disc
  {
    Code = onceCode,
    DiscountPercent = 10,
    IsActive = true,
    AllowMultipleUsePerUser = false,
    MaxUses = null,
    UsedCount = 0,
    CreatedAt = DateTime.Now,
    Description = "func-test once"
  };
  db.DiscountCodes.AddRange(multi, once);
  await db.SaveChangesAsync();
  Pass($"inserted {multiCode} / {onceCode}");

  db.DiscountCodeUsages.Add(new Usage { DiscountCodeId = multi.Id, UserPhone = phone, UsedAt = DateTime.Now });
  db.DiscountCodeUsages.Add(new Usage { DiscountCodeId = once.Id, UserPhone = phone, UsedAt = DateTime.Now });
  await db.SaveChangesAsync();

  var multiOk = await ValidateLikeApp(db, multiCode, phone);
  var onceOk = await ValidateLikeApp(db, onceCode, phone);

  if (multiOk.valid) Pass("unlimited still valid after same-phone prior use");
  else Fail($"unlimited should remain valid: {multiOk.message}");

  if (!onceOk.valid && onceOk.message!.Contains("قبلاً", StringComparison.Ordinal))
    Pass("one-time blocked after prior use");
  else
    Fail($"one-time should block, got valid={onceOk.valid} msg={onceOk.message}");

  multi.AllowMultipleUsePerUser = false;
  await db.SaveChangesAsync();
  if (!(await ValidateLikeApp(db, multiCode, phone)).valid) Pass("toggle OFF blocks reuse");
  else Fail("expected block after toggle off");

  multi.AllowMultipleUsePerUser = true;
  await db.SaveChangesAsync();
  if ((await ValidateLikeApp(db, multiCode, phone)).valid) Pass("toggle ON allows reuse");
  else Fail("expected allow after toggle on");
}
finally
{
  var ids = await db.DiscountCodes.Where(c => c.Code == multiCode || c.Code == onceCode).Select(c => c.Id).ToListAsync();
  if (ids.Count > 0)
  {
    await db.DiscountCodeUsages.Where(u => ids.Contains(u.DiscountCodeId)).ExecuteDeleteAsync();
    await db.DiscountCodes.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
  }
  Pass("cleaned up func-test rows");
}

Console.WriteLine(failed == 0 ? "\nALL FUNCTIONAL CHECKS PASSED" : $"\n{failed} CHECK(S) FAILED");
Environment.Exit(failed == 0 ? 0 : 1);

static async Task<(bool valid, string? message)> ValidateLikeApp(MiniDb db, string codeStr, string userPhone)
{
  var code = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Code == codeStr && d.IsActive);
  if (code == null) return (false, "کد تخفیف معتبر نیست.");
  if (code.ExpiryDate.HasValue && code.ExpiryDate < DateTime.Now) return (false, "کد تخفیف منقضی شده است.");
  if (code.MaxUses.HasValue && code.UsedCount >= code.MaxUses) return (false, "کد تخفیف به حداکثر استفاده رسیده است.");
  if (!code.AllowMultipleUsePerUser && !string.IsNullOrWhiteSpace(userPhone))
  {
    var alreadyUsed = await db.DiscountCodeUsages.AnyAsync(u => u.DiscountCodeId == code.Id && u.UserPhone == userPhone);
    if (alreadyUsed) return (false, "این کد تخفیف قبلاً توسط شما استفاده شده است.");
  }
  return (true, "کد تخفیف اعمال شد.");
}

[ApiController]
[Route("bind")]
public class BindController : ControllerBase
{
  [HttpPost]
  [IgnoreAntiforgeryToken]
  public IActionResult Post([FromForm] bool allowMultipleUsePerUser)
    => Ok(new { allowMultipleUsePerUser });
}

sealed class MiniDb : DbContext
{
  public MiniDb(DbContextOptions<MiniDb> options) : base(options) { }
  public DbSet<Disc> DiscountCodes => Set<Disc>();
  public DbSet<Usage> DiscountCodeUsages => Set<Usage>();
  protected override void OnModelCreating(ModelBuilder b)
  {
    b.Entity<Disc>().ToTable("DiscountCodes");
    b.Entity<Usage>().ToTable("DiscountCodeUsages");
  }
}

sealed class Disc
{
  public int Id { get; set; }
  public string Code { get; set; } = "";
  public int DiscountPercent { get; set; }
  public DateTime? ExpiryDate { get; set; }
  public int? MaxUses { get; set; }
  public int UsedCount { get; set; }
  public bool IsActive { get; set; }
  public bool AllowMultipleUsePerUser { get; set; }
  public DateTime CreatedAt { get; set; }
  public string? Description { get; set; }
}

sealed class Usage
{
  public int Id { get; set; }
  public int DiscountCodeId { get; set; }
  public string UserPhone { get; set; } = "";
  public DateTime UsedAt { get; set; }
}
