using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Ports;

namespace SIM800L_Flasher;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(244, 247, 250);
    private static readonly Color Surface = Color.White;
    private static readonly Color Navy = Color.FromArgb(21, 32, 51);
    private static readonly Color Accent = Color.FromArgb(0, 155, 132);
    private static readonly Color Muted = Color.FromArgb(102, 112, 133);
    private static readonly Color Border = Color.FromArgb(223, 228, 235);

    private readonly Sim800Device _device = new();
    private readonly Dictionary<string, Label> _values = new();
    private readonly ComboBox _ports = new();
    private readonly ComboBox _baud = new();
    private readonly Label _connectionStatus = new();
    private readonly Button _connectButton = new();
    private readonly Button _scanButton = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _terminal = new();
    private readonly TextBox _command = new();
    private readonly Button _sendButton = new();
    private readonly Label _activity = new();
    private readonly TextBox _firmwarePath = new();
    private readonly TextBox _flasherPath = new();
    private readonly TextBox _flasherArguments = new();
    private readonly TextBox _firmwareDetails = new();
    private readonly CheckBox _flashConsent = new();
    private FirmwarePackage? _firmware;
    
     // متغیر برای ذخیره IMEI جدید
    private string _targetImei;

    public MainForm()
    {
        Text = "SIM800L Control Center";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        Font = new Font("Segoe UI", 9.5f);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildLayout();
        RefreshPorts();
        FormClosing += (_, _) => _device.Dispose();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Background,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(18, 7);
        _tabs.Font = new Font("Segoe UI Semibold", 10f);
        _tabs.TabPages.Add(BuildDashboard());
        _tabs.TabPages.Add(BuildTerminal());
        _tabs.TabPages.Add(BuildFlasher());
        _tabs.TabPages.Add(BuildGuide());
        root.Controls.Add(_tabs, 0, 1);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Navy, Padding = new Padding(22, 13, 22, 12) };
        var title = new Label
        {
            Text = "مرکز کنترل SIM800L",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18f),
            AutoSize = true,
            Location = new Point(920, 11)
        };
        var subtitle = new Label
        {
            Text = "پایش ماژول، سیم‌کارت و firmware",
            ForeColor = Color.FromArgb(177, 190, 210),
            AutoSize = true,
            Location = new Point(927, 51)
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);

        ConfigureCombo(_ports, 755, 28, 120);
        ConfigureCombo(_baud, 625, 28, 115);
        foreach (var baud in new[] { 9600, 19200, 38400, 57600, 115200 }) _baud.Items.Add(baud.ToString());
        _baud.SelectedItem = "9600";
        panel.Controls.Add(_ports);
        panel.Controls.Add(_baud);

        _scanButton.Text = "بازخوانی";
        StyleButton(_scanButton, 525, 26, 90, 36, Color.FromArgb(49, 62, 83), Color.White);
        _scanButton.Click += (_, _) => RefreshPorts();
        panel.Controls.Add(_scanButton);

        _connectButton.Text = "اتصال";
        StyleButton(_connectButton, 415, 26, 100, 36, Accent, Color.White);
        _connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        panel.Controls.Add(_connectButton);

        _connectionStatus.Text = "● قطع";
        _connectionStatus.ForeColor = Color.FromArgb(255, 143, 143);
        _connectionStatus.AutoSize = true;
        _connectionStatus.Location = new Point(330, 36);
        panel.Controls.Add(_connectionStatus);

        return panel;
    }

    private TabPage BuildDashboard()
    {
        var page = NewPage("داشبورد دستگاه");
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(18) };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        page.Controls.Add(main);

        var top = new Panel { Dock = DockStyle.Fill };
        var heading = new Label { Text = "اطلاعات زنده دستگاه و سیم‌کارت", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Navy, AutoSize = true, Location = new Point(850, 5) };
        _activity.Text = "برای شروع به پورت COM متصل شوید.";
        _activity.ForeColor = Muted;
        _activity.AutoSize = true;
        _activity.Location = new Point(25, 13);
        top.Controls.Add(heading);
        top.Controls.Add(_activity);
        main.Controls.Add(top, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(0, 6, 0, 6) };
        for (var i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        for (var i = 0; i < 3; i++) cards.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
        AddCard(cards, 0, 0, "مدل ماژول", "model", "ATI");
        AddCard(cards, 1, 0, "نسخه Firmware", "revision", "AT+GMR");
        AddCard(cards, 2, 0, "IMEI دستگاه", "imei", "فقط خواندنی");
        AddCard(cards, 0, 1, "IMSI سیم‌کارت", "imsi", "شناسه مشترک");
        AddCard(cards, 1, 1, "ICCID", "iccid", "سریال سیم‌کارت");
        AddCard(cards, 2, 1, "اپراتور", "operator", "AT+COPS?");
        AddCard(cards, 0, 2, "قدرت سیگنال", "signal", "AT+CSQ");
        AddCard(cards, 1, 2, "ثبت شبکه", "registration", "AT+CREG?");
        AddCard(cards, 2, 2, "وضعیت SIM", "sim", "AT+CPIN?");
        main.Controls.Add(cards, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var refresh = NewActionButton("دریافت همه اطلاعات", Accent, 190);
        refresh.Click += async (_, _) => await LoadSnapshotAsync();
        var test = NewActionButton("تست ارتباط AT", Navy, 150);
        test.Click += async (_, _) => await RunCommandAsync("AT", true);
        var identify = NewActionButton("شماره سیم‌کارت", Color.FromArgb(70, 92, 125), 150);
        identify.Click += async (_, _) => await RunCommandAsync("AT+CNUM", true);
        actions.Controls.AddRange([refresh, test, identify]);
        main.Controls.Add(actions, 0, 2);
        return page;
    }

    private TabPage BuildTerminal()
    {
        var page = NewPage("ترمینال AT");
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        page.Controls.Add(panel);

        _terminal.Dock = DockStyle.Fill;
        _terminal.Multiline = true;
        _terminal.ReadOnly = true;
        _terminal.ScrollBars = ScrollBars.Both;
        _terminal.WordWrap = false;
        _terminal.RightToLeft = RightToLeft.No;
        _terminal.Font = new Font("Cascadia Mono", 10f);
        _terminal.BackColor = Color.FromArgb(17, 23, 34);
        _terminal.ForeColor = Color.FromArgb(174, 230, 202);
        _terminal.BorderStyle = BorderStyle.FixedSingle;

        var composer = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(0, 10, 0, 0) };
        _command.Dock = DockStyle.Fill;
        _command.RightToLeft = RightToLeft.No;
        _command.Font = new Font("Cascadia Mono", 10.5f);
        _command.Text = "AT";
        _command.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RunCommandAsync(_command.Text, true);
        };
        _sendButton.Text = "ارسال";
        _sendButton.Dock = DockStyle.Right;
        _sendButton.Width = 105;
        _sendButton.BackColor = Accent;
        _sendButton.ForeColor = Color.White;
        _sendButton.FlatStyle = FlatStyle.Flat;
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.Click += async (_, _) => await RunCommandAsync(_command.Text, true);
        composer.Controls.Add(_command);
        composer.Controls.Add(_sendButton);
        panel.Controls.Add(_terminal);
        panel.Controls.Add(composer);
        return page;
    }

    private TabPage BuildFlasher()
    {
        var page = NewPage("فلشر Firmware");
        var body = new TableLayoutPanel { Dock = DockStyle.Top, Height = 440, ColumnCount = 1, RowCount = 6, Padding = new Padding(24) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        page.Controls.Add(body);

        var warning = new Label
        {
            Text = "فلش مستقیم و ناشناخته می‌تواند ماژول را بریک کند. این بخش فقط فایل را با SHA-256 بررسی و ابزار رسمی سازنده را اجرا می‌کند.",
            ForeColor = Color.FromArgb(160, 72, 45),
            BackColor = Color.FromArgb(255, 244, 232),
            Padding = new Padding(12, 9, 12, 8),
            Dock = DockStyle.Fill
        };
        body.Controls.Add(warning);
        body.Controls.Add(BuildPickerRow("فایل Firmware", _firmwarePath, "انتخاب فایل", ChooseFirmware));
        body.Controls.Add(BuildPickerRow("ابزار رسمی فلش", _flasherPath, "انتخاب EXE", ChooseFlasher));

        var args = new Panel { Dock = DockStyle.Fill, Height = 58 };
        args.Controls.Add(new Label { Text = "آرگومان‌های ابزار", AutoSize = true, Location = new Point(950, 17), ForeColor = Navy });
        _flasherArguments.Text = "--port {PORT} --baud {BAUD} --firmware {FIRMWARE}";
        _flasherArguments.RightToLeft = RightToLeft.No;
        _flasherArguments.Location = new Point(160, 12);
        _flasherArguments.Width = 760;
        args.Controls.Add(_flasherArguments);
        body.Controls.Add(args);

        _firmwareDetails.Dock = DockStyle.Fill;
        _firmwareDetails.Multiline = true;
        _firmwareDetails.ReadOnly = true;
        _firmwareDetails.RightToLeft = RightToLeft.No;
        _firmwareDetails.BackColor = Color.White;
        _firmwareDetails.Text = "پس از انتخاب فایل، اندازه و SHA-256 اینجا نمایش داده می‌شود.";
        body.Controls.Add(_firmwareDetails);

        var final = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        _flashConsent.Text = "تطابق مدل برد، revision و منبع فایل را بررسی کرده‌ام.";
        _flashConsent.AutoSize = true;
        _flashConsent.Padding = new Padding(8);
        var launch = NewActionButton("اجرای فلشر رسمی", Accent, 180);
        launch.Click += (_, _) => LaunchFlasher();
        final.Controls.AddRange([launch, _flashConsent]);
        body.Controls.Add(final);
        return page;
    }

    private TabPage BuildGuide()
    {
        var page = NewPage("راهنما و ایمنی");
        var text = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32),
            Font = new Font("Segoe UI", 11f),
            ForeColor = Navy,
            Text = "اتصال پیشنهادی\r\n\r\n" +
                   "• منبع تغذیه پایدار ۳٫۷ تا ۴٫۲ ولت با تحمل پیک حدود ۲ آمپر\r\n" +
                   "• زمین مشترک بین USB‑TTL و SIM800L و اتصال ضربدری TX/RX\r\n" +
                   "• سطح منطقی UART سازگار و آنتن مناسب\r\n\r\n" +
                   "روند کار\r\n\r\n" +
                   "۱) پورت و Baud را انتخاب کنید. ۲) اتصال را بزنید. ۳) اطلاعات دستگاه را دریافت کنید. ۴) تنظیمات مجاز را از ترمینال AT اعمال کنید.\r\n\r\n" +
                   "IMEI فقط خوانده و نمایش داده می‌شود. این برنامه فرمان نوشتن IMEI یا داده‌های NV را ارسال نمی‌کند. برای firmware فقط از فایل و ابزار رسمی سازگار با revision دقیق برد استفاده کنید."
        };
        page.Controls.Add(text);
        return page;
    }

    private static TabPage NewPage(string title) => new(title) { BackColor = Background, Padding = new Padding(4) };

    private static void ConfigureCombo(ComboBox combo, int x, int y, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Location = new Point(x, y);
        combo.Width = width;
    }

    private static void StyleButton(Button button, int x, int y, int width, int height, Color background, Color foreground)
    {
        button.Location = new Point(x, y);
        button.Size = new Size(width, height);
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
    }

    private static Button NewActionButton(string text, Color background, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 40,
            BackColor = background,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(8)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void AddCard(TableLayoutPanel layout, int column, int row, string title, string key, string hint)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(8), Padding = new Padding(16) };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        var label = new Label { Text = title, ForeColor = Muted, AutoSize = true, Location = new Point(16, 14) };
        var value = new Label
        {
            Text = "—",
            ForeColor = Navy,
            Font = new Font("Segoe UI Semibold", 11.5f),
            AutoEllipsis = true,
            Location = new Point(16, 44),
            Size = new Size(300, 30),
            RightToLeft = RightToLeft.No,
            TextAlign = ContentAlignment.MiddleRight
        };
        var meta = new Label { Text = hint, ForeColor = Color.FromArgb(145, 153, 168), AutoSize = true, Location = new Point(16, 78) };
        card.Controls.AddRange([label, value, meta]);
        _values[key] = value;
        layout.Controls.Add(card, column, row);
    }

    private Control BuildPickerRow(string label, TextBox target, string buttonText, Action handler)
    {
        var row = new Panel { Dock = DockStyle.Fill, Height = 58 };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(950, 17), ForeColor = Navy });
        target.ReadOnly = true;
        target.RightToLeft = RightToLeft.No;
        target.Location = new Point(280, 12);
        target.Width = 640;
        row.Controls.Add(target);
        var button = NewActionButton(buttonText, Navy, 110);
        button.Location = new Point(150, 7);
        button.Click += (_, _) => handler();
        row.Controls.Add(button);
        return row;
    }

    private void RefreshPorts()
    {
        var selected = _ports.SelectedItem?.ToString();
        var names = SerialPort.GetPortNames().OrderBy(x => x).ToArray();
        _ports.Items.Clear();
        _ports.Items.AddRange(names);
        if (selected is not null && _ports.Items.Contains(selected)) _ports.SelectedItem = selected;
        else if (_ports.Items.Count > 0) _ports.SelectedIndex = 0;
        _activity.Text = names.Length == 0 ? "هیچ پورت COM پیدا نشد." : $"{names.Length} پورت پیدا شد.";
    }

    private async Task ToggleConnectionAsync()
    {
        if (_device.IsOpen)
        {
            _device.Close();
            SetConnection(false);
            return;
        }
        if (_ports.SelectedItem is null)
        {
            MessageBox.Show(this, "هیچ پورت COM انتخاب نشده است.", "اتصال", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            SetBusy(true);
            _device.Open(_ports.SelectedItem.ToString()!, int.Parse(_baud.SelectedItem!.ToString()!));
            var response = await _device.SendAsync("AT", 2500);
            AppendTerminal("AT", response);
            if (!response.Contains("OK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("پورت باز شد اما ماژول پاسخ OK نداد. Baud و سیم‌کشی را بررسی کنید.");
            SetConnection(true);
        }
        catch (Exception ex)
        {
            _device.Close();
            SetConnection(false);
            MessageBox.Show(this, ex.Message, "خطای اتصال", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task LoadSnapshotAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            SetBusy(true);
            var progress = new Progress<string>(command => _activity.Text = "در حال اجرا: " + command);
            var data = await _device.ReadSnapshotAsync(progress);
            _values["model"].Text = data.Model;
            _values["revision"].Text = data.Revision;
            _values["imei"].Text = data.Imei;
            _values["imsi"].Text = data.Imsi;
            _values["iccid"].Text = data.Iccid;
            _values["operator"].Text = data.OperatorName;
            _values["signal"].Text = data.Signal;
            _values["registration"].Text = data.Registration;
            _values["sim"].Text = data.SimStatus;
            _activity.Text = "اطلاعات با موفقیت دریافت شد.";
        }
        catch (Exception ex)
        {
            _activity.Text = "دریافت اطلاعات متوقف شد.";
            MessageBox.Show(this, ex.Message, "خطای خواندن", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task RunCommandAsync(string command, bool switchToTerminal)
    {
        if (!AtGuard.IsAllowed(command, out var reason))
        {
            MessageBox.Show(this, reason, "فرمان مسدود شد", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!EnsureConnected()) return;
        try
        {
            SetBusy(true);
            var response = await _device.SendAsync(command);
            AppendTerminal(command, response);
            if (switchToTerminal) _tabs.SelectedIndex = 1;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "خطای فرمان", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    private bool EnsureConnected()
    {
        if (_device.IsOpen) return true;
        MessageBox.Show(this, "ابتدا به ماژول متصل شوید.", "اتصال لازم است", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void AppendTerminal(string command, string response)
    {
        _terminal.AppendText($"[{DateTime.Now:HH:mm:ss}] > {command}\r\n{response}\r\n\r\n");
        _terminal.SelectionStart = _terminal.TextLength;
        _terminal.ScrollToCaret();
    }

    private void SetConnection(bool connected)
    {
        _connectionStatus.Text = connected ? $"● متصل — {_device.PortName}" : "● قطع";
        _connectionStatus.ForeColor = connected ? Color.FromArgb(116, 226, 175) : Color.FromArgb(255, 143, 143);
        _connectButton.Text = connected ? "قطع اتصال" : "اتصال";
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _connectButton.Enabled = !busy;
        _scanButton.Enabled = !busy;
        _sendButton.Enabled = !busy;
    }

    private void ChooseFirmware()
    {
        using var dialog = new OpenFileDialog { Title = "انتخاب firmware رسمی", Filter = "Firmware|*.bin;*.img;*.lod;*.cfg;*.pac|All files|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _firmware = FirmwarePackage.Inspect(dialog.FileName);
            _firmwarePath.Text = _firmware.FullPath;
            _firmwareDetails.Text = $"Size: {_firmware.Size:N0} bytes\r\nSHA-256: {_firmware.Sha256}";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "فایل نامعتبر", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ChooseFlasher()
    {
        using var dialog = new OpenFileDialog { Title = "انتخاب ابزار رسمی سازنده", Filter = "Executable|*.exe" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _flasherPath.Text = dialog.FileName;
    }

    private void LaunchFlasher()
    {
        if (_firmware is null) { MessageBox.Show(this, "ابتدا فایل firmware را انتخاب کنید."); return; }
        if (!File.Exists(_flasherPath.Text)) { MessageBox.Show(this, "ابزار رسمی فلش را انتخاب کنید."); return; }
        if (!_flashConsent.Checked) { MessageBox.Show(this, "تأیید تطابق مدل و revision لازم است."); return; }
        if (_ports.SelectedItem is null) { MessageBox.Show(this, "پورت COM را انتخاب کنید."); return; }

        var result = MessageBox.Show(this,
            $"SHA-256:\r\n{_firmware.Sha256}\r\n\r\nابزار رسمی اجرا شود؟ در زمان فلش برق و کابل را قطع نکنید.",
            "تأیید نهایی", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        _device.Close();
        SetConnection(false);
        var arguments = _flasherArguments.Text
            .Replace("{PORT}", Quote(_ports.SelectedItem.ToString()!))
            .Replace("{BAUD}", Quote(_baud.SelectedItem!.ToString()!))
            .Replace("{FIRMWARE}", Quote(_firmware.FullPath));
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _flasherPath.Text,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(_flasherPath.Text)!,
                UseShellExecute = false
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "اجرای فلشر ناموفق بود", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
