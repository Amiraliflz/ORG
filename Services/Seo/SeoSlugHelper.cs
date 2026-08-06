using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Services.Seo;

/// <summary>Persian city name → URL slug (known map + deterministic fallback).</summary>
public static class SeoSlugHelper
{
  private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
  {
    ["تهران"] = "tehran",
    ["اصفهان"] = "isfahan",
    ["رشت"] = "rasht",
    ["لاهیجان"] = "lahijan",
    ["چالوس"] = "chalus",
    ["نوشهر"] = "nowshahr",
    ["رامسر"] = "ramsar",
    ["ساری"] = "sari",
    ["کاشان"] = "kashan",
    ["همدان"] = "hamedan",
    ["زنجان"] = "zanjan",
    ["اردبیل"] = "ardabil",
    ["تبریز"] = "tabriz",
    ["قم"] = "qom",
    ["شهرکرد"] = "shahrekord",
    ["کرمانشاه"] = "kermanshah",
    ["سنندج"] = "sanandaj",
    ["شیراز"] = "shiraz",
    ["گرگان"] = "gorgan",
    ["مشهد"] = "mashhad",
    ["یزد"] = "yazd",
    ["قزوین"] = "qazvin",
    ["کرج"] = "karaj",
    ["بندرعباس"] = "bandarabbas",
    ["بندر عباس"] = "bandarabbas",
    ["اهواز"] = "ahvaz",
    ["کرمان"] = "kerman",
    ["ارومیه"] = "urmia",
    ["بوشهر"] = "bushehr",
    ["خرم‌آباد"] = "khorramabad",
    ["خرم آباد"] = "khorramabad",
    ["یاسوج"] = "yasuj",
    ["بجنورد"] = "bojnurd",
    ["سمنان"] = "semnan",
    ["شاهرود"] = "shahroud",
    ["نیشابور"] = "neyshabur",
    ["سبزوار"] = "sabzevar",
    ["کیش"] = "kish",
    ["قشم"] = "qeshm",
    ["آمل"] = "amol",
    ["بابل"] = "babol",
    ["بابلسر"] = "babolsar",
    ["محمودآباد"] = "mahmoudabad",
    ["تنکابن"] = "tonekabon",
    ["رودسر"] = "rudsar",
    ["انزلی"] = "anzali",
    ["بندر انزلی"] = "anzali",
    ["آستارا"] = "astara",
    ["فومن"] = "fuman",
    ["ماسال"] = "masal",
    ["دزفول"] = "dezful",
    ["آبادان"] = "abadan",
    ["خرمشهر"] = "khorramshahr",
    ["مراغه"] = "maragheh",
    ["میانه"] = "mianeh",
    ["ساوه"] = "saveh",
    ["اراک"] = "arak",
    ["بروجرد"] = "borujerd",
    ["ملایر"] = "malayer",
    ["نطنز"] = "natanz",
    ["نجف‌آباد"] = "najafabad",
    ["نجف آباد"] = "najafabad",
    ["شاهین‌شهر"] = "shahinshahr",
    ["شاهین شهر"] = "shahinshahr",
    ["اسلامشهر"] = "islamshahr",
    ["الیگودرز"] = "aligudarz",
    ["ایذه"] = "izeh",
    ["ایلام"] = "ilam",
    ["بندرترکمن"] = "bandartorkaman",
    ["بندر ترکمن"] = "bandartorkaman",
    ["بندرگناوه"] = "bandarganaveh",
    ["بندر گناوه"] = "bandarganaveh",
    ["بندرانزلی"] = "anzali",
    ["بهارستان"] = "baharestan",
    ["تاکستان"] = "takestan",
    ["جاجرم"] = "jajarm",
    ["خمینی شهر"] = "khomeinishahr",
    ["خمینی‌شهر"] = "khomeinishahr",
    ["خوی"] = "khoy",
    ["دماوند"] = "damavand",
    ["رفسنجان"] = "rafsanjan",
    ["زاهدان"] = "zahedan",
    ["سپاهان شهر"] = "sepahanshahr",
    ["سپاهان‌شهر"] = "sepahanshahr",
    ["سیرجان"] = "sirjan",
    ["شهر ری"] = "shahrerey",
    ["شهرری"] = "shahrerey",
    ["شهرقدس"] = "shahrqods",
    ["قدس"] = "shahrqods",
    ["عباس اباد"] = "abbasabad",
    ["عباس‌آباد"] = "abbasabad",
    ["عباس آباد"] = "abbasabad",
    ["عسلویه"] = "assaluyeh",
    ["فرودگاه امام خمینی"] = "ika-airport",
    ["فرودگاه وان"] = "van-airport",
    ["فشم"] = "fasham",
    ["فولادشهر"] = "fooladshahr",
    ["ماکو"] = "maku",
    ["مبارکه"] = "mobarakeh",
    ["مهران"] = "mehran",
    ["نور"] = "nur",
    ["وان"] = "van",
    ["چابهار"] = "chabahar",
    ["چابکسر"] = "chaboksar",
    ["کلاردشت"] = "kelardasht",
  };

