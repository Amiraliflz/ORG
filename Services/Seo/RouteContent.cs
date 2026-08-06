namespace Application.Services.Seo;

/// <summary>Deterministic, unique long-form copy per OD pair — avoids thin/doorway duplicate pages.</summary>
public static class RouteContent
{
  public sealed record Bundle(
    string H1,
    string MetaDescription,
    string Intro,
    string AboutCorridor,
    IReadOnlyList<(string Label, string Text)> AboutBlocks,
    string TravelInfo,
    string TipsHeading,
    IReadOnlyList<string> Tips,
    string HowToHeading,
    IReadOnlyList<(string Title, string Text)> HowToSteps,
    IReadOnlyList<(string Question, string Answer)> Faqs,
    string WhyHeading,
    string WhyBody,
    int? ApproxKm);

  public static Bundle For(RouteCatalog.RoutePage route)
  {
    var o = CityCatalog.FindByName(route.OriginFa)!;
    var d = CityCatalog.FindByName(route.DestinationFa)!;
    var time = FormatTime(route.TravelTimeMins);
    var km = EstimateKm(route.TravelTimeMins);
    var kmText = km is int k ? $" حدود {k} کیلومتر" : "";
    var timeText = time is null ? "" : $" مدت تقریبی حرکت{time}";
    var corridor = CorridorLabel(o, d);

    var h1 = $"سواری {route.OriginFa} به {route.DestinationFa} | رزرو آنلاین";
    var meta =
      $"رزرو آنلاین سواری {route.OriginFa} به {route.DestinationFa} با مسترشوفر — {corridor}، رانندگان تأییدشده،{timeText}{kmText}، بلیط سریع و پشتیبانی ۲۴/۷.";

    var intro =
      $"سواری {route.OriginFa} ({o.RegionFa}) به {route.DestinationFa} ({d.RegionFa}) را در این صفحه جستجو و رزرو می‌کنید. " +
      $"{o.NameFa} {o.RoleFa} است و {d.NameFa} به‌عنوان {d.RoleFa} مقصد این مسیر شناخته می‌شود. " +
      "تاریخ را مطابق برنامه خود تنظیم کنید تا گزینه‌های همان روز و کلاس‌های ناوگان نمایش داده شوند." +
      (time is null ? "" : $" زمان تقریبی حرکت این مسیر حدود{time} است.") +
      (km is int kk ? $" مسافت تقریبی حدود {kk} کیلومتر برآورد می‌شود." : "");

    var aboutBlocks = new List<(string Label, string Text)>
    {
      (
        "مسیر",
        $"مسیر {route.OriginFa} به {route.DestinationFa} یکی از مسیرهای فعال سواری بین‌شهری مسترشوفر در کریدور {corridor} است."
      ),
      (
        $"مبدأ — {o.NameFa}",
        $"{o.NameFa} {o.RoleFa} در {o.RegionFa} است. {o.BlurbFa}"
      ),
      (
        $"مقصد — {d.NameFa}",
        $"مقصد {d.NameFa} در {d.RegionFa} قرار دارد و {d.RoleFa} محسوب می‌شود. {d.BlurbFa}"
      ),
      (
        "قیمت",
        $"قیمت کلاس سفر {route.OriginFa} به {route.DestinationFa} پس از انتخاب تاریخ در نتایج بالای همین صفحه نمایش داده می‌شود."
      )
    };

    var about = string.Join(" ", aboutBlocks.Select(b => b.Text));

    var travelInfo =
      (time is null
        ? $"مدت حرکت {route.OriginFa} به {route.DestinationFa} به‌صورت پویا در نتایج جستجو و بر اساس شرایط روز مشخص می‌شود."
        : $"مدت تقریبی حرکت از {route.OriginFa} به {route.DestinationFa} حدود{time} است؛ ترافیک، آب‌وهوا و توقف‌ها می‌توانند آن را تغییر دهند.") +
      (km is int k2
        ? $" مسافت تقریبی حدود {k2} کیلومتر است."
        : "") +
      $" برای برنامه‌ریزی بهتر در مبدأ {route.OriginFa}، حرکت را خارج از ساعت‌های شلوغ و قبل از تعطیلات پرمسافر در نظر بگیرید.";

    var tips = new List<string>();
    if (o.TripTipsFa is { Length: > 0 }) tips.AddRange(o.TripTipsFa.Select(t => $"مبدأ ({route.OriginFa}): {t}"));
    if (d.TripTipsFa is { Length: > 0 }) tips.AddRange(d.TripTipsFa.Select(t => $"مقصد ({route.DestinationFa}): {t}"));
    tips.Add($"اگر در مسیر {route.OriginFa} به {route.DestinationFa} توقف یا مبدأ و مقصد اضافه دارید، قبل از شروع سفر با پشتیبانی تماس حاصل فرمایید.");
    tips.Add("برای نوروز، آخر هفته‌های طولانی و ایام پرمسافر، زودتر رزرو کنید.");
    if (IsNorthish(route.OriginFa, route.DestinationFa) || IsNorthRegion(o) || IsNorthRegion(d))
      tips.Add("در مسیرهای شمالی، باران و مه می‌توانند زمان رسیدن شما به مقصد را افزایش دهند.");
    if (IsMountainish(route.OriginFa, route.DestinationFa))
      tips.Add("در مسیرهای کوهستانی، شرایط جوی و ترافیک فصلی را جدی بگیرید.");
    if (IsWestCold(route.OriginFa, route.DestinationFa) || IsWestRegion(o) || IsWestRegion(d))
      tips.Add("در زمستان غرب و شمال‌غرب، حاشیه زمانی و آمادگی جاده را در نظر بگیرید.");
    if (IsCoastalRegion(o) || IsCoastalRegion(d))
      tips.Add("برای مقاصد ساحلی و بندری، محل دقیق سوار/پیاده شدن را از قبل مشخص کنید.");

    var reverse = RouteCatalog.ReverseOf(route);
    var reverseAnswer = reverse is null
      ? $"مسیر برگشت {route.DestinationFa} به {route.OriginFa} را از جستجوی صفحه اصلی یا فهرست مسیرها بررسی کنید."
      : $"بله — صفحه سواری {route.DestinationFa} به {route.OriginFa} در /routes/{reverse.Slug} در دسترس است.";

    var howTo = new List<(string, string)>
    {
      ("جستجوی مسیر", $"روی «جستجوی این مسیر» بزنید تا مبدأ {route.OriginFa} و مقصد {route.DestinationFa} از پیش انتخاب شوند."),
      ("انتخاب تاریخ", $"تاریخ سفر از {route.OriginFa} را مطابق با برنامه خود انتخاب کنید."),
      ("انتخاب کلاس", $"از بین کلاس‌های ناوگان، خودرو مناسب مسیر {route.OriginFa}–{route.DestinationFa} را انتخاب کنید."),
      ("تکمیل رزرو", "سفر خود را به راحتی رزرو کنید و بلیط را آنی دریافت کنید؛ همکاران ما برای هماهنگی سفر با شما در سریع‌ترین زمان ممکن تماس خواهند گرفت.")
    };

    var faqs = new List<(string, string)>
    {
      (
        $"چطور سواری {route.OriginFa} به {route.DestinationFa} رزرو کنم؟",
        $"دکمه جستجوی این مسیر را بزنید، تاریخ حرکت از {route.OriginFa} را تأیید کنید، کلاس را انتخاب و رزرو را تکمیل کنید. پشتیبانی برای هماهنگی با شما تماس می‌گیرد."
      ),
      (
        $"مدت سفر {route.OriginFa} به {route.DestinationFa} چقدر است؟",
        time is null
          ? "مدت دقیق پس از جستجو و بر اساس شرایط روز مشخص می‌شود. این صفحه صرفاً راهنمای مسیر ثابت است."
          : $"برآورد تقریبی حدود{time} است؛ ترافیک، آب‌وهوا و توقف‌ها می‌توانند آن را کم یا زیاد کنند."
      ),
      (
        $"ویژگی مسیر {route.OriginFa}–{route.DestinationFa} چیست؟",
        $"این مسیر کریدور {corridor} را پوشش می‌دهد: مبدأ {o.RoleFa} و مقصد {d.RoleFa}."
      ),
      (
        "آیا قیمت قبل از پرداخت مشخص است؟",
        "بله. پس از جستجو قیمت گزینه‌های موجود نمایش داده می‌شود تا قبل از رزرو مقایسه کنید."
      ),
      (
        $"آیا مسیر برگشت {route.DestinationFa} به {route.OriginFa} هم دارید؟",
        reverseAnswer
      ),
      (
        "پشتیبانی در طول سفر دارید؟",
        "پشتیبانی ۲۴/۷ تا آخرین لحظه سفر در دسترس است. از بخش ارتباط با ما نیز می‌توانید پیام بگذارید."
      )
    };

    var why =
      $"با مسترشوفر، سواری {route.OriginFa} به {route.DestinationFa} ({corridor}) را آنلاین جستجو می‌کنید، قیمت را قبل از رزرو می‌بینید و بلیط را آنی دریافت می‌کنید. " +
      "رانندگان تأییدشده‌اند و پس از رزرو همکاران ما برای هماهنگی سفر با شما تماس می‌گیرند؛ پشتیبانی هم تا پایان مسیر همراهتان است.";

    return ApplyOverlay(route.Slug, new Bundle(
      H1: h1,
      MetaDescription: meta.Length > 165 ? meta[..162] + "…" : meta,
      Intro: intro,
      AboutCorridor: about,
      AboutBlocks: aboutBlocks,
      TravelInfo: travelInfo,
      TipsHeading: $"نکته‌های سفر {route.OriginFa} به {route.DestinationFa}",
      Tips: tips.Distinct().Take(8).ToList(),
      HowToHeading: "چطور در چند دقیقه رزرو کنید",
      HowToSteps: howTo,
      Faqs: faqs,
      WhyHeading: "چرا مسترشوفر برای این مسیر؟",
      WhyBody: why,
      ApproxKm: km));
  }

