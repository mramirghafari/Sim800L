namespace SIM800L_Flasher;

internal enum WriteStrategyKind { CfwToggle, Egmr, Simei, QcfgQuectel }

internal sealed record WriteStrategy(
    WriteStrategyKind Kind,
    string Id,
    string Title,
    string[] Commands,
    string Notes,
    bool QuectelOnly = false,
    bool SimMustBeRemoved = false)
{
    public string[] Build(string imei) =>
        Commands.Select(c => c.Replace("{IMEI}", imei, StringComparison.Ordinal)).ToArray();
}

internal enum StepStatus { Ok, Error, Timeout }

internal sealed record WriteStep(string Command, string Response, StepStatus Status)
{
    public bool Succeeded => Status == StepStatus.Ok;
}

internal sealed record WriteResult(
    WriteStrategy Strategy,
    IReadOnlyList<WriteStep> Steps,
    string ImeiBefore,
    string ImeiAfter,
    bool Verified,
    string Summary)
{
    public bool AcceptedButNotPersisted =>
        !Verified && Steps.Count > 0 && Steps.All(s => s.Succeeded) &&
        !string.IsNullOrEmpty(ImeiBefore) && ImeiBefore == ImeiAfter;
}

internal sealed record AutoWriteOutcome(
    IReadOnlyList<WriteResult> Attempts,
    WriteResult? Success,
    string ImeiBefore,
    string Report);

/// <summary>
/// موتور نوشتن IMEI روی ماژول. چون رفتار firmware خانواده SIM800 یکسان نیست، چند استراتژی شناخته‌شده
/// به‌ترتیب امتحان می‌شود و صحت کار همیشه با «بازخوانی» (AT+GSN / AT+CGSN / AT+SIMEI?) تأیید می‌گردد.
/// موفقیت فقط زمانی گزارش می‌شود که شناسه خوانده‌شده دقیقاً برابر مقدار هدف باشد.
/// </summary>
internal sealed class ImeiWriter
{
    public static readonly IReadOnlyList<WriteStrategy> Strategies =
    [
        new(WriteStrategyKind.CfwToggle,
            "cfw-egmr",
            "١) خاموش‌کردن رادیو + EGMR (الگوی رایج SIM800/SIM900)",
            ["AT+CFUN=0", "AT+EGMR=0,7", "AT+EGMR=1,7,\"{IMEI}\"", "AT+CFUN=1"],
            "ابتدا AT+EGMR=0,7 خوانده می‌شود؛ اگر firmware پشتیبانی نکند، همان‌جا متوقف می‌شود.",
            SimMustBeRemoved: true),

        new(WriteStrategyKind.Egmr,
            "egmr",
            "٢) نوشتن مستقیم AT+EGMR",
            ["AT+EGMR=1,7,\"{IMEI}\""],
            "روی بعضی ریویژن‌ها بدون خاموش‌کردن رادیو کار می‌کند.",
            SimMustBeRemoved: true),

        new(WriteStrategyKind.Simei,
            "simei",
            "٣) فرمان اختصاصی SIMCom: AT+SIMEI",
            ["AT+SIMEI?", "AT+SIMEI=\"{IMEI}\""],
            "در بعضی firmwareهای SIM800C/SIM800L وجود دارد و تعداد نوشتن را محدود می‌کند (معمولاً ۳ بار).",
            SimMustBeRemoved: false),

        new(WriteStrategyKind.QcfgQuectel,
            "qcfg",
            "٤) AT+QCFG (مخصوص Quectel — فقط برای امتحان)",
            ["AT+QCFG=\"imei\",1", "AT+QCFG=\"imei\",\"{IMEI}\"", "AT+QCFG=\"imei\",2"],
            "این فرمان متعلق به Quectel است؛ روی SIM800L تقریباً همیشه ERROR می‌دهد و آسیبی هم نمی‌زند.",
            QuectelOnly: true)
    ];

    private readonly Sim800Device _device;
    private readonly Action<string, string>? _log;

    public ImeiWriter(Sim800Device device, Action<string, string>? log = null)
    {
        _device = device;
        _log = log;
    }

    /// <summary>استخراج IMEI از پاسخ خام؛ URCها و OK/ERROR نادیده گرفته می‌شوند.</summary>
    public static string ExtractImei(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return string.Empty;

        foreach (var raw in response.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains('+', StringComparison.Ordinal)) continue;
            if (line.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("RDY", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("AT", StringComparison.OrdinalIgnoreCase)) continue;

            var digits = new string(line.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 14 and <= 16) return digits;
        }
        return string.Empty;
    }

