namespace SIM800L_Flasher;

/// <summary>
/// بررسی‌های خودکار بدون نیاز به سخت‌افزار. با دستور «--self-test» اجرا می‌شود و کد خروج غیر از صفر یعنی شکست.
/// </summary>
internal static class SelfTests
{
    public static int Run(Action<string>? log = null)
    {
        var failures = 0;

        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                log?.Invoke($"PASS  {name}");
                return;
            }
            failures++;
            log?.Invoke($"FAIL  {name}{(detail.Length > 0 ? " — " + detail : string.Empty)}");
        }

        // --- اعتبارسنجی IMEI -------------------------------------------------
        Check("IMEI معتبر شناخته می‌شود",
            ImeiNumber.TryParse("490154203237518", out var imei1, out _) && imei1!.Tac == "49015420");

        Check("IMEI با رقم کنترلی اشتباه رد می‌شود",
            !ImeiNumber.TryParse("490154203237519", out _, out var luhnError) && luhnError.Contains("Luhn"));

        Check("IMEI با طول اشتباه رد می‌شود",
            !ImeiNumber.TryParse("123456", out _, out _));

        Check("IMEI با حرف و فاصله رد می‌شود",
            !ImeiNumber.TryParse("49015 4203237518", out _, out _));

        Check("IMEI جعلی همه‌صفر رد می‌شود",
            !ImeiNumber.TryParse("000000000000000", out _, out _));

        Check("محاسبه رقم کنترلی با Luhn سازگار است",
            ImeiNumber.ComputeCheckDigit("35693803564380") is var computed &&
            computed.Length == 15 &&
            ImeiNumber.MatchesLuhn(computed));

        Check("ماسک کردن IMEI ارقام میانی را پنهان می‌کند",
            ImeiNumber.TryParse("352099001761481", out var imei2, out _) && imei2!.Masked == "352099******1");

        // --- برنامه فلش ------------------------------------------------------
        var firmware = new FirmwarePackage("/tmp/sim800l-fw.bin", 6L * 1024 * 1024, new string('a', 64));
        var noConsent = new FlashPlan
        {
            TargetImei = imei1,
            Firmware = firmware,
            FirmwareVariant = "SIM800L 2/15",
            ModuleRespondsToAt = true,
            HasValidBackup = true,
            ConsentGiven = false
        };
        Check("نبود تأیید مسئولیت Blocker است",
            noConsent.HasBlockers &&
            noConsent.Evaluate().Any(i => i.Kind == PlanIssueKind.Blocker && i.Message.Contains("تأیید")) &&
            noConsent.Evaluate().Count(i => i.Kind == PlanIssueKind.Blocker) == 1);

        var full = noConsent with { ConsentGiven = true };
        Check("پس از تکمیل شرایط، «عدم پشتیبانی IMEI» گزارش می‌شود",
            full.Evaluate().Any(i => i.Kind == PlanIssueKind.Unsupported && i.Message.Contains("OTROMA")));

        Check("نوشتن IMEI باعث مسدود شدن فلش مجاز firmware نمی‌شود",
            !full.HasBlockers);

        Check("برنامه فلش هیچ‌گاه ادعای نوشتن هویت فیزیکی نمی‌کند",
            full.WritesPhysicalIdentity == false);

        var noBackup = noConsent with { ConsentGiven = true, HasValidBackup = false };
        Check("بدون پشتیبان، فلش مسدود است",
            noBackup.HasBlockers &&
            noBackup.Evaluate().Any(i => i.Kind == PlanIssueKind.Blocker && i.Message.Contains("پشتیبان")));

        Check("گزارش فلش دارای بخش SHA-256 و چک‌لیست است",
            full.ToReport().Contains("check list") && full.ToReport().Contains("sha256"));

        Check("چک‌لیست پشتیبان شامل حداقل ۵ آیتم است",
            full.BackupChecklist().Count >= 5);

        // --- پروفایل سیم‌کارت ------------------------------------------------
        Check("فرمان‌های بررسی UICC شامل CPIN و CCID است",
            SimCardProfile.UiccProbeCommands().Any(c => c.Contains("CPIN")) &&
            SimCardProfile.UiccProbeCommands().Any(c => c.Contains("CCID")));

        Check("توصیف کارت برای eUICC مسیر LPA را ذکر می‌کند",
            SimCardProfile.Describe("8966...", "43235...").Contains("eUICC"));

        Check("نکات ایمنی شامل ممنوعیت تغییر PIN است",
            SimCardProfile.SafetyNotes().Any(n => n.Contains("PIN")));

        // --- پاک‌سازی لاگ ----------------------------------------------------
        const string rawLog = "AT+CGSN\r\n490154203237518\r\nOK\r\nAT+CCID\r\n89610123456789012345\r\nOK";
        var redacted = SessionLogger.Redact(rawLog);
        Check("IMEI ۱۵ رقمی در لاگ پاک می‌شود", !redacted.Contains("490154203237518"));
        Check("ICCID در لاگ پاک می‌شود", !redacted.Contains("89610123456789012345"));
        Check("ساختار لاگ پس از پاک‌سازی حفظ می‌شود", redacted.Contains("AT+CGSN") && redacted.Contains("OK"));
        Check("لاگ خالی بدون خطا پاک‌سازی می‌شود", SessionLogger.Redact(null) == string.Empty);

        // --- محافظ AT --------------------------------------------------------
        AtGuard.ImeiWriteUnlocked = false;
        Check("فرمان معمولی AT مجاز است", AtGuard.IsAllowed("AT+CSQ", out _));
        Check("خواندن IMEI همیشه مجاز است", AtGuard.IsAllowed("AT+GSN", out _) && AtGuard.IsAllowed("AT+SIMEI?", out _));
        Check("نوشتن IMEI در حالت قفل مسدود است", !AtGuard.IsAllowed("AT+QIMEI=490154203237518", out _));
        Check("AT+EGMR نوشتن در حالت قفل مسدود است", !AtGuard.IsAllowed("AT+EGMR=1,7,\"490154203237518\"", out _));
        Check("AT+SIMEI نوشتن در حالت قفل مسدود است", !AtGuard.IsAllowed("AT+SIMEI=\"490154203237518\"", out _));
        Check("فرمان بدون AT مسدود است", !AtGuard.IsAllowed("QIMEI=1", out _));
        Check("نوشتن خام NV حتی در حالت باز هم مسدود است", !AtGuard.IsAllowed("AT+NVWRITE=1", out _));

        AtGuard.ImeiWriteUnlocked = true;
        Check("پس از باز کردن قفل، AT+EGMR مجاز است", AtGuard.IsAllowed("AT+EGMR=1,7,\"490154203237518\"", out _));
        Check("پس از باز کردن قفل، AT+SIMEI مجاز است", AtGuard.IsAllowed("AT+SIMEI=\"490154203237518\"", out _));
        Check("پس از باز کردن قفل هم NV خام مسدود است", !AtGuard.IsAllowed("AT+NVWRITE=1", out _));
        AtGuard.ImeiWriteUnlocked = false;

        // --- سیاست فرمان IMEI ------------------------------------------------
        Check("تشخیص فرمان نوشتن IMEI", ImeiCommandPolicy.IsImeiMutation("AT+EGMR=1,7,\"1\"") &&
            ImeiCommandPolicy.IsImeiMutation("AT+SIMEI=\"1\"") &&
            ImeiCommandPolicy.IsImeiMutation("AT+QCFG=\"IMEI\",\"1\""));
        Check("خواندن به‌عنوان نوشتن تشخیص داده نمی‌شود",
            !ImeiCommandPolicy.IsImeiMutation("AT+GSN") && !ImeiCommandPolicy.IsImeiMutation("AT+EGMR=0,7") &&
            ImeiCommandPolicy.IsImeiRead("AT+GSN"));

        // --- موتور نوشتن IMEI ------------------------------------------------
        Check("استخراج IMEI از پاسخ AT+GSN",
            ImeiWriter.ExtractImei("AT+GSN\r\r\n490154203237518\r\n\r\nOK\r\n") == "490154203237518");
        Check("استخراج IMEI، URCهای اضافی را نادیده می‌گیرد",
            ImeiWriter.ExtractImei("\r\nRDY\r\n+CFUN: 1\r\n352099001761481\r\nOK\r\n") == "352099001761481");
        Check("پاسخ ERROR شناسایی می‌شود", ImeiWriter.Classify("\r\n+CME ERROR: operation not supported\r\n") == StepStatus.Error);
        Check("پاسخ بی‌پاسخ (timeout) شناسایی می‌شود",
            ImeiWriter.Classify("(بدون پاسخ؛ مهلت دریافت تمام شد)") == StepStatus.Timeout);
        Check("پاسخ OK شناسایی می‌شود", ImeiWriter.Classify("\r\nOK\r\n") == StepStatus.Ok);
        Check("چهار استراتژی نوشتن تعریف شده است", ImeiWriter.Strategies.Count == 4);
        Check("استراتژی EGMR مقدار هدف را جایگذاری می‌کند",
            ImeiWriter.Strategies.First(s => s.Kind == WriteStrategyKind.Egmr)
                .Build("490154203237518").Single() == "AT+EGMR=1,7,\"490154203237518\"");
        Check("استراتژی QCFG به‌عنوان Quectel علامت خورده است",
            ImeiWriter.Strategies.Single(s => s.Kind == WriteStrategyKind.QcfgQuectel).QuectelOnly);
        Check("گزارش موفقیت فقط با بازخوانی یکسان ساخته می‌شود",
            BuildVerifiedResult().Verified && !BuildUnverifiedResult().Verified);
        Check("گزارش ناموفق، IMEI قبلی را برای بازگردانی نگه می‌دارد",
            ImeiWriter.BuildReport([BuildUnverifiedResult()], null, "490154203237518", "352099001761481")
                .Contains("490154203237518"));

        // --- بسته firmware ---------------------------------------------------
        var temp = Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(temp, Enumerable.Repeat((byte)0x5A, 4096).ToArray());
            var package = FirmwarePackage.Inspect(temp);
            Check("بسته firmware با SHA-256 محاسبه می‌شود",
                package.Size == 4096 && package.Sha256.Length == 64);

            var wrongExt = Path.ChangeExtension(temp, ".txt");
            File.Move(temp, wrongExt);
            var rejected = 0;
            try { FirmwarePackage.Inspect(wrongExt); }
            catch (InvalidDataException) { rejected = 1; }
            Check("پسوند غیرمجاز رد می‌شود", rejected == 1);
            File.Delete(wrongExt);
        }
        catch (Exception ex)
        {
            failures++;
            log?.Invoke($"FAIL  بررسی بسته firmware — {ex.Message}");
        }

        log?.Invoke(failures == 0 ? "همه تست‌ها موفق بود." : $"{failures} تست ناموفق بود.");
        return failures;
    }

    /// <summary>نتیجه‌ای که ماژول را تأییدشده شبیه‌سازی می‌کند (برای تست خالص منطق گزارش).</summary>
    private static WriteResult BuildVerifiedResult()
    {
        var strategy = ImeiWriter.Strategies.First(s => s.Kind == WriteStrategyKind.Egmr);
        return new WriteResult(strategy,
            [new WriteStep("AT+EGMR=1,7,\"352099001761481\"", "\r\nOK\r\n", StepStatus.Ok)],
            "490154203237518", "352099001761481", true, "تأیید شد");
    }

    /// <summary>ماژول OK می‌دهد اما شناسه تغییر نکرده است.</summary>
    private static WriteResult BuildUnverifiedResult()
    {
        var strategy = ImeiWriter.Strategies.First(s => s.Kind == WriteStrategyKind.Simei);
        return new WriteResult(strategy,
            [new WriteStep("AT+SIMEI=\"352099001761481\"", "\r\nOK\r\n", StepStatus.Ok)],
            "490154203237518", "490154203237518", false, "تغییر نکرد");
    }
}
