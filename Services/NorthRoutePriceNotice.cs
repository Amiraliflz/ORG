using System.Globalization;
using Application.Services.Seo;

namespace Application.Services;

/// <summary>
/// Price-variance notice for Mazandaran / Gilan / Golestan routes
/// on Wed–Fri (چهارشنبه تا جمعه) and official Iranian public holidays.
/// </summary>
public static class NorthRoutePriceNotice
{
  public const string SupportPhoneDisplay = "021-28422243";
  public const string SupportPhoneTel = "02128422243";

  public const string Message =
    "مسافر گرامی با توجه به شلوغی و ترافیک سنگین در ایام تعطیلات و آخر هفته ممکن است هزینه سفر مقداری با قیمت درج شده تفاوت ایجاد شود لذا خواهشمند است قبل از رزرو سفر خود جهت استعلام قیمت دقیق سفر با پشتیبانی سفرها به شماره 021-28422243 تماس فرمایید";

  /// <summary>Cities (and province labels) in مازندران، گیلان، گلستان.</summary>
  private static readonly HashSet<string> NorthCities = new(StringComparer.Ordinal)
  {
    "مازندران",
    "گیلان",
    "گلستان",

    // گیلان
    "آستارا",
    "بندرانزلی",
    "بندر انزلی",
    "انزلی",
    "رشت",
    "رودسر",
    "لاهیجان",
    "چابکسر",
    "فومن",
    "ماسال",

    // مازندران
    "آمل",
    "بابل",
    "بابلسر",
    "تنکابن",
    "رامسر",
    "ساری",
    "عباس اباد",
    "عباس آباد",
    "عباس‌آباد",
    "محمودآباد",
    "نور",
    "نوشهر",
    "چالوس",
    "کلاردشت",

    // گلستان
    "گرگان",
    "بندرترکمن",
    "بندر ترکمن",
  };

  public static bool IsNorthCity(string? cityName)
  {
    var key = SeoSlugHelper.StripCityLabel(cityName);
    if (key.Length == 0) return false;
    if (NorthCities.Contains(key)) return true;

    var compact = key.Replace(" ", string.Empty).Replace("‌", string.Empty);
    return NorthCities.Contains(compact);
  }

  public static bool InvolvesNorthRoute(string? origin, string? destination) =>
    IsNorthCity(origin) || IsNorthCity(destination);

  /// <summary>Peak travel window: چهارشنبه، پنج‌شنبه، جمعه.</summary>
  public static bool IsPeakWeekday(DateTime date) =>
    date.DayOfWeek is DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday;

  /// <summary>Official Iranian public holidays (fixed Shamsi + Hijri).</summary>
  public static bool IsOfficialHoliday(DateTime date)
  {
    var day = date.Date;
    var pc = new PersianCalendar();
    int m = pc.GetMonth(day);
    int d = pc.GetDayOfMonth(day);

    // Fixed solar holidays
    if (m == 1 && d is >= 1 and <= 4) return true; // نوروز
    if (m == 1 && d == 12) return true;             // روز جمهوری اسلامی
    if (m == 1 && d == 13) return true;             // سیزده‌بدر
    if (m == 3 && d == 14) return true;             // رحلت امام خمینی
    if (m == 3 && d == 15) return true;             // قیام ۱۵ خرداد
    if (m == 11 && d == 22) return true;            // پیروزی انقلاب اسلامی
    if (m == 12 && d == 29) return true;            // ملی شدن صنعت نفت

    // Lunar holidays (Hijri). Parentheses required — `&&` / `is` / `or` precedence traps.
    var hc = new HijriCalendar();
    int hm = hc.GetMonth(day);
    int hd = hc.GetDayOfMonth(day);

    if (hm == 1 && (hd == 9 || hd == 10)) return true;   // تاسوعا / عاشورا
    if (hm == 2 && hd == 20) return true;                // اربعین
    if (hm == 2 && hd == 28) return true;                // رحلت پیامبر
    if (hm == 2 && hd == 30) return true;                // شهادت امام رضا (صفر ۳۰روزه)
    if (hm == 3 && hd == 8) return true;                 // شهادت امام رضا
    if (hm == 3 && hd == 17) return true;                // میلاد پیامبر
    if (hm == 6 && hd == 3) return true;                 // شهادت حضرت فاطمه
    if (hm == 7 && hd == 13) return true;                // ولادت امام علی
    if (hm == 7 && hd == 27) return true;                // مبعث
    if (hm == 8 && hd == 15) return true;                // ولادت امام زمان
    if (hm == 9 && hd == 21) return true;                // شهادت امام علی
    if (hm == 10 && (hd == 1 || hd == 2)) return true;   // عید فطر
    if (hm == 10 && hd == 25) return true;               // شهادت امام صادق
    if (hm == 12 && hd == 10) return true;               // عید قربان
    if (hm == 12 && hd == 18) return true;               // عید غدیر

    return false;
  }

  public static bool IsPeakTravelDay(DateTime date) =>
    IsPeakWeekday(date) || IsOfficialHoliday(date);

  public static bool ShouldShow(string? origin, string? destination, DateTime travelDate) =>
    InvolvesNorthRoute(origin, destination) && IsPeakTravelDay(travelDate.Date);
}
