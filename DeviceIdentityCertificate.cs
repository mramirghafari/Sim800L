using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// برگه «یکپارچگی هویت دستگاه»: IMEI خوانده‌شده از هر مسیر را می‌سنجد و به‌صورت سندی
/// قابل ارائه به مرکز خدمات یا سامانه همتا درمی‌آورد.
///
/// کاربرد واقعی: وقتی IMEI گوشی خالی/نامعتبر شده یا باید ثابت شود دستگاه همان است که
/// روی جعبه/مدرک نوشته، این برگه شاهد مستند می‌دهد. هیچ شناسه‌ای را تغییر نمی‌دهد و
/// هیچ «تعمیر» خودکاری انجام نمی‌دهد — عمداً، چون تغییر IMEI عملیات مجازِ تعمیرگاهی نیست.
/// </summary>
internal sealed record DeviceIdentityCertificate(
    DateTimeOffset IssuedAt,
    string DocumentId,
    string Imei1,
    string Imei2,
    string Eid,
    string IccidMasked,
    string Model,
    string Serial,
    string Chipset,
    string ReadPath,
    string Imei1Status,
    string Imei2Status,
    string TacInfo,
    IReadOnlyList<string> PathAudit,
    string Verdict)
{
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("برگه یکپارچگی هویت دستگاه — XAnten");
        text.AppendLine(new string('═', 64));
        text.AppendLine($"شماره سند: {DocumentId}");
        text.AppendLine($"تاریخ صدور: {IssuedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        text.AppendLine();
        text.AppendLine("دستگاه");
        text.AppendLine($"  مدل: {Show(Model)}");
        text.AppendLine($"  سریال: {Show(Serial)}");
        text.AppendLine($"  چیپست: {Show(Chipset)}");
        text.AppendLine();
        text.AppendLine("شناسه‌ها");
        text.AppendLine($"  IMEI 1: {Show(Imei1)} — {Imei1Status}");
        text.AppendLine($"  IMEI 2: {Show(Imei2)} — {Imei2Status}");
        text.AppendLine($"  EID:    {Show(Eid)}");
        text.AppendLine($"  ICCID:  {Show(IccidMasked)} (ماسک‌شده)");
        text.AppendLine();
        text.AppendLine("ساختار IMEI");
        text.AppendLine($"  {TacInfo}");
        text.AppendLine();
        text.AppendLine($"مسیر خواندن موفق: {ReadPath}");
        text.AppendLine("رد مسیرها:");
        foreach (var line in PathAudit) text.AppendLine($"  • {line}");
        text.AppendLine();
        text.AppendLine("نتیجه");
        text.AppendLine(Verdict);
        text.AppendLine();
        text.AppendLine("این سند فقط خواندنی است و بر اساس پاسخ خودِ دستگاه ساخته شده. " +
                        "برای بازگردانی IMEI کارخانه یا ثبت در همتا، همین برگه را به مرکز مجاز یا سامانه ارائه کن.");
        return text.ToString();
    }

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static DeviceIdentityCertificate Build(UniversalIdentity identity, DateTimeOffset issuedAt)
    {
        var imei1Status = Describe(identity.Imei1);
        var imei2Status = Describe(identity.Imei2);

        var tac = string.Empty;
        if (identity.Imei1.Length == 15 && ImeiNumber.TryParse(identity.Imei1, out var parsed, out _))
            tac = $"TAC: {parsed!.TacFormatted}   سریال تولید: {parsed.Serial}   رقم کنترلی: {parsed.CheckDigit} (Luhn معتبر)";
        else
            tac = "IMEI قابل استخراج نبود؛ ساختار TAC بررسی نشد.";

        var allFailed = identity.Attempts.Count > 0 && identity.Attempts.All(a => !a.Succeeded);
        var verdict = identity.Found
            ? (identity.Imei1.Length == 15
                ? "هویت دستگاه سالم خوانده شد و رقم کنترلی معتبر است."
                : "IMEI2 خوانده شد ولی IMEI1 نه؛ برای دستگاه دوسیم این وضعیت غیرعادی است و باید بررسی شود.")
            : allFailed
                ? "هیچ مسیری IMEI نداد. این وضعیت معمولاً یعنی IMEI در خود دستگاه خالی/آسیب‌دیده است؛ " +
                  "برگه را برای بازگردانی رسمی به مرکز خدمات ببر (نه برای تغییر)."
                : "خواندن ناقص بود؛ مسیرهای ردشده را در فهرست بالا ببین.";

        return new DeviceIdentityCertificate(
            issuedAt,
            $"XID-{issuedAt:yyyyMMdd}-{Math.Abs(identity.Imei1.GetHashCode(StringComparison.Ordinal)) % 100000:D5}",
            identity.Imei1, identity.Imei2, identity.Eid,
            identity.Iccid.Length > 4 ? "••••" + identity.Iccid[^4..] : string.Empty,
            identity.Model, identity.Serial, identity.Vendor.ToString(),
            identity.BestPath,
            imei1Status, imei2Status, tac,
            identity.Attempts.Select(a => $"[{a.Badge}] {a.Path} — {a.Detail}").ToList(),
            verdict);
    }

    private static string Describe(string imei) => imei.Length switch
    {
        0 => "خوانده نشد",
        15 => ImeiNumber.MatchesLuhn(imei) ? "معتبر (Luhn)" : "۱۵ رقم ولی Luhn نامعتبر — مقدار مشکوک",
        _ => $"{imei.Length} رقم — نامعتبر"
    };
}
