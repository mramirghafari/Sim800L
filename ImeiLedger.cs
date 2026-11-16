using System.Text;
using System.Text.Json;

namespace SIM800L_Flasher;

internal sealed record ImeiLedgerEntry(
    DateTime TimestampUtc,
    string PortName,
    string ModuleModel,
    string FirmwareRevision,
    string ImeiBefore,
    string ImeiTarget,
    string ImeiAfter,
    string Strategy,
    bool Verified,
    bool Persisted,
    string OperatorName,
    string IccidMasked,
    string Notes)
{
    public string Status => Persisted ? "پایدار" : Verified ? "تأییدشده/ناپایدار" : "ناموفق";

    public string ToLine() =>
        $"{TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm} | {PortName,-6} | {ModuleModel,-18} | " +
        $"{ImeiBefore,-15} ← {ImeiAfter,-15} | {Status,-16} | {Strategy}";
}

/// <summary>
/// دفتر ثبت عملیات نوشتن IMEI. هر عملیات با IMEI قبلی و نتیجه پایداری ذخیره می‌شود تا اگر بعداً
/// دستگاهی مشکل پیدا کرد، بدانید چه چیزی روی آن نوشته شده و با چه مقداری می‌شود برگرداند.
/// داده شخصی (ICCID) ماسک می‌شود.
/// </summary>
internal sealed class ImeiLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ImeiLedger(string? path = null)
    {
        _path = path ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XAnten",
        "imei-ledger.json");

    public string FilePath => _path;

    public IReadOnlyList<ImeiLedgerEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<ImeiLedgerEntry>>(json) ?? [];
        }
        catch
        {
            // دفتر خراب نباید مانع کار شود؛ از نو شروع می‌کنیم و فایل قبلی حفظ می‌شود.
            return [];
        }
    }

    public void Append(ImeiLedgerEntry entry)
    {
        var entries = Load().ToList();
        entries.Add(entry);
        Save(entries);
    }

    private void Save(List<ImeiLedgerEntry> entries)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public string ExportText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("XAnten — دفتر عملیات IMEI");
        builder.AppendLine(new string('=', 60));
        builder.AppendLine("تاریخ            | پورت   | مدل               | IMEI قبلی → بعدی            | وضعیت              | استراتژی");
        builder.AppendLine(new string('-', 120));
        foreach (var entry in Load().OrderByDescending(e => e.TimestampUtc))
            builder.AppendLine(entry.ToLine());
        return builder.ToString();
    }

    /// <summary>اگر روی این پورت قبلاً نوشته شده، IMEI اولیه را برای بازگردانی برمی‌گرداند.</summary>
    public ImeiLedgerEntry? LastVerifiedFor(string portName) =>
        Load().Where(e => e.PortName == portName && e.Persisted)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefault();

    public static string MaskIccid(string? iccid)
    {
        if (string.IsNullOrWhiteSpace(iccid) || iccid == "—") return "—";
        return iccid.Length <= 6 ? new string('*', iccid.Length)
            : iccid[..4] + new string('*', Math.Max(0, iccid.Length - 6)) + iccid[^2..];
    }
}
