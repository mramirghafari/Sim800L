using System.Text;

namespace SIM800L_Flasher;

/// <summary>نتیجه بررسی جابه‌جایی برد بین دو گوشی.</summary>
internal enum BoardSwapVerdict
{
    /// <summary>IMEI اصلی روی برد سالم است و همان را می‌توان در همتا ثبت کرد — نوشتن لازم نیست.</summary>
    RegisterExistingImei,

    /// <summary>IMEI برد خالی/خراب است و با پشتیبان همان برد قابل بازگردانی است.</summary>
    RestoreFromBackup,

    /// <summary>نمی‌شود کاری کرد؛ جفت‌سازی گواهی/مودم از بیرون ممکن نیست.</summary>
    NotRecoverable,

    /// <summary>اطلاعات کافی نیست؛ اول باید IMEI برد خوانده شود.</summary>
    NeedReading
}

internal sealed record BoardSwapStep(int Order, string Title, string Why, bool RequiresWriting);

internal sealed record BoardSwapAssessment(
    string OldDevice,
    string NewDevice,
    string BoardImei,
    string BoardImei2,
    string SimIccid,
    BoardSwapVerdict Verdict,
    IReadOnlyList<BoardSwapStep> Steps,
    string HtaNote,
    string Summary)
{
    public bool ImpliesImeiRewrite => Steps.Any(step => step.RequiresWriting);

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("بررسی انتقال برد از گوشی قدیمی به گوشی جدید");
        text.AppendLine(new string('═', 66));
        text.AppendLine($"گوشی قدیمی (منبع برد): {Show(OldDevice)}");
        text.AppendLine($"گوشی جدید (مقصد): {Show(NewDevice)}");
        text.AppendLine($"IMEI خوانده‌شده از برد: {Show(BoardImei)}");
        text.AppendLine($"IMEI2: {Show(BoardImei2)}");
        text.AppendLine($"ICCID سیم‌کارت: {Show(SimIccid)}");
        text.AppendLine();
        text.AppendLine("حکم:");
        text.AppendLine(Verdict switch
        {
            BoardSwapVerdict.RegisterExistingImei => "IMEI اصلی روی همین برد سالم است؛ نوشتن لازم نیست — فقط ثبت/انتقال در همتا.",
            BoardSwapVerdict.RestoreFromBackup => "IMEI برد خالی است؛ با پشتیبان سالم همان برد قابل بازگردانی است.",
            BoardSwapVerdict.NotRecoverable => "جفت‌سازی گواهی/مودم از بیرون ممکن نیست؛ این برد قابل احیا نیست.",
            _ => "اول IMEI برد را بخوان تا حکم قطعی شود."
        });
        text.AppendLine();
        text.AppendLine("گام‌ها:");
        foreach (var step in Steps)
            text.AppendLine($"  {step.Order}) {step.Title}{(step.RequiresWriting ? "  ← نیازمند نوشتن" : "")}\n     {step.Why}");
        text.AppendLine();
        text.AppendLine("از نظر سامانه همتا:");
        text.AppendLine(HtaNote);
        text.AppendLine();
        text.AppendLine(Summary);
        return text.ToString();
    }

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static BoardSwapAssessment Build(
        string oldDevice,
        string newDevice,
        string boardImei,
        string boardImei2,
        string simIccid,
        bool boardHasBackup,
        bool sameVendor)
    {
        var damage = IdentityRecoveryPlan.Classify(boardImei);

        var verdict = damage switch
        {
            IdentityDamage.Healthy => BoardSwapVerdict.RegisterExistingImei,
            IdentityDamage.Empty or IdentityDamage.Corrupt =>
                boardHasBackup ? BoardSwapVerdict.RestoreFromBackup : BoardSwapVerdict.NotRecoverable,
            _ => BoardSwapVerdict.NeedReading
        };

        var steps = new List<BoardSwapStep>();

        switch (verdict)
        {
            case BoardSwapVerdict.RegisterExistingImei:
                steps.AddRange([
                    new BoardSwapStep(1, "IMEI برد را با «خواندن IMEI از هر مسیری» بگیر",
                        "IMEI مالِ برد است، نه قاب. بعد از انتقال برد، همان IMEI با گوشی جدید به شبکه می‌رود — بدون هیچ نوشتن.", false),
                    new BoardSwapStep(2, "IMEI را با *#06# روی گوشی جدید تطبیق بده",
                        "اگر عدد برد با آنچه *#06# نشان می‌دهد یکی بود، یعنی مسیر درست است.", false),
                    new BoardSwapStep(3, "در همتا استعلام وضعیت بگیر",
                        "IMEI برد قبلاً ثبت بوده؛ اگر وضعیت «ثبت‌شده» است، معمولاً همان کافی است.", false),
                    new BoardSwapStep(4, "اگر سامانه پذیرفت، سیم‌کارت مشتری را بگذار و ثبت شبکه را چک کن",
                        "اگر آنتن آمد، پرونده بسته است؛ هیچ تغییری در هویت لازم نبود.", false),
                    new BoardSwapStep(5, "اگر سامانه پذیرفت ولی سرویس نداد، سراغ ثبت نوع دستگاه برو",
                        "مدل/برند در همتا ممکن است با قاب جدید فرق کند؛ «دستیار ثبت در همتا» مسیر را می‌سازد.", false)
                ]);
                break;

            case BoardSwapVerdict.RestoreFromBackup:
                steps.AddRange([
                    new BoardSwapStep(1, "پشتیبان EFS/NVRAM همان برد را پیدا کن",
                        "NVRAM/EFS مالِ همان برد است؛ پشتیبان برد دیگر هویت را قاطی می‌کند.", false),
                    new BoardSwapStep(2, "پارتیشن هویت را Restore کن",
                        "QCN روی Qualcomm، NVRAM/NVDATA روی MediaTek. نتیجه: IMEI اصلی همان برد برمی‌گردد.", true),
                    new BoardSwapStep(3, "بعد از Restore، IMEI را دوباره بخوان",
                        "فقط با تطبیق مقدار اصلی، بازگردانی موفق است.", false)
                ]);
                break;

            case BoardSwapVerdict.NotRecoverable:
                steps.Add(new BoardSwapStep(1, "این برد قابل احیا نیست",
                    boardHasBackup
                        ? "پشتیبان داری ولی IMEI خوانده نشد؛ اول با پشتیبان Restore کن و دوباره بخوان."
                        : "نه IMEI سالم دارد و نه پشتیبان. نوشتن عدد دلخواه روی برد، امضا را می‌شکند.", false));
                break;

            default:
                steps.Add(new BoardSwapStep(1, "اول IMEI برد را بخوان",
                    "بدون مقدار فعلی، هیچ حکمی درباره نوشتن یا ثبت معنا ندارد.", false));
                break;
        }

        if (verdict == BoardSwapVerdict.RegisterExistingImei && !sameVendor)
            steps.Add(new BoardSwapStep(steps.Count + 1, "توجه: برد و قاب از دو برند مختلف‌اند",
                "از نظر شبکه IMEI مهم است، ولی سامانه همتا مدل/برند را هم ثبت می‌کند؛ اگر برند قاب با IMEI نخواند، ممکن است نیاز به اصلاح اطلاعات دستگاه باشد.", false));

        var htaNote =
            "همتا IMEI را با سیم‌کارت و مشخصات دستگاه تطبیق می‌دهد. چون IMEI برد قبلاً ثبت بوده، " +
            "در حالت سالم فقط «انتقال/به‌روزرسانی مشخصات دستگاه» لازم است، نه نوشتن IMEI. " +
            "اگر سامانه برای مالکیت مدرک خواست: فاکتور گوشی قدیمی + فاکتور تعمیر/تعویض برد + برگه یکپارچگی هویت XAnten.";

        var summary = verdict == BoardSwapVerdict.RegisterExistingImei
            ? "خلاصه: در این سناریو، انتقال برد خودش هویت را منتقل می‌کند. عملیات لازم «ثبت/انتقال در همتا» است، نه تغییر IMEI."
            : "خلاصه: مسیر درست، بازگردانی پارتیشن هویت همان برد است؛ نوشتن عدد دلخواه روی برد، دستگاه را بی‌سیم می‌کند.";

        return new BoardSwapAssessment(oldDevice, newDevice, boardImei, boardImei2, simIccid,
            verdict, steps, htaNote, summary);
    }
}