  private static readonly Dictionary<char, string> FaToLat = new()
  {
    ['ا'] = "a", ['آ'] = "a", ['ب'] = "b", ['پ'] = "p", ['ت'] = "t", ['ث'] = "s",
    ['ج'] = "j", ['چ'] = "ch", ['ح'] = "h", ['خ'] = "kh", ['د'] = "d", ['ذ'] = "z",
    ['ر'] = "r", ['ز'] = "z", ['ژ'] = "zh", ['س'] = "s", ['ش'] = "sh", ['ص'] = "s",
    ['ض'] = "z", ['ط'] = "t", ['ظ'] = "z", ['ع'] = "a", ['غ'] = "gh", ['ف'] = "f",
    ['ق'] = "gh", ['ک'] = "k", ['گ'] = "g", ['ل'] = "l", ['م'] = "m", ['ن'] = "n",
    ['و'] = "v", ['ه'] = "h", ['ی'] = "i", ['ئ'] = "i", ['ء'] = "", ['ٔ'] = "",
    ['ي'] = "i", ['ك'] = "k", ['ة'] = "h", ['‌'] = "", [' '] = "-",
  };

  public static IReadOnlyDictionary<string, string> KnownMap => Known;

  public static string StripCityLabel(string? s)
  {
    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
    var str = s.Trim();
    var idx = str.IndexOf('(');
    if (idx >= 0) str = str[..idx].Trim();
    str = Regex.Replace(str, "[\u200C\u200F\u200E\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED]", string.Empty);
    str = str.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9').Replace('\u0629', '\u0647');
    str = Regex.Replace(str, @"\s+", " ").Trim();
    return str;
  }

  public static string SlugifyCity(string? nameFa, out bool usedFallback)
  {
    usedFallback = false;
    var name = StripCityLabel(nameFa);
    if (name.Length == 0) return "city";

    if (Known.TryGetValue(name, out var known))
      return known;

    // Try without ZWNJ / space variants
    var compact = name.Replace(" ", "").Replace("\u200C", "");
    foreach (var kv in Known)
    {
      if (kv.Key.Replace(" ", "").Replace("\u200C", "") == compact)
        return kv.Value;
    }

    usedFallback = true;
    var sb = new StringBuilder(name.Length * 2);
    foreach (var ch in name.Normalize(NormalizationForm.FormC))
    {
      if (FaToLat.TryGetValue(ch, out var lat))
        sb.Append(lat);
      else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
        sb.Append(ch);
      else if (ch is >= 'A' and <= 'Z')
        sb.Append(char.ToLowerInvariant(ch));
    }

    var slug = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    if (string.IsNullOrEmpty(slug))
      slug = "city-" + Math.Abs(name.GetHashCode(StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture);
    return slug.ToLowerInvariant();
  }

  public static string RouteSlug(string originFa, string destFa)
  {
    var o = SlugifyCity(originFa, out _);
    var d = SlugifyCity(destFa, out _);
    return $"{o}-{d}";
  }
}
