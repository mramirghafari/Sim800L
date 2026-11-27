using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIM800L_Flasher;

internal sealed class MobileRepairPage : TabPage
{
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ComboBox _devices = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, RightToLeft = RightToLeft.No };
    private readonly TextBox _report = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White, AccessibleName = "گزارش عیب‌یابی گوشی"
    };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(3, 4, 3, 8), AccessibleName = "وضعیت عملیات" };
    private readonly TextBox _expected = MakeImeiInput("IMEI مرجع");
    private readonly TextBox _observed = MakeImeiInput("IMEI روی صفحه گوشی");
    private readonly Button _scan = MakeButton("اسکن USB");
    private readonly Button _read = MakeButton("خواندن از گوشی");
    private readonly Button _cancel = MakeButton("توقف");
    private readonly Button _tools = MakeButton("پوشه ابزارها");
    private readonly Button _export = MakeButton("ذخیره گزارش JSON");
    private readonly Button _compare = MakeButton("مقایسه IMEI اصلی");
    private readonly Button _saveSnapshot = MakeButton("ذخیره اسنپ‌شات رمزدار");
    private readonly Button _verifySnapshot = MakeButton("بررسی اسنپ‌شات");
    private readonly System.Windows.Forms.Timer _monitor = new() { Interval = 3500 };
    private IReadOnlyList<MobileUsbNode>? _usb;
    private MobileReading? _reading;
    private DateTimeOffset? _readAt;
    private CancellationTokenSource? _operation;
    private string _folder = "";
    private string _discoveryStatus = "برای بررسی اتصال، اسکن USB را بزنید.";
    private string? _comparison;
    private bool _checkingUsb;

    public MobileRepairPage() : base("تعمیرات گوشی")
    {
        BackColor = Color.FromArgb(244, 247, 250);
        Padding = new Padding(14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var caption = new Label { Text = "عیب‌یابی USB گوشی", AutoSize = true, Font = AppFonts.Ui(14, FontStyle.Bold),
            ForeColor = Color.FromArgb(21, 32, 51) };
        layout.Controls.Add(caption, 0, 0);
        layout.Controls.Add(Buttons(_scan, _tools, _read, _cancel, _export, _saveSnapshot, _verifySnapshot), 0, 1);
        _devices.AccessibleName = "گوشی متصل";
        layout.Controls.Add(_devices, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(_report, 0, 4);
        layout.Controls.Add(Buttons(_expected, _observed, _compare,
            SupportLink("بازیابی رسمی Samsung", "https://update.kies.samsung.com/SmartSwitchPC_recovery_guide/firmware/emergencyrecovery/"),
            SupportLink("بازیابی رسمی Apple", "https://support.apple.com/en-us/118106")), 0, 5);

        _scan.Click += async (_, _) => await ScanAsync();
        _read.Click += async (_, _) => await ReadAsync();
        _tools.Click += async (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "پوشه اجرایی ابزارهای رسمی ADB یا libimobiledevice", SelectedPath = _folder };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            _folder = picker.SelectedPath;
            await ScanAsync();
        };
        _cancel.Click += (_, _) => _operation?.Cancel();
        _export.Click += (_, _) => Export();
        _compare.Click += (_, _) => Compare();
        _saveSnapshot.Click += (_, _) => SaveSnapshot();
        _verifySnapshot.Click += (_, _) => VerifySnapshot();
        _devices.SelectedIndexChanged += (_, _) =>
        {
            ClearReading();
            ShowReport();
        };
        _monitor.Tick += async (_, _) => await CheckUsbAsync();
        VisibleChanged += (_, _) => _monitor.Enabled = Visible;
        ShowReport();
    }

    private MobileDiagnostics CreateDiagnostics() => new(_folder, MobileDiagnostics.RunToolAsync);

    private async Task CheckUsbAsync()
    {
        if (_checkingUsb || _operation is not null || IsDisposed || !Visible) return;
        _checkingUsb = true;
        try
        {
            var nodes = await Task.Run(MobileUsbInventory.Scan);
            if (_usb is null || !_usb.SequenceEqual(nodes)) await ScanAsync();
        }
        catch (InvalidOperationException)
        {
            if (!IsDisposed && _operation is null) _status.Text = "وضعیت USB از ویندوز دریافت نشد. اسکن دستی را امتحان کنید.";
        }
        finally { _checkingUsb = false; }
    }

    private Task ScanAsync() => RunAsync("در حال اسکن USB…", async token =>
    {
        var nodes = await Task.Run(MobileUsbInventory.Scan, token);
        token.ThrowIfCancellationRequested();
        var selection = _devices.SelectedItem as MobileTarget;
        ClearReading();
        _devices.Items.Clear();
        _usb = nodes;
        try
        {
            var diagnostics = CreateDiagnostics();
            var scans = await Task.WhenAll(diagnostics.DiscoverAsync(MobileDiagnostics.Android, nodes, token),
                diagnostics.DiscoverAsync(MobileDiagnostics.Apple, nodes, token));
            token.ThrowIfCancellationRequested();
            var devices = MobileDiagnostics.CombineWithWindowsUsb(scans.SelectMany(scan => scan.Devices), nodes);
            _devices.Items.AddRange(devices);
            var index = Array.FindIndex(devices, item => item.Protocol == selection?.Protocol && item.Id == selection.Id);
            if (index < 0 && selection?.UsbNode is { } previousNode)
                index = Array.FindIndex(devices, item => MobileDiagnostics.MatchesWindowsUsb(item, previousNode));
            if (devices.Length > 0) _devices.SelectedIndex = Math.Max(0, index);
            var summaries = nodes.Any(node => node.Vendor == "Samsung") && !nodes.Any(node => node.Vendor == "Apple") &&
                scans[1].Devices.Count == 0 ? scans.Take(1) : scans;
            _discoveryStatus = string.Join("   |   ", summaries.Select(scan => scan.Status));
        }
        catch (OperationCanceledException)
        {
            _discoveryStatus = "اسکن متوقف شد. برای تلاش مجدد «اسکن USB» را بزنید.";
            throw;
        }
    });

    private Task ReadAsync()
    {
        if (_devices.SelectedItem is not MobileTarget target) return Task.CompletedTask;
        ClearReading();
        return RunAsync("در حال خواندن اطلاعات گوشی…", async token =>
        {
            _reading = await CreateDiagnostics().ReadAsync(target, token);
            _readAt = DateTimeOffset.UtcNow;
        });
    }

    private async Task RunAsync(string message, Func<CancellationToken, Task> work)
    {
        if (_operation is not null || IsDisposed || !Visible) return;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _status.Text = message;
        UpdateButtons();
        string? error = null;
        try { await work(operation.Token); }
        catch (OperationCanceledException) { error = "عملیات متوقف شد."; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            error = "اسکن USB کامل نشد. اتصال و وضعیت درایور را دوباره بررسی کنید.";
        }
        finally
        {
            _operation = null;
            if (!IsDisposed)
            {
                ShowReport();
                if (error is not null) _status.Text = error;
            }
        }
    }

    private void ClearReading()
    {
        _reading = null;
        _readAt = null;
        _comparison = null;
        _expected.Clear();
        _observed.Clear();
    }

    private void ShowReport()
    {
        var lines = new List<string> { _discoveryStatus, "", "گره‌های USB سامسونگ و اپل در ویندوز" };
        if (_usb is { Count: 0 }) lines.Add("ویندوز گره USB از این دو سازنده گزارش نکرد.");
        if (_usb is { } usb)
        {
            foreach (var node in usb)
                lines.Add($"{node.Vendor}  |  {node.HardwareId}  |  {DriverStatus(node)}");
        }

        if (_reading is { } reading)
        {
            lines.AddRange(["", reading.Status]);
            if (_devices.SelectedItem is MobileTarget { UsbNode: null }) lines.Add($"زمان پاسخ: {reading.ElapsedMs} ms");
            lines.AddRange(reading.Fields.Select(field => $"{field.Key}   {field.Value}"));
            lines.AddRange(reading.Identifiers.Select(id => $"{id.Key}   {MobileDiagnostics.Mask(id.Value)}{ImeiValidation(id.Key, id.Value)}"));
            if (_comparison is not null) lines.Add(_comparison);
            if (CanEnterImei(reading))
                lines.Add("برای مقایسه دستی، IMEI را از Settings ← About phone یا با ‎*#06#‎ روی گوشی ببینید و کنار شماره مرجع وارد کنید. برای گوشی دو سیم‌کارته، شماره همان جایگاه را مقایسه کنید.");
            lines.AddRange(["", "قابلیت‌های دستگاه"]);
            lines.AddRange(reading.Capabilities.Select(item => $"{item.Key}   {CapabilityLabel(item.Value)}"));
        }
        else if (_devices.SelectedItem is MobileTarget { UsbNode: { } node })
        {
            lines.AddRange(["", MobileDiagnostics.WindowsUsbHint(node)]);
        }
        else if (_devices.SelectedItem is MobileTarget { Protocol: MobileDiagnostics.Android, State: "unauthorized" })
        {
            lines.AddRange(["", "گوشی در ADB دیده شد. قفل صفحه را باز کنید، پیام Allow USB debugging را تأیید کنید و دوباره اسکن کنید."]);
        }
        else if (_devices.SelectedItem is MobileTarget)
        {
            lines.AddRange(["", "برای بررسی مجوز اتصال و اطلاعات دستگاه، «خواندن از گوشی» را بزنید."]);
        }
        else if (_usb is not null)
        {
            lines.AddRange(["", "در گوشی سامسونگ، USB debugging را فعال و درخواست مجوز رایانه را تأیید کنید.",
                "در آیفون، قفل صفحه را باز کنید و در صورت نمایش درخواست، Trust را انتخاب کنید."]);
        }

        var output = string.Join(Environment.NewLine, lines);
        if (_report.Text != output) _report.Text = output;
        _status.Text = _reading?.Status ?? _discoveryStatus;
        UpdateButtons();
    }

    private static string DriverStatus(MobileUsbNode node) => node.DriverProblem switch
    {
        0 => "بدون کد خطای PnP",
        28 => "درایور نصب نیست (کد ۲۸)",
        null => "وضعیت درایور نامشخص",
        var code => $"خطای PnP (کد {code})"
    };

    private static string CapabilityLabel(MobileCapability capability) => capability switch
    {
        MobileCapability.Supported => "قابل دسترس",
        MobileCapability.Unsupported => "پشتیبانی نمی‌شود",
        MobileCapability.Restricted => "محدود در این مسیر",
        MobileCapability.AuthorizationRequired => "نیازمند مجوز روی گوشی",
        MobileCapability.DriverMissing => "درایور نصب نیست",
        MobileCapability.NotTested => "آزموده نشده",
        _ => "نامشخص"
    };

    private static string ImeiValidation(string label, string value) => label.StartsWith("IMEI", StringComparison.Ordinal)
        ? ImeiNumber.TryParse(value, out _, out _) ? "   (Luhn معتبر)" : "   (قالب یا Luhn نامعتبر)"
        : "";

    private void Compare()
    {
        if (_reading is not { } reading) return;
        if (!TryParseEnteredImei(_expected.Text, out var expected, out var error))
        {
            _status.Text = $"IMEI مرجع — {error}";
            _expected.Focus();
            return;
        }

        if (CanEnterImei(reading))
        {
            if (!TryParseEnteredImei(_observed.Text, out var observed, out error))
            {
                _status.Text = $"IMEI روی صفحه گوشی — {error}";
                _observed.Focus();
                return;
            }
            _comparison = CompareEnteredImei(expected!, observed!);
        }
        else
        {
            var candidates = reading.Identifiers.Where(item => item.Key.StartsWith("IMEI", StringComparison.Ordinal))
                .Where(item => ImeiNumber.TryParse(item.Value, out _, out _)).ToArray();
            var match = candidates.FirstOrDefault(item => item.Value == expected!.Value);
            _comparison = candidates.Length == 0 ? "IMEI معتبر از گوشی دریافت نشد. مقایسه ممکن نیست."
                : match.Key is null ? "IMEI مرجع با شناسه‌های قابل خواندن گوشی تطابق ندارد."
                : $"IMEI مرجع با {match.Key} گوشی تطابق دارد.";
        }

        _expected.Clear();
        _observed.Clear();
        ShowReport();
        _status.Text = _comparison;
    }

    private static bool CanEnterImei(MobileReading? reading) =>
        reading?.Capabilities.TryGetValue("IMEI 1", out var capability) == true && capability == MobileCapability.Restricted;

    internal static bool TryParseEnteredImei(string input, out ImeiNumber? imei, out string error)
    {
        var normalized = string.Concat(input.Select(digit => digit switch
        {
            >= '۰' and <= '۹' => (char)('0' + digit - '۰'),
            >= '٠' and <= '٩' => (char)('0' + digit - '٠'),
            _ => digit
        }));
        return ImeiNumber.TryParse(normalized, out imei, out error);
    }

    internal static string CompareEnteredImei(ImeiNumber expected, ImeiNumber observed) => expected.Value == observed.Value
        ? "مقایسه دستی — شماره واردشده از صفحه گوشی با شماره مرجع یکسان است."
        : "مقایسه دستی — شماره واردشده از صفحه گوشی با شماره مرجع اختلاف دارد.";

    private void Export()
    {
        var report = CreateJsonReport(_usb ?? [], _devices.Items.Cast<MobileTarget>(),
            _devices.SelectedItem as MobileTarget, _reading, _readAt, _comparison);
        using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "XAnten-mobile-report.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, report);
            _status.Text = "گزارش JSON ذخیره شد.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "ذخیره گزارش انجام نشد. مسیر و دسترسی فایل را بررسی کنید.";
        }
    }

    internal static string CreateJsonReport(IReadOnlyList<MobileUsbNode> usb, IEnumerable<MobileTarget> targets,
        MobileTarget? target, MobileReading? reading, DateTimeOffset? readAt, string? comparison) => JsonSerializer.Serialize(new
    {
        SchemaVersion = 1, ExportedAtUtc = DateTimeOffset.UtcNow, ReadAtUtc = readAt,
        Usb = usb.Select(node => new { node.Vendor, node.HardwareId, node.DriverProblem }),
        Devices = targets.Select(device => new { device.Protocol, device.State }),
        SelectedProtocol = target?.Protocol,
        Fields = reading?.Fields, Capabilities = reading?.Capabilities, Status = reading?.Status,
        ElapsedMs = reading?.ElapsedMs, IdentityComparison = comparison
    }, ReportOptions);

    private void UpdateButtons()
    {
        var ready = _operation is null;
        _devices.Enabled = _scan.Enabled = _tools.Enabled = ready;
        _read.Enabled = ready && _devices.SelectedItem is MobileTarget;
        _cancel.Enabled = !ready;
        _export.Enabled = ready && _usb is not null;
        _saveSnapshot.Enabled = ready && _reading?.Fields.Count > 0 && _devices.SelectedItem is MobileTarget { UsbNode: null };
        _verifySnapshot.Enabled = ready;
        var manual = CanEnterImei(_reading);
        _observed.Visible = manual;
        _observed.Enabled = ready && manual;
        _compare.Enabled = _expected.Enabled = ready && (manual ||
            _reading?.Identifiers.Keys.Any(label => label.StartsWith("IMEI", StringComparison.Ordinal)) == true);
    }

    private void SaveSnapshot()
    {
        if (_devices.SelectedItem is not MobileTarget target || _reading is not { } reading || _readAt is not { } readAt) return;
        using var dialog = new SaveFileDialog { Filter = "XAnten snapshot (*.xasnap)|*.xasnap", FileName = "XAnten-mobile.xasnap" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            MobileSnapshot.Save(dialog.FileName, target, reading, readAt);
            _status.Text = "اسنپ‌شات با حساب کاربری فعلی ویندوز رمزگذاری و ذخیره شد.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _status.Text = "ذخیره اسنپ‌شات انجام نشد. مسیر و حساب ویندوز را بررسی کنید.";
        }
    }

    private void VerifySnapshot()
    {
        using var dialog = new OpenFileDialog { Filter = "XAnten snapshot (*.xasnap)|*.xasnap" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            MobileSnapshot.Verify(dialog.FileName);
            _status.Text = "رمزگشایی موفق بود و SHA-256 اسنپ‌شات تطابق دارد.";
        }
        catch (CryptographicException)
        {
            _status.Text = "رمزگشایی انجام نشد. فایل ممکن است آسیب دیده یا متعلق به حساب ویندوز دیگری باشد.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = "فایل قابل خواندن نیست یا ساختار و SHA-256 آن معتبر نیست.";
        }
    }

    private static Button MakeButton(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(0, 32), Padding = new Padding(6, 0, 6, 0), UseVisualStyleBackColor = true };

    private static TextBox MakeImeiInput(string label) => new()
    {
        Width = 165, MaxLength = 15, RightToLeft = RightToLeft.No, UseSystemPasswordChar = true,
        PlaceholderText = label, AccessibleName = label, TextAlign = HorizontalAlignment.Center
    };

    private Button SupportLink(string text, string url)
    {
        var button = MakeButton(text);
        button.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
            catch (Win32Exception) { _status.Text = "باز کردن مرورگر ممکن نبود."; }
        };
        return button;
    }

    private static Control Buttons(params Control[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        panel.Controls.AddRange(buttons);
        return panel;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Dispose();
            _operation?.Cancel();
        }
        base.Dispose(disposing);
    }
}