  private static Bundle ApplyOverlay(string slug, Bundle generated)
  {
    var overlay = RouteContentOverlays.Find(slug);
    return overlay is null ? generated : RouteContentOverlays.Apply(generated, overlay);
  }

  private static string CorridorLabel(CityCatalog.CityProfile o, CityCatalog.CityProfile d)
  {
    if (o.RegionFa == d.RegionFa) return o.RegionFa;
    return $"{o.RegionFa} → {d.RegionFa}";
  }

  private static bool IsNorthRegion(CityCatalog.CityProfile c) =>
    c.RegionFa.Contains("شمال", StringComparison.Ordinal);

  private static bool IsWestRegion(CityCatalog.CityProfile c) =>
    c.RegionFa.Contains("غرب", StringComparison.Ordinal);

  private static bool IsCoastalRegion(CityCatalog.CityProfile c) =>
    c.RegionFa.Contains("ساحل", StringComparison.Ordinal) ||
    c.RoleFa.Contains("ساحل", StringComparison.Ordinal) ||
    c.RoleFa.Contains("بندر", StringComparison.Ordinal);

  private static string? FormatTime(int? mins)
  {
    if (mins is not int m || m <= 0) return null;
    if (m < 60) return $" {m} دقیقه";
    var h = m / 60;
    var r = m % 60;
    return r == 0 ? $" {h} ساعت" : $" {h} ساعت و {r} دقیقه";
  }

  private static int? EstimateKm(int? mins)
  {
    if (mins is not int m || m < 30) return null;
    // ~75 km/h effective highway average for rough editorial distance
    return (int)Math.Round(m / 60.0 * 75 / 5.0) * 5;
  }

  private static bool IsNorthish(string a, string b)
  {
    string[] n = ["رشت", "لاهیجان", "چالوس", "نوشهر", "رامسر", "ساری", "گرگان"];
    return n.Contains(a) || n.Contains(b);
  }

  private static bool IsMountainish(string a, string b) =>
    a is "چالوس" or "نوشهر" or "رامسر" or "شهرکرد" || b is "چالوس" or "نوشهر" or "رامسر" or "شهرکرد";

  private static bool IsWestCold(string a, string b)
  {
    string[] w = ["تبریز", "اردبیل", "زنجان", "همدان", "سنندج", "کرمانشاه"];
    return w.Contains(a) || w.Contains(b);
  }
}
