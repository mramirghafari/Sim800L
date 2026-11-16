using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// «قرض‌گیری هویت»: سیم‌کارت را با IMEI دستگاهی که در همتا ثبت شده، روی شبکه بالا می‌آوریم
/// تا خودِ سیم‌کارت سرویس بگیرد و بشود آن را در دستگاه دیگری (گوشی/ماژول دیگر) گذاشت.
///
/// این فایل فقط «آنچه واقعاً اتفاق می‌افتد» را مدل می‌کند و همان چیزی را می‌گوید که بعد از
/// تعویض دستگاه قابل مشاهده است:
///   IMEI در حافظه NV خودِ دستگاه (ماژول/گوشی) است و در پیام Identity Response به شبکه می‌رود.
///   سیم‌کارت هیچ فرمانی برای تعیین یا جایگزینی آن ندارد، پس IMEI هرگز «روی سیم‌کارت» ذخیره
///   نمی‌شود و با جابه‌جا کردن کارت همراه آن نمی‌رود.
/// هدف این کلاس صادق بودن خروجی است: هر گام یا اثر واقعی دارد، یا صریحاً «بی‌اثر» علامت می‌خورد.
/// </summary>
internal enum LoanerStepState
{
    /// <summary>گام با AT قابل انجام است و اثر واقعی روی همین ماژول دارد.</summary>
    Actionable,

    /// <summary>گام فقط با دستکاری خود دستگاه مقصد ممکن است (فلش/ابزار سازنده).</summary>
    RequiresDeviceSideChange,

    /// <summary>گام هیچ اثری ندارد؛ انجامش فقط وقت و سهمیه نوشتن را هدر می‌دهد.</summary>
    NoEffect
}

internal sealed record LoanerStep(
    int Order,
    string Title,
    string Command,
    LoanerStepState State,
    string Effect)
{
    public string Badge => State switch
    {
        LoanerStepState.Actionable => "اثر واقعی",
        LoanerStepState.RequiresDeviceSideChange => "فقط در دستگاه مقصد",
        _ => "بی‌اثر روی سیم‌کارت"
    };
}

