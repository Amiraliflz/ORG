using Application.Data;
using Application.Services.TravelTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Application.Areas.Admin.Controllers;

/// <summary>
/// Manual ETA sync trigger. First run: POST /Admin/TravelTimes/Sync?force=true
/// (requires Admin auth). Hosted service also runs when Shamsi month advances or table is empty.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "Admin")]
public class TravelTimesController : Controller
{
  private readonly ITravelTimeSyncService _sync;
  private readonly AppDbContext _db;

  public TravelTimesController(ITravelTimeSyncService sync, AppDbContext db)
  {
    _sync = sync;
    _db = db;
  }

  [HttpGet]
  public async Task<IActionResult> Status(CancellationToken ct)
  {
    var state = await _db.TravelTimeSyncStates.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
    var routeCount = await _db.RouteTravelTimes.CountAsync(ct);
    var cityCount = await _db.CityCoordinates.CountAsync(ct);
    var needs = await _sync.NeedsSyncAsync(ct);
    return Json(new
    {
      needsSync = needs,
      routeCount,
      cityCount,
      state
    });
  }

  [HttpPost]
  [IgnoreAntiforgeryToken]
  public async Task<IActionResult> Sync(bool force = true, bool gapsOnly = false, CancellationToken ct = default)
  {
    var result = await _sync.SyncAsync(force, gapsOnly, ct);
    return Json(result);
  }
}
