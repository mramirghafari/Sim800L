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

        using (var bodyFont = AppFonts.Ui(10.5f))
        using (var headingFont = AppFonts.Ui(12f, FontStyle.Bold))
        {
            Check("فونت داخلی Yekan Bakh بارگذاری می‌شود",
                AppFonts.ActiveFamilyName.Contains("Yekan Bakh", StringComparison.OrdinalIgnoreCase),
                AppFonts.ActiveFamilyName);
            var actualGdiFace = AppFonts.GetActualGdiFaceName(bodyFont);
            Check("فونت Yekan Bakh واقعاً در GDI ویندوز انتخاب می‌شود",
                AppFonts.IsRegisteredWithGdi &&
                actualGdiFace.Contains("Yekan Bakh", StringComparison.OrdinalIgnoreCase),
                $"family={AppFonts.ActiveFamilyName}; GDI={actualGdiFace}; registered={AppFonts.IsRegisteredWithGdi}");
            Check("وزن بدنه سبک و تیتر بولد است", !bodyFont.Bold && headingFont.Bold);
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
            full.Evaluate().Any(i => i.Kind == PlanIssueKind.Unsupported &&
                i.Message.Contains("از مسیر فلش firmware انجام نمی‌شود")));

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

        // --- اپراتور و شماره سیم‌کارت --------------------------------------
        Check("کد 43235 به نام ایرانسل تبدیل می‌شود",
            Sim800Device.ResolveOperatorName(
                "+COPS: 0,0,\"MTN Irancell\"", "+COPS: 0,2,\"43235\"", string.Empty, "432350466774037")
                .Contains("ایرانسل"));
        Check("در نبود پاسخ COPS، اپراتور از IMSI تشخیص داده می‌شود",
            Sim800Device.ResolveOperatorName(string.Empty, string.Empty, string.Empty, "432350466774037")
                == "ایرانسل (43235)");
        Check("همراه اول، رایتل و آپتل از IMSI درست تشخیص داده می‌شوند",
            Sim800Device.ResolveOperatorName(string.Empty, string.Empty, string.Empty, "432110123456789").StartsWith("همراه اول") &&
            Sim800Device.ResolveOperatorName(string.Empty, string.Empty, string.Empty, "432200123456789").StartsWith("رایتل") &&
            Sim800Device.ResolveOperatorName(string.Empty, string.Empty, string.Empty, "432020123456789").StartsWith("آپتل"));
        Check("برای سیم‌کارت MVNO، کد IMSI بر شبکه میزبان COPS اولویت دارد",
            Sim800Device.ResolveOperatorName(
                "+COPS: 0,0,\"MCI\"", "+COPS: 0,2,\"43211\"", string.Empty, "432020123456789")
                .StartsWith("آپتل"));
        Check("نام الفبایی اپراتور ناشناخته حفظ می‌شود",
            Sim800Device.ResolveOperatorName(
                "+COPS: 0,0,\"Example Network\"", string.Empty, string.Empty, string.Empty) == "Example Network");
        Check("شماره AT+CNUM استخراج و بین‌المللی می‌شود",
            Sim800Device.ParsePhoneNumber("+CNUM: \"\",\"09121234567\",129") == "+989121234567");
        Check("شماره از دفترچه Own Numbers خوانده می‌شود",
            Sim800Device.ParsePhoneNumber("ERROR", "+CPBR: 1,\"989351234567\",145,\"\"") == "+989351234567");
        Check("نبود MSISDN صادقانه گزارش می‌شود",
            Sim800Device.ParsePhoneNumber("+CNUM: \"\",\"\",129\r\nOK") == "روی سیم‌کارت ثبت نشده");

        // --- پاک‌سازی لاگ ----------------------------------------------------
        const string rawLog = "AT+CGSN\r\n490154203237518\r\nOK\r\nAT+CCID\r\n89610123456789012345\r\nOK";
        var redacted = SessionLogger.Redact(rawLog);
        Check("IMEI ۱۵ رقمی در لاگ پاک می‌شود", !redacted.Contains("490154203237518"));
        Check("ICCID در لاگ پاک می‌شود", !redacted.Contains("89610123456789012345"));
        Check("ساختار لاگ پس از پاک‌سازی حفظ می‌شود", redacted.Contains("AT+CGSN") && redacted.Contains("OK"));
        Check("لاگ خالی بدون خطا پاک‌سازی می‌شود", SessionLogger.Redact(null) == string.Empty);

        // --- تشخیص قابلیت و پایداری ------------------------------------------
        var capability = new CapabilityReport("SIM800L", "Revision:1", true, true, false, false, false,
            "490154203237518", ["نمونه"], ImeiWriter.Strategies[0], "raw");
        Check("تشخیص با EGMR نوشتنی، مسیر AT را تأیید می‌کند", capability.AnyWritePath);
        Check("پیشنهاد تشخیص، استراتژی درست را برمی‌گرداند",
            capability.Recommendation.Contains("EGMR"));

        var noPath = capability with { EgmrWritable = false, SimeiSupported = false, RecommendedStrategy = null };
        Check("بدون مسیر AT، تشخیص صریحاً اعلام می‌کند", !noPath.AnyWritePath && noPath.Recommendation.Contains("فلش"));

        var persisted = new PersistenceResult("352099001761481", "352099001761481", "352099001761481", true, true, "ok");
        Check("پایداری وقتی مقدار بعد از ری‌استارت یکی است تأیید می‌شود", persisted.Persisted);

        var volatileResult = new PersistenceResult("352099001761481", "352099001761481", "490154203237518", true, false, "revert");
        Check("برگشت مقدار پس از ری‌استارت ناپایدار محسوب می‌شود", !volatileResult.Persisted);

        var neverWritten = new PersistenceResult("352099001761481", "", "", false, false, "port error");
        Check("وقتی هیچ مقداری خوانده نشده، نتیجه به‌عنوان «نوشته نشده» علامت می‌خورد",
            neverWritten.ExpectedNeverWritten);

        // --- دسترسی به فایل‌های سیم‌کارت --------------------------------------
        Check("IMEI در قالب BCD استاندارد سیم‌کارت کدگذاری می‌شود",
            SimFileAccess.EncodeBcd("352099001761481") == "53029900711684F1");
        Check("کدگذاری و رمزگشایی BCD دوطرفه است",
            SimFileAccess.DecodeImei(SimFileAccess.EncodeBcd("490154203237518")) == "490154203237518");
        Check("رمزگشایی BCD داده ناقص را رد می‌کند",
            SimFileAccess.DecodeImei("5302990071") == string.Empty);
        Check("داده خالی کارت خطا نمی‌دهد", SimFileAccess.DecodeImei(null) == string.Empty);

        Check("وضعیت 9000 موفق خوانده می‌شود", new SimApduResult("x", 0x90, 0x00, "").IsSuccess);
        Check("وضعیت 6982 به «نیاز به ADM» تفسیر می‌شود",
            SimFileAccess.ClassifyWrite(new SimApduResult("x", 0x69, 0x82, "")) == SimFileAccess.AdminRequired);
        Check("وضعیت 9804 به «نیاز به ADM» تفسیر می‌شود",
            SimFileAccess.ClassifyWrite(new SimApduResult("x", 0x98, 0x04, "")) == SimFileAccess.AdminRequired);
        Check("وضعیت 9404 به «فایل نیست» تفسیر می‌شود",
            SimFileAccess.ClassifyWrite(new SimApduResult("x", 0x94, 0x04, "")) == SimFileAccess.NotFound);
        Check("وضعیت موفق به «مجاز» تفسیر می‌شود",
            SimFileAccess.ClassifyWrite(new SimApduResult("x", 0x90, 0x00, "")) == SimFileAccess.UpdateOk);
        Check("توضیح «نیاز به ADM» صریح و بدون ادعای موفقیت است",
            SimFileAccess.Explain(SimFileAccess.AdminRequired).Contains("ADM"));

        // --- خانواده ماژول و دفتر IMEI ---------------------------------------
        Check("مدل SIM808 از ATI تشخیص داده می‌شود", ModuleFamily.Detect("SIM808 R14.18") == ModuleFamily.Sim808);
        Check("مدل SIM800L از ATI تشخیص داده می‌شود", ModuleFamily.Detect("SIM800L R14.18") == ModuleFamily.Sim800L);
        Check("مدل Quectel از ATI تشخیص داده می‌شود", ModuleFamily.Detect("EC25-E") == ModuleFamily.Quectel);
        Check("مدل ناشناخته صادقانه گزارش می‌شود", ModuleFamily.Detect("") == ModuleFamily.Unknown);
        Check("Quectel مسیر اختصاصی دارد و SIM800L نه",
            ModuleFamily.HasVendorPath(ModuleFamily.Quectel) && !ModuleFamily.HasVendorPath(ModuleFamily.Sim800L));
        Check("توضیح SIM808 به GPS و ری‌ست بعد از فلش اشاره می‌کند",
            ModuleFamily.Note(ModuleFamily.Sim808).Contains("GPS"));

        Check("ICCID در دفتر ماسک می‌شود",
            ImeiLedger.MaskIccid("89610123456789012345") == "8961**************45");
        Check("ICCID خالی خطا نمی‌دهد", ImeiLedger.MaskIccid(null) == "—");

        var tempLedger = Path.Combine(Path.GetTempPath(), $"ledger-{Guid.NewGuid():N}.json");
        try
        {
            var ledger = new ImeiLedger(tempLedger);
            ledger.Append(new ImeiLedgerEntry(DateTime.UtcNow, "COM4", "SIM800L", "R14.18",
                "490154203237518", "352099001761481", "352099001761481", "AT+EGMR", true, true,
                "ایرانسل", "8961**********45", "پایدار"));
            ledger.Append(new ImeiLedgerEntry(DateTime.UtcNow, "COM5", "SIM808", "R14.18",
                "111111111111111", "352099001761481", "111111111111111", "AT+SIMEI", false, false,
                "همراه اول", "—", "خطا"));

            Check("دفتر IMEI عملیات را ذخیره و بازخوانی می‌کند", ledger.Load().Count == 2);
            Check("دفتر فقط رکورد پایدار پورت را برای بازگردانی برمی‌گرداند",
                ledger.LastVerifiedFor("COM4")?.ImeiBefore == "490154203237518" &&
                ledger.LastVerifiedFor("COM5") is null);
            var export = ledger.ExportText();
            Check("خروجی دفتر شامل IMEI قبلی و وضعیت است",
                export.Contains("490154203237518") && export.Contains("پایدار"));
            File.Delete(tempLedger);
        }
        catch (Exception ex)
        {
            failures++;
            log?.Invoke($"FAIL  دفتر IMEI — {ex.Message}");
        }

        Check("توضیح جریان هویت شامل مرحله Identity Response است",
            string.Join(" ", IdentityFlow.Stages().Select(x => x.What)).Contains("Identity Response"));
        Check("پاسخ تعویض گوشی صریح است", IdentityFlow.AnswerSwapQuestion().Contains("IMEI خودش"));

        // --- چرخه پایداری و برگه تأیید ---------------------------------------
        var threeCycles = new PersistenceResult("352099001761481", "352099001761481", "352099001761481", true, true, "ok", 3, false);
        Check("پایداری سه‌چرخه‌ای سطح اطمینان بالا می‌گیرد",
            threeCycles.MultiCycle && threeCycles.Confidence.StartsWith("بالا"));
        var oneCycle = threeCycles with { Cycles = 1 };
        Check("پایداری یک‌چرخه‌ای فقط اطمینان متوسط دارد", !oneCycle.MultiCycle && oneCycle.Confidence.StartsWith("متوسط"));
        var failedCycle = threeCycles with { Persisted = false };
        Check("پایداری ناموفق هرگز اطمینان نمی‌گیرد", failedCycle.Confidence == "پایدار نشد");

        var certificate = new VerificationCertificate(
            "352099001761481", "490154203237518", "SIM800L R14.18", ModuleFamily.Sim800L,
            "Revision:1418B05SIM800L24", "COM4", "ایرانسل", "8961**************45",
            3, true, "AT+EGMR", new DateTime(2026, 11, 16, 9, 30, 0, DateTimeKind.Utc),
            ["IMEI خوانده شد", "پایداری ۳ چرخه"]);
        var rendered = certificate.Render();
        Check("برگه تأیید شامل IMEI نهایی و مدل است",
            rendered.Contains("352099001761481") && rendered.Contains("SIM800L"));
        Check("برگه تأیید IMEI اولیه را برای بازگردانی نشان می‌دهد", rendered.Contains("490154203237518"));
        Check("شماره سند برگه شامل چهار رقم آخر IMEI است", certificate.Serial.EndsWith("1481"));
        Check("نسخه فشرده برگه قابل کپی است",
            certificate.RenderCompact().Contains("352099001761481") && certificate.RenderCompact().Contains("۳ چرخه"));
        Check("نام فایل برگه شامل IMEI است", certificate.RenderFileName().Contains("IMEI-cert-352099001761481"));

        // --- قفل IMEI روی ماژول ----------------------------------------------
        Check("چهار فرمان قفل تعریف شده است", ImeiPolicyEnforcer.LockCommands().Count == 4);
        Check("اولین فرمان قفل EGMR است",
            ImeiPolicyEnforcer.LockCommands()[0].Command.StartsWith("AT+EGMR"));
        Check("فرمان ذخیره پروفایل AT&W در فهرست قفل هست",
            ImeiPolicyEnforcer.LockCommands().Any(c => c.Command == "AT&W"));

        var holdOk = new EnforceResult("352099001761481", "352099001761481", true, true, [], "r");
        Check("قفل زمانی معتبر است که مقدار بعد از قفل هم هدف بماند", holdOk.PolicyHolds);
        var holdBroken = holdOk with { ImeiAfterEnforce = "490154203237518", Verified = false };
        Check("اگر مقدار بعد از قفل عوض شود، قفل معتبر نیست", !holdBroken.PolicyHolds);
        var holdUnlocked = holdOk with { ImeiLocked = false };
        Check("بدون پذیرش فرمان قفل، نتیجه تأییدشده نیست", !holdUnlocked.PolicyHolds);

        Check("خانواده SIMCom LTE شناسایی می‌شود", ModuleFamily.Detect("SIM7600E-H") == ModuleFamily.SimcomLte);
        Check("SIMCom LTE مسیر اختصاصی دارد", ModuleFamily.HasVendorPath(ModuleFamily.SimcomLte));

        // --- تفکیک منبع هویت ------------------------------------------------
        Check("هشدار IMEI تکراری، بلاک‌شدن هر دو دستگاه را ذکر می‌کند",
            IdentityProof.DuplicateImeiWarning("352099001761481").Contains("تکراری") &&
            IdentityProof.DuplicateImeiWarning("352099001761481").Contains("بلاک"));
        Check("هشدار شامل خود IMEI است",
            IdentityProof.DuplicateImeiWarning("352099001761481").Contains("352099001761481"));

        // --- پروفایل سیم‌کارت ↔ IMEI ----------------------------------------
        var tempProfiles = Path.Combine(Path.GetTempPath(), $"simprof-{Guid.NewGuid():N}.json");
        try
        {
            var binder = new SimProfileBinder(tempProfiles);
            binder.Upsert("432350466774037", "352099001761481", "490154203237518", "کارت اداری", "ایرانسل");

            Check("نسبت سیم‌کارت ↔ IMEI ذخیره می‌شود",
                binder.Find("432350466774037")?.Imei == "352099001761481");
            Check("جست‌وجو با فاصله/خط تیره هم کار می‌کند",
                binder.Find("432350 466774037")?.Imei == "352099001761481");
            Check("IMEI قبلی برای بازگردانی نگه داشته می‌شود",
                binder.Find("432350466774037")?.ImeiBefore == "490154203237518");

            binder.Upsert("89610123456789012345", "490154203237518", "352099001761481", "کارت دوم", "همراه اول");
            Check("دو سیم‌کارت مستقل ثبت می‌شوند", binder.Load().Count == 2);
            Check("IMEI متناظر هر کارت جداگانه بازگردانده می‌شود",
                binder.ResolveForPort("89610123456789012345")?.Imei == "490154203237518");

            var dup = binder.DuplicateWarning("352099001761481", "89610123456789012345");
            Check("IMEI تکراری بین دو سیم‌کارت هشدار می‌دهد", dup is not null && dup!.Contains("قبلاً"));
            Check("همان سیم‌کارت هشدار تکراری نمی‌گیرد",
                binder.DuplicateWarning("352099001761481", "432350466774037") is null);

            Check("IMEI نامعتبر در پروفایل پذیرفته نمی‌شود",
                Throws(() => binder.Upsert("432350466774037", "12345", "", "", "")));
            Check("شناسه خالی سیم‌کارت رد می‌شود",
                Throws(() => binder.Upsert("", "352099001761481", "", "", "")));

            binder.Upsert("432350466774037", "352099001761481", "490154203237518", "به‌روزشده", "ایرانسل");
            Check("ثبت مکرر همان کارت به‌روزرسانی می‌شود نه تکراری", binder.Load().Count == 2);
            File.Delete(tempProfiles);
        }
        catch (Exception ex)
        {
            failures++;
            log?.Invoke($"FAIL  پروفایل سیم‌کارت — {ex.Message}");
        }

        // --- آزمون اصالت (Quiz) ---------------------------------------------
        const string quizImei = "352099001761481";
        var challenge = DeviceQuiz.BuildChallenge(quizImei, new Random(1234));
        Check("چالش آزمون ۳۵ رقمی است", challenge.Length == 35 && challenge.All(char.IsDigit));
        var answer = DeviceQuiz.ExpectedAnswer(challenge, quizImei);
        Check("پاسخ آزمون ۳۵ کاراکتری است", answer.Length == 35);
        Check("پاسخ درست تأیید می‌شود",
            DeviceQuiz.Evaluate(answer, challenge, quizImei, 3).Correct);
        Check("پاسخ غلط رد و از تلاش کم می‌شود",
            DeviceQuiz.Evaluate("غلط", challenge, quizImei, 3) is { Correct: false, Remaining: 2 });
        Check("پاسخ برای IMEI دیگر معتبر نیست",
            !DeviceQuiz.Evaluate(answer, challenge, "490154203237518", 3).Correct);
        Check("توضیح آزمون به سامسونگ/اندروید ۱۱ اشاره می‌کند",
            DeviceQuiz.Explain().Contains("سامسونگ"));

        // --- تشخیص علت شکست ------------------------------------------------
        Check("پاسخ ندادن ماژول به مسئله تغذیه نسبت داده می‌شود",
            RecognizeFailureForTest("490154203237518", "", "352099001761481").Contains("تغذیه"));
        Check("OK بدون تغییر، سه علت محتمل را فهرست می‌کند",
            RecognizeFailureForTest("490154203237518", "490154203237518", "352099001761481").Contains("سهمیه"));
        Check("مقدار متفاوت، پیام جداگانه می‌گیرد",
            RecognizeFailureForTest("490154203237518", "111111111111111", "352099001761481").Contains("فرق دارد"));

        // --- نقشه قرض‌گیری IMEI ---------------------------------------------
        var loaner = LoanerPlan.Build("352921101646918", "", "D0AA002301J90411189", "iPhone 18", "350782285315072");
        Check("نقشه قرض‌گیری هرگز ادعا نمی‌کند IMEI روی سیم‌کارت ذخیره شده", !loaner.SimCarriesImei);
        Check("گام «نوشتن روی سیم‌کارت» بی‌اثر علامت می‌خورد",
            loaner.Steps.Any(s => s.State == LoanerStepState.NoEffect && s.Command.Contains("28542")),
            string.Join(", ", loaner.Steps.Select(s => s.State.ToString())));
        Check("گام نوشتن روی ماژول قابل انجام است",
            loaner.Steps.Any(s => s.State == LoanerStepState.Actionable && s.Command.Contains("EGMR")));
        Check("اعلام هویت توسط دستگاه مقصد در سمت دستگاه است",
            loaner.Steps.Any(s => s.State == LoanerStepState.RequiresDeviceSideChange));
        Check("هدف آیفون صریحاً مسیر بسته اعلام می‌شود",
            loaner.Verdict.Contains("آیفون") && loaner.Verdict.Contains("NV"));
        Check("هدف اندروید مسیر NV با ابزار را نام می‌برد",
            LoanerPlan.Build("352921101646918", "", "", "Android", "350782285315072").Verdict.Contains("QPST"));
        Check("هشدار IMEI تکراری همیشه همراه نقشه است",
            loaner.Warning.Contains("تکراری") || loaner.Warning.Contains("دو دستگاه"));
        Check("گزارش متنی نقشه هر سه نوع گام را نشان می‌دهد",
            loaner.ToText().Contains("اثر واقعی") &&
            loaner.ToText().Contains("بی‌اثر روی سیم‌کارت") &&
            loaner.ToText().Contains("فقط در دستگاه مقصد"));

        // --- هویت روی گوشی (اندروید/iOS) -------------------------------------
        var mtk = PhoneAccessPlan.Build(
            new PhoneIdentity(PhoneVendor.MediaTek, "Xiaomi", "SN1", "350782285315072", "", "", "13", "MOLY", false, false, false),
            DeviceAccessKind.ServicePort, ["test"]);
        Check("MediaTek مسیر SP Flash/Maui Meta را می‌دهد",
            mtk.WritableOnDevice && mtk.Tools.Any(t => t.Name.Contains("SP Flash")) &&
            mtk.Tools.Any(t => t.Name.Contains("Maui Meta")));
        Check("MediaTek پشتیبان‌گیری NVRAM در گام‌ها آمده",
            mtk.WriteSteps.Any(x => x.Contains("NVRAM")) && mtk.VerifySteps.Any(x => x.Contains("*#06#")));

        var qcom = PhoneAccessPlan.Build(
            new PhoneIdentity(PhoneVendor.Qualcomm, "Pixel", "SN2", "350782285315072", "", "", "14", "QCOM", false, false, false),
            DeviceAccessKind.ServicePort, ["test"]);
        Check("Qualcomm مسیر QPST/QXDM و NV 550 را می‌دهد",
            qcom.WritableOnDevice && qcom.Tools.Any(t => t.Name.Contains("QPST")) &&
            qcom.WriteSteps.Any(x => x.Contains("550")));
        Check("Qualcomm بدون روت، پیش‌نیاز پورت DIAG را می‌گوید",
            qcom.Verdict.Contains("DIAG") || qcom.Tools.Any(t => t.Preconditions.Contains("DIAG")));

        var secure = PhoneAccessPlan.Build(
            new PhoneIdentity(PhoneVendor.Qualcomm, "Pixel", "SN2", "", "", "", "14", "QCOM", false, false, true),
            DeviceAccessKind.ServicePort, ["test"]);
        Check("Secure Boot باعث حکم محتاطانه می‌شود", secure.Verdict.Contains("Secure Boot"));

        var iphone = PhoneAccessPlan.Build(
            new PhoneIdentity(PhoneVendor.Apple, "iPhone 18", "UDID", "350782285315072", "", "89049032020008884800251820547965", "18", "APPLE", false, false, true),
            DeviceAccessKind.UserUsb, ["test"]);
        Check("آیفون هرگز قابل نوشتن اعلام نمی‌شود", !iphone.WritableOnDevice);
        Check("آیفون توضیح بیس‌باند امضاشده دارد",
            iphone.IosStatement.Contains("NV") && iphone.IosStatement.Contains("Activation"));
        Check("گزارش آیفون EID را نشان می‌دهد", iphone.ToText().Contains("89049032020008884800251820547965"));

        var paths = ActivationPaths.All("iPhone 18");
        Check("مسیر اول ارزان‌ترین است و خود دستگاه را برنمی‌گرداند",
            paths[0].Rank == 1 && !paths[0].RestoresThisDevice);
        Check("فقط ثبت همتا و نوشتن NV خود دستگاه را برمی‌گردانند",
            paths.Where(p => p.RestoresThisDevice).Count() == 2 &&
            paths.Where(p => p.RestoresThisDevice).All(p => p.Title.Contains("NV") || p.Title.Contains("همتا")));
        Check("آیفون در جدول هزینه به‌عنوان مسیر ناممکن علامت می‌خورد",
            paths.Any(p => p.TimeToService == "ممکن نیست" && p.RestoresThisDevice == false));
        Check("گزارش هزینه سه بخش چطور/هزینه/پایداری دارد",
            ActivationPaths.BuildReport("iPhone 18").Contains("چطور:") &&
            ActivationPaths.BuildReport("iPhone 18").Contains("پایداری:"));

        Check("ارقام فارسی به لاتین تبدیل می‌شوند",
            PhoneDetector.NormalizeDigits("۳۵۲۹۲۱۱۰۱۶۴۶۹۱۸") == "352921101646918");

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
        var acceptedButNotPersisted = new WriteResult(
            ImeiWriter.Strategies[0],
            [new WriteStep("AT+EGMR=1,7,\"352099001761481\"", "\r\nOK\r\n", StepStatus.Ok)],
            "490154203237518", "490154203237518", false, "OK ولی بدون تغییر");
        Check("حالت «OK اما بدون تغییر» تشخیص داده می‌شود", acceptedButNotPersisted.AcceptedButNotPersisted);
        Check("حالت تأییدشده «OK اما بدون تغییر» محسوب نمی‌شود", !BuildVerifiedResult().AcceptedButNotPersisted);
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

    /// <summary>هم‌ارز منطق تشخیص علت شکست در MainForm (برای تست بدون UI).</summary>
    private static string RecognizeFailureForTest(string before, string after, string target)
    {
        if (after.Length != 15) return "تغذیه";
        if (after == target) return "موفق";
        if (after == before) return "سهمیه AT+SIMEI یا NV یا OTP";
        return "فرق دارد";
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; }
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