internal sealed record LoanerPlan(
    string LoanerImei,
    string LoanerImei2,
    string LoanerSerial,
    string TargetDevice,
    string TargetImei,
    IReadOnlyList<LoanerStep> Steps,
    string Verdict,
    string Warning)
{
    public bool SimCarriesImei => false;

    public int ActionableCount => Steps.Count(s => s.State == LoanerStepState.Actionable);

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("نقشه «قرض‌گیری IMEI» — سیم‌کارت به دستگاه دوم");
        text.AppendLine(new string('─', 62));
        text.AppendLine($"IMEI دستگاه ثبت‌شده (قرض‌دهنده): {Format(LoanerImei)}");
        if (LoanerImei2.Length == 15) text.AppendLine($"IMEI2 همان دستگاه: {Format(LoanerImei2)}");
        if (LoanerSerial.Length > 0) text.AppendLine($"سریال دستگاه قرض‌دهنده: {LoanerSerial}");
        text.AppendLine($"دستگاه مقصد: {TargetDevice}");
        text.AppendLine($"IMEI فعلی دستگاه مقصد: {Format(TargetImei)}");
        text.AppendLine();

        foreach (var step in Steps)
            text.AppendLine($"{step.Order}) [{step.Badge}] {step.Title} → {step.Command}\n     {step.Effect}");

        text.AppendLine();
        text.AppendLine("حکم:");
        text.AppendLine(Verdict);
        text.AppendLine();
        text.AppendLine("هشدار:");
        text.AppendLine(Warning);
        return text.ToString();
    }

    private static string Format(string imei) => imei.Length == 15 ? imei : "خوانده نشد";

    public static LoanerPlan Build(
        string loanerImei,
        string loanerImei2,
        string loanerSerial,
        string targetDevice,
        string targetImei)
    {
        var steps = new List<LoanerStep>
        {
            new(1,
                "نوشتن IMEI قرض‌دهنده روی NV همین ماژول",
                "AT+SIMEI=\"<IMEI-قرض‌دهنده>\"  یا  AT+EGMR=1,7,\"<IMEI-قرض‌دهنده>\"",
                LoanerStepState.Actionable,
                "اگر firmware بنویسد، همین ماژول با آن هویت ثبت شبکه می‌شود. روی ماژول شما " +
                "AT+SIMEI پاسخ OK داد ولی مقدار عوض نشد، پس این گام باید با «6) تلاش مجدد با ری‌استارت» تأیید شود."),

            new(2,
                "تأیید با بازخوانی و ری‌استارت",
                "AT+GSN  →  AT+CFUN=1,1  →  AT+GSN",
                LoanerStepState.Actionable,
                "تنها معیار موفقیت: عدد بازخوانی‌شده دقیقاً برابر IMEI قرض‌دهنده باشد، هم قبل و هم بعد از ری‌استارت."),

            new(3,
                "ثبت جفت IMSI↔IMEI در پروفایل",
                "(بدون فرمان AT — ذخیره در sim-profiles.json)",
                LoanerStepState.Actionable,
                "برنامه نگه می‌دارد کدام سیم‌کارت با کدام IMEI مجاز است، تا وقتی کارت را برگرداندی دوباره همان IMEI نوشته شود."),

            new(4,
                "انتظار برای نوشته شدن IMEI روی سیم‌کارت",
                "AT+CRSM=176,28542,0,0,11  (خواندن EF_LOCI)",
                LoanerStepState.NoEffect,
                "کارت هیچ‌وقت IMEI را نگه نمی‌دارد؛ کلاس دسترسی UPDATE این فایل ADM است و پاسخ معمول 6982 است. " +
                "این گام در برنامه فقط برای اثبات همین موضوع است، نه برای تغییر."),

            new(5,
                "اعلام IMEI قرض‌دهنده توسط دستگاه مقصد",
                "—",
                LoanerStepState.RequiresDeviceSideChange,
                "دستگاه مقصد (گوشی) هویت خودش را از بیس‌باند/NV می‌خواند. تا وقتی IMEI در NV خودِ آن دستگاه عوض نشود، " +
                "شبکه همان IMEI غیرمجاز را می‌بیند و آنتن می‌پرد — حتی اگر سیم‌کارت سالم باشد.")
        };

        var targetKind = targetDevice.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                         targetDevice.Contains("iOS", StringComparison.OrdinalIgnoreCase)
            ? "ios"
            : "android";

        var verdict = targetKind == "ios"
            ? "روی آیفون این مسیر بسته است: IMEI در NV امضاشده بیس‌باند است و هیچ ابزار یا جیلبریکی آن را قابل نوشتن نمی‌کند. " +
              "سیم‌کارت در آیفون همان IMEI آیفون را اعلام می‌کند، پس نوشتن IMEI روی ماژول هیچ اثری روی آیفون ندارد. " +
              "برای آیفون تنها مسیر: ثبت همان IMEI در همتا، یا استفاده از خود گوشی ثبت‌شده."
            : "روی اندروید فقط وقتی اثر دارد که IMEI در NV بیس‌باند نوشته شود (MediaTek با ابزار META، Qualcomm با QPST/QXDM و پشتیبان QCN). " +
              "بازنویسی روی گوشی‌های ۲۰۲۰+ اغلب با secure boot و auth file بسته است. نوشتن روی ماژول SIM800L هم فقط هویت خودِ ماژول را عوض می‌کند، نه گوشی.";

        var warning =
            "اگر IMEI قرض‌دهنده هم‌زمان در دو دستگاه فعال باشد، اپراتور تکرار می‌بیند و می‌تواند هر دو را ببندد. " +
            "دستگاه قرض‌دهنده را خاموش نگه دار و IMEI آن را در دفتر IMEI ثبت کن تا بتوانی برگردانی.";

        return new LoanerPlan(
            loanerImei ?? string.Empty,
            loanerImei2 ?? string.Empty,
            loanerSerial ?? string.Empty,
            string.IsNullOrWhiteSpace(targetDevice) ? "دستگاه مقصد" : targetDevice.Trim(),
            targetImei ?? string.Empty,
            steps,
            verdict,
            warning);
    }
}