    public static StepStatus Classify(string? response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.StartsWith("(بدون پاسخ", StringComparison.Ordinal))
            return StepStatus.Timeout;
        if (response.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) return StepStatus.Error;
        return StepStatus.Ok;
    }

    public async Task<string> ReadImeiAsync(CancellationToken ct = default)
    {
        foreach (var command in new[] { "AT+GSN", "AT+CGSN" })
        {
            var value = ExtractImei(await _device.SendAsync(command, 2500, ct));
            if (value.Length == 15) return value;
        }
        return string.Empty;
    }

    /// <summary>پس از ری‌استارت، تا آماده شدن ماژول صبر می‌کند.</summary>
    private async Task<string> ReadImeiAfterRestartAsync(int attempts, CancellationToken ct)
    {
        for (var i = 0; i < attempts; i++)
        {
            await Task.Delay(900, ct);
            var value = await ReadImeiAsync(ct);
            if (value.Length == 15) return value;
        }
        return string.Empty;
    }

    public async Task<WriteResult> WriteAsync(
        WriteStrategy strategy,
        string imei,
        string imeiBefore,
        bool persistProfile,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var steps = new List<WriteStep>();
        var commands = strategy.Build(imei);

        foreach (var command in commands)
        {
            progress?.Report($"{strategy.Title} → {command}");
            string response;
            StepStatus status;
            try
            {
                response = await _device.SendAsync(command, 4000, ct);
                status = Classify(response);
            }
            catch (Exception ex)
            {
                response = ex.Message;
                status = StepStatus.Timeout;
            }

            _log?.Invoke(command, response);
            steps.Add(new WriteStep(command, response, status));

            // خطای صریح یعنی این استراتژی روی این firmware وجود ندارد؛ ادامه دادن بی‌فایده و پرخطر است.
            if (status == StepStatus.Error) break;
        }

        if (persistProfile && steps.Count > 0 && steps[^1].Succeeded)
        {
            var persist = await _device.SendAsync("AT&W", 3000, ct);
            _log?.Invoke("AT&W", persist);
            steps.Add(new WriteStep("AT&W", persist, Classify(persist)));
        }

        var needsRestart = strategy.Kind is WriteStrategyKind.CfwToggle or WriteStrategyKind.QcfgQuectel ||
                           steps.Any(s => s.Command.StartsWith("AT+CFUN=1", StringComparison.OrdinalIgnoreCase));

        var imeiAfter = needsRestart
            ? await ReadImeiAfterRestartAsync(8, ct)
            : await ReadImeiAsync(ct);

        if (imeiAfter.Length != 15) imeiAfter = await ReadImeiAfterRestartAsync(4, ct);

        var verified = imeiAfter == imei;
        var summary = verified
            ? "تأیید شد: شناسه خوانده‌شده با مقدار هدف یکسان است."
            : steps.Count == 0
                ? "هیچ فرمانی اجرا نشد."
                : steps[^1].Status == StepStatus.Error
                    ? "ماژول فرمان را نپذیرفت (ERROR)."
                    : imeiAfter == imeiBefore
                        ? "فرمان‌ها OK گرفتند اما شناسه تغییر نکرد؛ در این firmware نوشتن پایدار نمی‌شود."
                        : $"شناسه خوانده‌شده «{imeiAfter}» با هدف فرق دارد.";

        return new WriteResult(strategy, steps, imeiBefore, imeiAfter, verified, summary);
    }

    /// <summary>
    /// استراتژی‌ها را به‌ترتیب امتحان می‌کند و در اولین موفقیت متوقف می‌شود. پشتیبان IMEI اصلی
    /// همیشه در خروجی و در لاگ ذخیره می‌شود تا در صورت خرابی قابل بازگردانی باشد.
    /// </summary>
    public async Task<AutoWriteOutcome> AutoWriteAsync(
        string imei,
        bool persistProfile,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var attempts = new List<WriteResult>();
        var imeiBefore = await ReadImeiAsync(ct);
        progress?.Report($"IMEI فعلی: {(imeiBefore.Length == 15 ? imeiBefore : "نامشخص")}");

        WriteResult? success = null;
        foreach (var strategy in Strategies)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"تلاش با {strategy.Title}");

            var result = await WriteAsync(strategy, imei, imeiBefore, persistProfile, progress, ct);
            attempts.Add(result);
            if (result.Verified)
            {
                success = result;
                break;
            }
        }

        return new AutoWriteOutcome(attempts, success, imeiBefore, BuildReport(attempts, success, imeiBefore, imei));
    }

    public static string BuildReport(
        IReadOnlyList<WriteResult> attempts,
        WriteResult? success,
        string imeiBefore,
        string imeiTarget)
    {
        var lines = new List<string>
        {
            "گزارش عملیات نوشتن IMEI",
            new string('-', 46),
            $"IMEI اصلی (پشتیبان): {(imeiBefore.Length == 15 ? imeiBefore : "خوانده نشد")}",
            $"IMEI هدف: {imeiTarget}",
            string.Empty
        };

        foreach (var attempt in attempts)
        {
            lines.Add($"• {attempt.Strategy.Title}");
            lines.Add($"  نتیجه: {(attempt.Verified ? "موفق" : "ناموفق")} — {attempt.Summary}");
            foreach (var step in attempt.Steps)
            {
                var tag = step.Status switch
                {
                    StepStatus.Ok => "OK",
                    StepStatus.Error => "ERROR",
                    _ => "بی‌پاسخ"
                };
                lines.Add($"    [{tag}] {step.Command} → {Shorten(step.Response)}");
            }
            lines.Add(string.Empty);
        }

        lines.Add(success is not null
            ? $"نتیجه نهایی: IMEI با موفقیت به {imeiTarget} نوشته و بازخوانی شد."
            : "نتیجه نهایی: هیچ‌کدام از استراتژی‌ها شناسه را تغییر نداد. راهنمای پایین را ببینید.");
        lines.Add("یادآوری: در حین نوشتن، سیم‌کارت را خارج کنید، تغذیه پایدار باشد و ماژول را ری‌استارت کنید.");
        lines.Add("اگر همه استراتژی‌ها ERROR دادند، firmware ماژول شما فرمان نوشتن ندارد و تنها مسیر، فلش بسته اختصاصی همان revision است.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Shorten(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "(خالی)";
        var flat = string.Join(" ", response.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 90 ? flat : flat[..90] + "…";
    }
}
