using System.Security.Cryptography;

namespace SIM800L_Flasher;

internal static class MobileSelfTests
{
    internal static int Run(Action<string>? log)
    {
        var failures = 0;
        void Check(string test, bool success)
        {
            if (!success) failures++;
            log?.Invoke($"{(success ? "PASS" : "FAIL")}  گوشی: {test}");
        }

        var realNodes = MobileUsbInventory.Scan();
        Check($"Configuration Manager در ویندوز اجرا شد ({realNodes.Count} گره اپل یا سامسونگ)",
            realNodes.All(node => node.HardwareId.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)));

        const string adbListing = """
            List of devices attached
            FIRST123 device usb:1-2 model:SM_S918B transport_id:3
            SECOND456 unauthorized transport_id:4
            THIRD789 offline usb:2-1 transport_id:6
            192.0.2.4:5555 device transport_id:9
            adb-SERIAL-123._adb-tls-connect._tcp device transport_id:12
            emulator-5554 device transport_id:14
            """;
        var samsungNode = new MobileUsbNode(@"USB\VID_04E8&PID_ABCD\SECOND456", "Samsung", "VID_04E8&PID_ABCD", 0);
        IReadOnlyList<string> tools = ["adb.exe", "idevice_id.exe", "ideviceinfo.exe"];
        var folder = Directory.CreateTempSubdirectory("XAnten-mobile-test-");
        try
        {
            foreach (var tool in tools) File.WriteAllText(Path.Combine(folder.FullName, tool), "test path");
            CheckProviders(folder.FullName, adbListing, samsungNode, Check);
        }
        finally
        {
            foreach (var tool in tools) File.Delete(Path.Combine(folder.FullName, tool));
            folder.Delete();
        }

        CheckIdentityProbes(Check);

        var encrypted = MobileSnapshot.Protect("XAnten sample "u8);
        MobileSnapshot.VerifyEnvelope(encrypted);
        Check("اسنپ‌شات با DPAPI رمزگشایی شد و SHA-256 تطابق داشت", encrypted.AsSpan().IndexOf("XAnten sample "u8) < 0);
        encrypted[^1] ^= 0x01;
        try
        {
            MobileSnapshot.VerifyEnvelope(encrypted);
            Check("اسنپ‌شات دستکاری‌شده رد می‌شود", false);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException)
        {
            Check("اسنپ‌شات دستکاری‌شده رد می‌شود", true);
        }

