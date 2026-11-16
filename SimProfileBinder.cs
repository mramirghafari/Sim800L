using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SIM800L_Flasher;

/// <summary>
/// هر ردیف = یک سیم‌کارت و IMEI تخصیص‌یافته به آن.
///
/// مسیر عملی که در سخت‌افزار واقعاً وجود دارد (و مستندات SIMBox/SIMServer هم همین را می‌گویند):
/// نرم‌افزار مدیریت سیم‌کارت، IMEI را روی **ماژول همان پورت** می‌نویسد و پورت با آن IMEI ثبت شبکه می‌شود.
/// یعنی IMEI همیشه روی ماژول/پورت می‌نشیند، نه روی سیم‌کارت؛ و هدف این فایل این است که هنگام تعویض
/// سیم‌کارت، IMEI متناظر همان سیم‌کارت به‌طور خودکار روی پورت نوشته شود.
/// </summary>
internal sealed record SimBinding(
    string ImsiOrIccid,
    string Imei,
    string ImeiBefore,
    string Label,
    string OperatorName,
    DateTime UpdatedUtc,
    bool Locked)
{
    public bool HasValidImei => Imei.Length == 15;

    public string MaskedKey => ImsiOrIccid.Length <= 6
        ? ImsiOrIccid
        : ImsiOrIccid[..4] + new string('*', Math.Max(0, ImsiOrIccid.Length - 6)) + ImsiOrIccid[^2..];
}

/// <summary>
/// «پروفایل سیم‌کارت»: نگهداری نسبت سیم‌کارت ↔ IMEI و اعمال خودکار IMEI وقتی سیم‌کارت عوض می‌شود.
///
/// بر اساس الگوی مستند SIMBox (SIM Server): IMEI به هر سیم‌کارت تخصیص داده می‌شود و هنگام ثبت
/// سیم‌کارت روی پورت، IMEI همان سیم‌کارت روی ماژول نوشته می‌شود. تفاوت مهم با تصور رایج: این
/// نرم‌افزار IMEI را روی **ماژول** می‌نویسد (خود SIMBox هم همین کار را با پورت GSM می‌کند)، چون
/// سیم‌کارت هیچ فرمانی برای تعیین IMEI میزبان ندارد.
/// </summary>
internal sealed class SimProfileBinder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SimProfileBinder(string? path = null) => _path = path ?? DefaultPath;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XAnten",
        "sim-profiles.json");

    public string FilePath => _path;

    public IReadOnlyList<SimBinding> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<SimBinding>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save(List<SimBinding> items)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(items, JsonOptions));
    }

    /// <summary>افزودن یا به‌روزرسانی نسبت سیم‌کارت ↔ IMEI.</summary>
    public void Upsert(string imsiOrIccid, string imei, string imeiBefore, string label, string operatorName, bool locked = false)
    {
        if (string.IsNullOrWhiteSpace(imsiOrIccid)) throw new ArgumentException("شناسه سیم‌کارت خالی است.", nameof(imsiOrIccid));
        if (imei.Length != 15) throw new ArgumentException("IMEI باید ۱۵ رقم باشد.", nameof(imei));

        var items = Load().ToList();
        var key = NormalizeKey(imsiOrIccid);
        var existing = items.FindIndex(x => NormalizeKey(x.ImsiOrIccid) == key);
        var entry = new SimBinding(key, imei, imeiBefore, label, operatorName, DateTime.UtcNow, locked);
        if (existing >= 0) items[existing] = entry; else items.Add(entry);
        Save(items);
    }

    public SimBinding? Find(string imsiOrIccid)
    {
        var key = NormalizeKey(imsiOrIccid);
        return Load().FirstOrDefault(x => NormalizeKey(x.ImsiOrIccid) == key);
    }

    public SimBinding? FindByImei(string imei) =>
        Load().FirstOrDefault(x => x.Imei == imei);

    /// <summary>هنگام تعویض سیم‌کارت: IMEI ثبت‌شده برای این کارت را برمی‌گرداند تا روی پورت نوشته شود.</summary>
    public SimBinding? ResolveForPort(string currentImsiOrIccid) => Find(currentImsiOrIccid);

    /// <summary>اگر همین IMEI به کارت دیگری تخصیص یافته باشد، هشدار IMEI تکراری می‌دهد.</summary>
    public string? DuplicateWarning(string imei, string currentKey)
    {
        var other = FindByImei(imei);
        if (other is null) return null;
        if (NormalizeKey(other.ImsiOrIccid) == NormalizeKey(currentKey)) return null;
        return $"این IMEI قبلاً به سیم‌کارت {other.MaskedKey} تخصیص یافته است؛ طبق سند، هر SIM باید IMEI یگانه داشته باشد.";
    }

    public string ExportText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("XAnten — پروفایل سیم‌کارت ↔ IMEI");
        builder.AppendLine(new string('=', 66));
        builder.AppendLine("شناسه سیم‌کارت        | IMEI تخصیصی      | IMEI قبلی        | برچسب        | اپراتور   | قفل");
        builder.AppendLine(new string('-', 120));

        foreach (var item in Load().OrderByDescending(x => x.UpdatedUtc))
            builder.AppendLine($"{item.MaskedKey,-20} | {item.Imei,-15} | " +
                               $"{(item.ImeiBefore.Length == 15 ? item.ImeiBefore : "—"),-15} | " +
                               $"{(item.Label.Length > 0 ? item.Label : "—"),-12} | " +
                               $"{(item.OperatorName.Length > 0 ? item.OperatorName : "—"),-9} | " +
                               $"{(item.Locked ? "بله" : "خیر")}");
        return builder.ToString();
    }

    private static string NormalizeKey(string value) =>
        new(value.Where(char.IsDigit).ToArray());
}
