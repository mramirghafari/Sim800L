using System.Text;

namespace SIM800L_Flasher;

internal sealed record PersistenceResult(
    string TargetImei,
    string ImeiBeforeRestart,
    string ImeiAfterRestart,
    bool RestartDetected,
    bool Persisted,
    string Detail,
    int Cycles = 0,
    bool PowerCycleRequired = false)
{
    public bool ExpectedNeverWritten => ImeiBeforeRestart.Length != 15 && !Persisted;

    /// <summary>آیا نتیجه بر اساس چند چرخه کامل است (۳ چرخه برای اطمینان بیشتر).</summary>
    public bool MultiCycle => Cycles >= 3;

    public string Confidence => Persisted && MultiCycle
        ? "بالا — ۳ چرخه ری‌استارت/برق را پشت سر گذاشت"
        : Persisted
            ? "متوسط — فقط یک چرخه (برای اطمینان ۳ چرخه را اجرا کنید)"
            : "پایدار نشد";
}

/// <summary>
/// تأیید پایداری IMEI: بعد از نوشتن، ماژول را نرم‌ری‌استارت می‌کند (CFUN=1,1) و می‌بیند مقدار
/// پس از بالا آمدن دوباره همان مقدار هدف است یا نه. اگر ری‌استارت نرم ممکن نبود، از کاربر
/// می‌خواهد برق را قطع/وصل کند و دوباره بررسی می‌کند.
/// </summary>
internal sealed class PersistenceVerifier
{
    private readonly Sim800Device _device;

    public PersistenceVerifier(Sim800Device device) => _device = device;

    private static async Task<string> ReadImeiAsync(Sim800Device device, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var value = ImeiWriter.ExtractImei(await device.SendAsync("AT+GSN", 2500, ct));
            if (value.Length == 15) return value;
            await Task.Delay(600, ct);
        }
        return string.Empty;
    }

    /// <summary>ری‌استارت نرم با AT+CFUN=1,1 و سپس انتظار برای پاسخ AT.</summary>
    public async Task<bool> SoftRestartAsync(CancellationToken ct = default)
    {
        await _device.SendAsync("AT+CFUN=1,1", 4000, ct);
        await Task.Delay(2500, ct);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var response = await _device.SendAsync("AT", 2000, ct);
                if (response.Contains("OK", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                // در زمان ری‌استارت، پورت می‌تواند موقتاً خطا بدهد.
            }
            await Task.Delay(800, ct);
        }
        return false;
    }

    /// <summary>
    /// پایداری را در چند چرخه پشت سر هم می‌سنجد. اگر در هر چرخه مقدار برگشت، همان‌جا متوقف
    /// می‌شود و ناپایدار گزارش می‌دهد. <paramref name="cycles"/> پیشنهادی ۳ است.
    /// </summary>
    public async Task<PersistenceResult> VerifyMultiCycleAsync(
        string target,
        int cycles = 3,
        Func<bool>? waitForManualCycle = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        cycles = Math.Clamp(cycles, 1, 10);
        var detail = new StringBuilder();
        detail.AppendLine($"تأیید پایداری در {cycles} چرخه برای IMEI {target}");
        detail.AppendLine(new string('-', 46));

        var last = await ReadImeiAsync(_device, ct);
        var powerCycleNeeded = false;
        var completed = 0;

        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            progress?.Report($"چرخه {cycle} از {cycles}: ری‌استارت و بازخوانی");
            var softOk = false;
            try { softOk = await SoftRestartAsync(ct); } catch { softOk = false; }

            if (!softOk)
            {
                powerCycleNeeded = true;
                progress?.Report("ری‌استارت نرم ممکن نشد؛ منتظر قطع/وصل برق دستی هستم.");
                waitForManualCycle?.Invoke();
                await Task.Delay(1500, ct);
            }

            var current = await ReadImeiAsync(_device, ct);
            completed++;
            detail.AppendLine($"چرخه {cycle}: {(softOk ? "ری‌استارت نرم" : "قطع/وصل برق")} → {(current.Length == 15 ? current : "بدون پاسخ")}");

            if (current != target)
            {
                detail.AppendLine(current.Length != 15
                    ? "ماژول پاسخ نداد؛ کابل/تغذیه را بررسی و دوباره تلاش کنید."
                    : "مقدار تغییر کرد؛ این firmware مقدار را پایدار نگه نمی‌دارد.");
                return new PersistenceResult(target, last, current, softOk, false, detail.ToString(), completed, powerCycleNeeded);
            }

            last = current;
        }

        detail.AppendLine();
        detail.AppendLine($"✅ پایدار: IMEI {target} در هر {completed} چرخه ثابت ماند.");
        detail.AppendLine("برای نتیجه قطعی، یک بار هم برق ماژول را کامل قطع و وصل کنید و بازخوانی را تکرار کنید.");
        return new PersistenceResult(target, target, last, true, true, detail.ToString(), completed, powerCycleNeeded);
    }

    /// <summary>
    /// پس از نوشتن، پایداری را بررسی می‌کند. اگر ری‌استارت نرم موفق نباشد،
    /// <paramref name="waitForManualCycle"/> کاربر را برای قطع/وصل برق راهنمایی می‌کند.
    /// </summary>
    public async Task<PersistenceResult> VerifyAsync(
        string target,
        Action? waitForManualCycle = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var before = await ReadImeiAsync(_device, ct);
        progress?.Report($"IMEI پیش از ری‌استارت: {(before.Length == 15 ? before : "نامشخص")}");

        var softOk = false;
        try
        {
            softOk = await SoftRestartAsync(ct);
        }
        catch (Exception)
        {
            softOk = false;
        }

        if (!softOk)
        {
            progress?.Report("ری‌استارت نرم موفق نبود؛ منتظر قطع/وصل برق دستی هستم.");
            waitForManualCycle?.Invoke();
            await Task.Delay(1500, ct);
        }

        var after = await ReadImeiAsync(_device, ct);
        var persisted = after == target;

        var detail = new StringBuilder();
        detail.AppendLine($"IMEI هدف            : {target}");
        detail.AppendLine($"پیش از ری‌استارت     : {(before.Length == 15 ? before : "نامشخص")}");
        detail.AppendLine($"پس از ری‌استارت      : {(after.Length == 15 ? after : "نامشخص")}");
        detail.AppendLine($"روش ری‌استارت       : {(softOk ? "نرم (AT+CFUN=1,1)" : "برق دستی")}");
        detail.AppendLine(persisted
            ? "نتیجه: ✅ پایدار — IMEI جدید روی حافظه ماژول ماندگار است."
            : before == target
                ? "نتیجه: ⚠️ ناپایدار — مقدار در RAM نوشته شده و با ری‌استارت برگشت."
                : after.Length != 15
                    ? "نتیجه: ماژول بعد از ری‌استارت پاسخ نداد؛ کابل/تغذیه را بررسی و دوباره بررسی کنید."
                    : "نتیجه: ❌ نوشتن پایدار نشد.");

        return new PersistenceResult(target, before, after, softOk, persisted, detail.ToString());
    }
}