        return failures;
    }

    private static void CheckProviders(string folder, string adbListing, MobileUsbNode samsungNode, Action<string, bool> check)
    {
        const string appleId = "00008110-001A2B3C44556677";
        const string imei = "490154203237518";
        var calls = new List<(string Tool, string[] Args)>();
        var response = new MobileToolResult(0, "", "");
        var diagnostics = new MobileDiagnostics(folder, (path, args, token) =>
        {
            token.ThrowIfCancellationRequested();
            var tool = Path.GetFileName(path);
            calls.Add((tool, args.ToArray()));
            return Task.FromResult((tool, args[0]) switch
            {
                ("adb.exe", "devices") => new MobileToolResult(0, adbListing, ""),
                ("idevice_id.exe", "-l") => new MobileToolResult(0, $"{appleId}\nnot-a-udid\n{appleId}", ""),
                _ => response
            });
        });

        var android = diagnostics.DiscoverAsync(MobileDiagnostics.Android, [samsungNode], default).GetAwaiter().GetResult();
        check("چند دستگاه USB با حذف اتصال شبکه و شبیه‌ساز", android.Devices.Count == 3);
        var samsungMtp = samsungNode with { HardwareId = samsungNode.HardwareId + "&MS_COMP_MTP&SAMSUNG_Android" };
        var samsungModem = samsungNode with { HardwareId = samsungNode.HardwareId + "&Modem" };
        IReadOnlyList<MobileUsbNode> samsungUsb = [samsungNode, samsungMtp, samsungModem];
        var windowsOnly = MobileDiagnostics.CombineWithWindowsUsb([], samsungUsb);
        check("سامسونگ بدون ADB از طریق USB ویندوز یک بار در فهرست ظاهر می‌شود", windowsOnly is [{ UsbNode: not null }]);
        if (windowsOnly is [var fromWindows])
        {
            var beforeWindowsRead = calls.Count;
            var reading = diagnostics.ReadAsync(fromWindows, default).GetAwaiter().GetResult();
            check("شناسایی ویندوز جزئیات USB را بدون اجرای ADB گزارش می‌کند", calls.Count == beforeWindowsRead &&
                reading.Fields["شناسه سخت‌افزار USB"] == samsungNode.HardwareId && reading.Identifiers.Count == 0);
        }
        check("با شناسایی همان سامسونگ در ADB ردیف ویندوز تکرار نمی‌شود",
            MobileDiagnostics.CombineWithWindowsUsb(android.Devices, samsungUsb).Length == android.Devices.Count);
        if (android.Devices is [var authorized, var unauthorized, var offline])
        {
            var before = calls.Count;
            var denied = diagnostics.ReadAsync(unauthorized, default).GetAwaiter().GetResult();
            var unavailable = diagnostics.ReadAsync(offline, default).GetAwaiter().GetResult();
            check("دستگاه بدون مجوز یا آفلاین فرمان دریافت نمی‌کند", calls.Count == before &&
                denied.Capabilities["مدل"] == MobileCapability.AuthorizationRequired && unavailable.Fields.Count == 0);

            response = new(0, "[ro.product.model]: [SM-S918B]\n[gsm.version.baseband]: [unknown]\n[gsm.sim.state]: [READY,ABSENT]", "");
            var androidRead = diagnostics.ReadAsync(authorized, default).GetAwaiter().GetResult();
            check("ADB فقط دستگاه انتخابی را می‌خواند و بیس‌بند گزارش‌نشده را نامشخص نشان می‌دهد",
                calls[^1].Args.SequenceEqual(["-t", "3", "shell", "getprop"]) &&
                androidRead.Fields["مدل"] == "SM-S918B" && androidRead.Capabilities["بیس‌بند"] == MobileCapability.Unknown &&
                androidRead.Capabilities["IMEI 1"] == MobileCapability.Restricted);

            var referenceOk = MobileRepairPage.TryParseEnteredImei(imei, out var reference, out _);
            var observedOk = MobileRepairPage.TryParseEnteredImei("۴۹۰۱۵۴۲۰۳۲۳۷۵۱۸", out var observed, out _);
            var otherOk = MobileRepairPage.TryParseEnteredImei("352099001761481", out var other, out _);
            var comparison = referenceOk && observedOk ? MobileRepairPage.CompareEnteredImei(reference!, observed!) : "";
            check("مقایسه دستی ارقام فارسی را می‌پذیرد و تطابق و اختلاف را جداگانه گزارش می‌کند",
                comparison.Contains("یکسان است") && otherOk &&
                MobileRepairPage.CompareEnteredImei(reference!, other!).Contains("اختلاف دارد") &&
                !MobileRepairPage.TryParseEnteredImei("490154203237519", out _, out _));
            var manualReport = MobileRepairPage.CreateJsonReport(samsungUsb, android.Devices, authorized, androidRead, DateTimeOffset.UtcNow, comparison);
            check("گزارش نتیجه مقایسه دستی را بدون شماره واردشده ثبت می‌کند", manualReport.Contains("IdentityComparison") &&
                !manualReport.Contains(imei) && !manualReport.Contains("352099001761481") && !manualReport.Contains("۴۹۰۱۵۴۲۰۳۲۳۷۵۱۸"));
        }

        var apple = diagnostics.DiscoverAsync(MobileDiagnostics.Apple, [], default).GetAwaiter().GetResult();
        check("UDID معتبر USB آیفون بدون تکرار شناسایی شد", apple.Devices.Count == 1);
        if (apple.Devices is not [var target]) return;

        response = new(-1, "", "Could not connect to lockdownd: Pairing dialog response pending");
        var trust = diagnostics.ReadAsync(target, default).GetAwaiter().GetResult();
        check("آیفون بدون Trust وضعیت مجوز می‌گیرد", trust.Capabilities["نسخه iOS"] == MobileCapability.AuthorizationRequired);

        response = new(0, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
                <key>ProductVersion</key><string>18.0</string>
                <key>InternationalMobileEquipmentIdentity</key><string>{imei}</string>
                <key>DeviceName</key><string>Personal phone</string>
            </dict></plist>
            """, "");
        var read = diagnostics.ReadAsync(target, default).GetAwaiter().GetResult();
        check("آیفون فیلدهای مجاز را می‌خواند و بقیه نامشخص می‌مانند",
            calls[^1].Args.SequenceEqual(["-u", appleId, "-x"]) && read.Fields.Count == 1 &&
            read.Identifiers["IMEI 1"] == imei && MobileDiagnostics.Mask(imei) != imei &&
            read.Capabilities["IMEI 2"] == MobileCapability.Unknown);

        var report = MobileRepairPage.CreateJsonReport([samsungNode], [target], target, read, DateTimeOffset.UtcNow, null);
        check("گزارش عمومی مدل و قابلیت را نگه می‌دارد و شناسه‌ها را حذف می‌کند",
            report.Contains("18.0") && report.Contains("Unsupported") &&
            !report.Contains(imei) && !report.Contains(appleId) && !report.Contains(samsungNode.InstanceTail));

        response = new(0, "<plist>", "");
        var malformed = diagnostics.ReadAsync(target, default).GetAwaiter().GetResult();
        check("پاسخ XML ناقص موفق گزارش نمی‌شود", malformed.Fields.Count == 0 && malformed.Status.Contains("معتبر"));

        var slowTool = new MobileDiagnostics(folder, (_, _, _) => throw new TimeoutException());
        var late = slowTool.ReadAsync(target, default).GetAwaiter().GetResult();
        check("پایان مهلت پاسخ به وضعیت نامشخص منجر می‌شود", late.Capabilities["نسخه iOS"] == MobileCapability.Unknown &&
            late.Status.Contains("زمان پاسخ"));

        try
        {
            diagnostics.ReadAsync(target, new CancellationToken(true)).GetAwaiter().GetResult();
            check("لغو به اجراکننده فرمان می‌رسد", false);
        }
        catch (OperationCanceledException) { check("لغو به اجراکننده فرمان می‌رسد", true); }
    }

    private static void CheckIdentityProbes(Action<string, bool> check)
    {
        // --- استخراج IMEI از پاسخ مودم ---
        check("IMEI خالص از پاسخ AT+CGSN خوانده می‌شود",
            ModemIdentityProbe.ExtractImei("\r\n352921101646918\r\nOK\r\n") == "352921101646918");
        check("پاسخ +CGSN: IMEI هم پذیرفته می‌شود",
            ModemIdentityProbe.ExtractImei("+CGSN: 490154203237518\r\nOK") == "490154203237518");
        check("پاسخ خالی یا ERROR به IMEI تبدیل نمی‌شود",
            ModemIdentityProbe.ExtractImei("ERROR") == "" &&
            ModemIdentityProbe.ExtractImei("000000000000000") == "");

        // --- سرویس binder اندروید ---
        // دامپ واقعی: هر کلمه ۴ بایت، رشته UTF-16 big-endian (00 34 = '4').
        const string parcel = "Result: Parcel(00000000 0000000f 00340039 00300031 00350034 00320030 " +
                              "00330032 00330037 00350031 00380000 00000000)";
        var decoded = AndroidIdentityProbe.DecodeHexDump(parcel);
        check("سرویس binder عدد ۱۵ رقمی معتبر را برمی‌گرداند", decoded == "490154203237518");
        check("پاسخ هگز نامعتبر یا Luhn غلط پذیرفته نمی‌شود",
            AndroidIdentityProbe.DecodeHexDump("Result: Parcel(deadbeef)") == "" &&
            AndroidIdentityProbe.DecodeHexDump("Result: Parcel(00310032 00330034 00350036 00370038 00390031 00310032 00330034)") == "");

        // --- آیفون: خواندن کلید به کلید ---
        var appleCalls = new List<string>();
        var appleRunner = new Func<string, IReadOnlyList<string>, CancellationToken, Task<MobileToolResult>>((path, args, token) =>
        {
            appleCalls.Add(string.Join(' ', args));
            var key = args[^1];
            var value = key switch
            {
                "InternationalMobileEquipmentIdentity" => "352921101646918",
                "InternationalMobileEquipmentIdentity2" => "352921101658061",
                "EmbeddedIdentityDocument" => "89049032020008884800251820547965",
                "ProductType" => "iPhone17,1",
                "ProductVersion" => "18.0",
                _ => ""
            };
            return Task.FromResult(new MobileToolResult(0, value + "\n", ""));
        });
        var apple = AppleIdentityProbe.ReadAsync("ideviceinfo.exe", appleRunner, "00008110-001A2B3C44556677", default)
            .GetAwaiter().GetResult();
        check("IMEI و IMEI2 و EID آیفون از lockdownd خوانده می‌شوند",
            apple.Imei1 == "352921101646918" && apple.Imei2 == "352921101658061" &&
            apple.Eid == "89049032020008884800251820547965");
        check("برای هر کلید یک فرمان ideviceinfo جدا اجرا می‌شود",
            appleCalls.Count == AppleIdentityProbe.IdentityKeys.Length && appleCalls.All(call => call.Contains("-k")));

        var noTrust = AppleIdentityProbe.ReadAsync("ideviceinfo.exe",
            (path, args, _) => Task.FromResult(args[0] == "-l"
                ? new MobileToolResult(0, "00008110-001A2B3C44556677\n", "")
                : new MobileToolResult(1, "", "Could not connect to lockdownd: Pairing dialog response pending")),
            null, default).GetAwaiter().GetResult();
        check("آیفون بدون Trust با پیام روشن رد می‌شود", !noTrust.FoundAny && noTrust.Verdict.Contains("Trust"));

        // --- نوشتن روی گوشی: اعتبارسنجی و مسیرهای بسته ---
        check("IMEI هدف نامعتبر رد می‌شود",
            PhoneImeiWriter.ValidateTarget("352921101646919", "352921101646918").Contains("معتبر نیست"));
        check("IMEI برابر مقدار فعلی، نوشتن لازم ندارد",
            PhoneImeiWriter.ValidateTarget("352921101646918", "352921101646918").Contains("یکسان"));
        check("هدف معتبر بدون پیام خطا عبور می‌کند",
            PhoneImeiWriter.ValidateTarget("352921101646918", "867372053415555") == "");
        check("فرمان‌های Qualcomm شامل NV 550 و بازگردانی است",
            PhoneImeiWriter.QualcommNv550Commands("352921101646918").Any(c => c.Contains("550")) &&
            PhoneImeiWriter.QualcommNv550Commands("352921101646918").Any(c => c.Contains("QNVRESTART")));
        var blocked = PhoneImeiWriter.NotPossible(PhoneVendor.Apple, "352921101646918", "350782285315072",
            PhoneImeiWriter.BuildWriteGuide(PhoneVendor.Apple, "352921101646918"), "ثبت در همتا");
        check("iOS نوشتن را ممکن اعلام نمی‌کند و دلیل می‌دهد",
            !blocked.Success && blocked.Outcome == PhoneWriteOutcome.Blocked &&
            blocked.Verdict.Contains("NV") && blocked.ToText().Contains("قدم بعدی"));
        var missing = PhoneImeiWriter.MissingTool(PhoneVendor.MediaTek, "352921101646918", "", "Maui Meta", "tools");
        check("نبود ابزار به‌صورت ToolMissing گزارش می‌شود و موفقیت جعلی نمی‌سازد",
            missing.Outcome == PhoneWriteOutcome.ToolMissing && !missing.Success);

        // --- خواننده جهانی: ترتیب مسیرها و تجمیع نتیجه ---
        var attempts = new UniversalIdentity(
        [
            new IdentityAttempt("پورت مودم (AT)", "COM7 → OK", true, "352921101646918", "", ""),
            new IdentityAttempt("ADB / سرویس binder", "مسدود", false, "", "", "")
        ], "352921101646918", "", "", "", "SM-S918B", "R58N", PhoneVendor.Qualcomm,
            "IMEI از مسیر «پورت مودم (AT)» خوانده شد.");
        check("خواننده جهانی مسیر موفق را برمی‌گرداند", attempts.Found && attempts.BestPath.Contains("مودم"));
        check("گزارش خواننده جهانی همه مسیرها را با برچسب نشان می‌دهد",
            attempts.ToText().Contains("[موفق]") && attempts.ToText().Contains("[ناموفق]"));
        var none = attempts with { Imei1 = "", Attempts = [] };
        check("بدون شناسه، Found منفی است", !none.Found);
    }
}
