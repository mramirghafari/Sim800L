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
    
    private readonly TextBox _imeiInput = new();
    private readonly Label _imeiVerdict = new();
    private readonly TextBox _imeiPlan = new();
    private readonly Label _cardVerdict = new();
    private ImeiNumber? _targetImei;
    private TabPage? _imeiPage;
    private readonly TextBox _logView = new();
    private readonly CheckBox _unlockWrite = new();
    private readonly CheckBox _removeSim = new();
    private readonly CheckBox _persistProfile = new();
    private readonly ComboBox _strategyPicker = new();
    private readonly TextBox _writeLog = new();
    private readonly Label _writeVerdict = new();
    private readonly Button _writeButton = new();
    private bool _writing;
    private CancellationTokenSource? _writeCts;
    private const string InitialLogPlaceholder = "برای دیدن لاگ، دکمه «ذخیره فایل لاگ» را بزنید یا از تب ترمینال AT فرمانی ارسال کنید. داده شخصی به‌صورت خودکار ستاره‌دار می‌شود.";

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
        _imeiPage = BuildImeiLab();
        _tabs.TabPages.Add(_imeiPage);
        _tabs.TabPages.Add(BuildCardProfile());
        _tabs.TabPages.Add(BuildLogs());
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

    private TabPage BuildImeiLab()
    {
        var page = NewPage("بررسی و نوشتن IMEI");
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        page.Controls.Add(body);

        var notice = new Label
        {
            Text = "نوشتن IMEI فقط با تأیید صریح شما فعال می‌شود. صحت کار همیشه با بازخوانی خودکار تأیید می‌شود و " +
                   "IMEI قبلی در گزارش ذخیره می‌گردد. پیش از شروع: سیم‌کارت را خارج و تغذیه را پایدار کنید.",
            ForeColor = Color.FromArgb(160, 72, 45),
            BackColor = Color.FromArgb(255, 244, 232),
            Padding = new Padding(12, 8, 12, 8),
            Dock = DockStyle.Fill
        };
        body.Controls.Add(notice, 0, 0);

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        var caption = new Label { Text = "IMEI هدف (۱۵ رقم)", AutoSize = true, ForeColor = Navy, Margin = new Padding(8, 12, 8, 0) };
        _imeiInput.Width = 300;
        _imeiInput.MaxLength = 15;
        _imeiInput.RightToLeft = RightToLeft.No;
        _imeiInput.Font = new Font("Cascadia Mono", 12f);
        _imeiInput.Margin = new Padding(8, 6, 8, 0);
        _imeiInput.TextChanged += (_, _) => ValidateImeiInput();
        var useRead = NewActionButton("IMEI فعلی دستگاه", Navy, 165);
        useRead.Margin = new Padding(8, 4, 8, 0);
        useRead.Click += (_, _) => CopyCurrentImei();
        var planButton = NewActionButton("چک‌لیست", Color.FromArgb(70, 92, 125), 120);
        planButton.Margin = new Padding(8, 4, 8, 0);
        planButton.Click += (_, _) => BuildFlashPlan();
        row.Controls.AddRange([caption, _imeiInput, useRead, planButton]);
        body.Controls.Add(row, 0, 1);

        _imeiVerdict.Text = "هنوز چیزی وارد نشده است.";
        _imeiVerdict.ForeColor = Muted;
        _imeiVerdict.AutoSize = true;
        _imeiVerdict.Dock = DockStyle.Fill;
        _imeiVerdict.Padding = new Padding(6, 4, 6, 0);
        body.Controls.Add(_imeiVerdict, 0, 2);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _unlockWrite.Text = "مسئولیت و مالکیت دستگاه را می‌پذیرم و نوشتن IMEI را فعال می‌کنم";
        _unlockWrite.AutoSize = true;
        _unlockWrite.ForeColor = Color.FromArgb(150, 60, 45);
        _unlockWrite.CheckedChanged += (_, _) => ToggleImeiWrite();
        _removeSim.Text = "سیم‌کارت خارج شده است";
        _removeSim.AutoSize = true;
        _removeSim.Checked = true;
        _persistProfile.Text = "ذخیره با AT&W";
        _persistProfile.AutoSize = true;
        _persistProfile.Checked = true;
        _strategyPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _strategyPicker.Width = 330;
        _strategyPicker.Items.Add("خودکار (همه استراتژی‌ها به‌ترتیب)");
        foreach (var strategy in ImeiWriter.Strategies) _strategyPicker.Items.Add(strategy.Title);
        _strategyPicker.SelectedIndex = 0;
        foreach (var control in new Control[] { _unlockWrite, _removeSim, _persistProfile, _strategyPicker })
        {
            control.Margin = new Padding(8, 8, 8, 0);
            options.Controls.Add(control);
        }
        body.Controls.Add(options, 0, 3);

        _writeLog.Dock = DockStyle.Fill;
        _writeLog.Multiline = true;
        _writeLog.ReadOnly = true;
        _writeLog.ScrollBars = ScrollBars.Both;
        _writeLog.WordWrap = false;
        _writeLog.RightToLeft = RightToLeft.Yes;
        _writeLog.BackColor = Color.FromArgb(252, 253, 255);
        _writeLog.Font = new Font("Segoe UI", 9.5f);
        _writeLog.Text = "ترتیب استراتژی‌ها: EGMR با خاموش‌کردن رادیو → EGMR مستقیم → AT+SIMEI (SIMCom) → QCFG (Quectel).\r\n" +
                         "در هر تلاش، پاسخ واقعی ماژول و نتیجه بازخوانی همین‌جا ثبت می‌شود.";
        body.Controls.Add(_writeLog, 0, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _writeButton.Text = "نوشتن و بازخوانی IMEI";
        _writeButton.Width = 210;
        _writeButton.Height = 38;
        _writeButton.BackColor = Color.FromArgb(200, 90, 60);
        _writeButton.ForeColor = Color.White;
        _writeButton.FlatStyle = FlatStyle.Flat;
        _writeButton.FlatAppearance.BorderSize = 0;
        _writeButton.Enabled = false;
        _writeButton.Margin = new Padding(8, 6, 8, 0);
        _writeButton.Click += async (_, _) => await WriteImeiAsync();

        var stop = NewActionButton("توقف", Color.FromArgb(70, 92, 125), 100);
        stop.Margin = new Padding(8, 4, 8, 0);
        stop.Click += (_, _) => _writeCts?.Cancel();

        var save = NewActionButton("ذخیره گزارش", Color.FromArgb(70, 92, 125), 140);
        save.Margin = new Padding(8, 4, 8, 0);
        save.Click += (_, _) => SaveWriteReport();

        actions.Controls.AddRange([_writeButton, stop, save]);
        body.Controls.Add(actions, 0, 5);

        _writeVerdict.Text = "قفل نوشتن بسته است؛ ابتدا چک‌باکس بالا را انتخاب کنید.";
        _writeVerdict.ForeColor = Muted;
        _writeVerdict.AutoSize = true;
        _writeVerdict.Dock = DockStyle.Fill;
        _writeVerdict.Padding = new Padding(6, 4, 6, 0);
        body.Controls.Add(_writeVerdict, 0, 6);
        return page;
    }

    private void ToggleImeiWrite()
    {
        AtGuard.ImeiWriteUnlocked = _unlockWrite.Checked;
        _writeButton.Enabled = _unlockWrite.Checked && !_writing;
        _writeVerdict.ForeColor = _unlockWrite.Checked ? Color.FromArgb(190, 96, 60) : Muted;
        _writeVerdict.Text = _unlockWrite.Checked
            ? "قفل باز است؛ IMEI معتبر را وارد و دکمه نوشتن را بزنید."
            : "قفل نوشتن بسته است؛ ابتدا چک‌باکس بالا را انتخاب کنید.";
    }

    private async Task WriteImeiAsync()
    {
        if (_writing) return;
        if (!EnsureConnected()) return;
        if (!_unlockWrite.Checked)
        {
            MessageBox.Show(this, "ابتدا قفل نوشتن IMEI را باز کنید.", "قفل است", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_targetImei is null)
        {
            ValidateImeiInput();
            MessageBox.Show(this, "IMEI هدف معتبر نیست. مقدار ۱۵ رقمی با رقم کنترلی درست وارد کنید.", "IMEI نامعتبر", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!_removeSim.Checked)
        {
            MessageBox.Show(this, "برای جلوگیری از خطای نوشتن، سیم‌کارت را خارج کنید و سپس تیک مربوطه را بزنید.", "سیم‌کارت", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"IMEI هدف: {_targetImei.Value}\r\n\r\nنوشتن روی حافظه ماژول انجام می‌شود و ممکن است برگشت‌پذیر نباشد. " +
            "IMEI قبلی در گزارش ذخیره می‌شود. ادامه می‌دهید؟",
            "تأیید نهایی نوشتن IMEI", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        var target = _targetImei.Value;
        _writing = true;
        _writeCts = new CancellationTokenSource();
        _writeButton.Enabled = false;
        SetBusy(true);
        _writeLog.Clear();
        AppendWriteLog($"شروع عملیات — هدف {target} — {DateTime.Now:HH:mm:ss}");

        try
        {
            var writer = new ImeiWriter(_device, (command, response) => AppendWriteLog($"  {command} → {Flatten(response)}"));
            var progress = new Progress<string>(message =>
            {
                if (_writeLog.TextLength == 0) _writeLog.Clear();
                AppendWriteLog("» " + message);
            });

            string current = string.Empty;
            if (_strategyPicker.SelectedIndex <= 0)
            {
                var outcome = await writer.AutoWriteAsync(target, _persistProfile.Checked, progress, _writeCts.Token);
                current = outcome.Success?.ImeiAfter ?? outcome.ImeiBefore;
                _writeLog.Text = outcome.Report;
                FinishWrite(outcome.Success is not null, outcome.Success?.Strategy.Title ?? "—", outcome.ImeiBefore, current, target);
            }
            else
            {
                var strategy = ImeiWriter.Strategies[_strategyPicker.SelectedIndex - 1];
                var before = await writer.ReadImeiAsync(_writeCts.Token);
                var result = await writer.WriteAsync(strategy, target, before, _persistProfile.Checked, progress, _writeCts.Token);
                current = result.ImeiAfter;
                _writeLog.Text = ImeiWriter.BuildReport([result], result.Verified ? result : null, before, target);
                FinishWrite(result.Verified, strategy.Title, before, current, target);
            }

            await LoadSnapshotAsync();
        }
        catch (OperationCanceledException)
        {
            AppendWriteLog("عملیات توسط کاربر متوقف شد.");
            _writeVerdict.ForeColor = Color.FromArgb(160, 72, 45);
            _writeVerdict.Text = "متوقف شد. برای اطمینان، IMEI فعلی را دوباره بخوانید.";
        }
        catch (Exception ex)
        {
            AppendWriteLog("خطا: " + ex.Message);
            _writeVerdict.ForeColor = Color.FromArgb(180, 62, 62);
            _writeVerdict.Text = "خطا در اجرای عملیات: " + ex.Message;
        }
        finally
        {
            _writing = false;
            _writeCts?.Dispose();
            _writeCts = null;
            SetBusy(false);
            _writeButton.Enabled = _unlockWrite.Checked;
        }
    }

    private void FinishWrite(bool verified, string strategyTitle, string before, string after, string target)
    {
        if (verified)
        {
            _writeVerdict.ForeColor = Color.FromArgb(0, 130, 110);
            _writeVerdict.Text = $"✓ موفق — {strategyTitle} — IMEI اکنون {after} است. ماژول را یک بار ری‌استارت کنید.";
            _activity.Text = $"IMEI به {after} تغییر کرد و بازخوانی تأیید شد.";
        }
        else
        {
            _writeVerdict.ForeColor = Color.FromArgb(180, 62, 62);
            _writeVerdict.Text = $"✗ ناموفق — IMEI همچنان {(after.Length == 15 ? after : before)} است. گزارش پایین را ذخیره و بررسی کنید.";
            _activity.Text = "نوشتن IMEI ناموفق بود؛ گزارش عملیات ثبت شد.";
        }
    }

    private void AppendWriteLog(string line)
    {
        _writeLog.AppendText(line + Environment.NewLine);
        _writeLog.SelectionStart = _writeLog.TextLength;
        _writeLog.ScrollToCaret();
    }

    private void SaveWriteReport()
    {
        if (_writeLog.TextLength == 0)
        {
            MessageBox.Show(this, "هنوز گزارشی ساخته نشده است.");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "ذخیره گزارش نوشتن IMEI",
            Filter = "Text|*.txt",
            FileName = $"imei-write-{DateTime.Now:yyyyMMdd-HHmm}.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _writeLog.Text);
        _activity.Text = "گزارش ذخیره شد: " + dialog.FileName;
    }

    private static string Flatten(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "(خالی)";
        var flat = string.Join(" ", response.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 120 ? flat : flat[..120] + "…";
    }

    private TabPage BuildCardProfile()
    {
        var page = NewPage("کارت و سیم‌کارت");
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        page.Controls.Add(body);

        var head = new Panel { Dock = DockStyle.Fill };
        head.Controls.Add(new Label
        {
            Text = "خواندن داده کارت",
            Font = new Font("Segoe UI Semibold", 13f),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(850, 8)
        });
        head.Controls.Add(new Label
        {
            Text = "SIM800L فقط کارت فیزیکی را می‌خواند. eSIM/eUICC نیاز به ابزار LPA/APDU جداگانه دارد.",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(540, 40)
        });
        body.Controls.Add(head, 0, 0);

        _cardVerdict.Dock = DockStyle.Fill;
        _cardVerdict.Padding = new Padding(10);
        _cardVerdict.BackColor = Color.White;
        _cardVerdict.ForeColor = Navy;
        _cardVerdict.Font = new Font("Segoe UI", 10.5f);
        _cardVerdict.Text = SimCardProfile.Describe("—", "—") + string.Join(Environment.NewLine, SimCardProfile.SafetyNotes().Select(n => "• " + n));
        body.Controls.Add(_cardVerdict, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var probe = NewActionButton("بررسی کارت", Accent, 150);
        probe.Click += async (_, _) => await ProbeCardAsync();
        var notes = NewActionButton("نمایش نکات ایمنی", Color.FromArgb(70, 92, 125), 160);
        notes.Click += (_, _) => _cardVerdict.Text = string.Join(Environment.NewLine, SimCardProfile.SafetyNotes().Select(n => "• " + n));
        actions.Controls.AddRange([probe, notes]);
        body.Controls.Add(actions, 0, 2);
        return page;
    }

    private TabPage BuildLogs()
    {
        var page = NewPage("گزارش‌ها و لاگ");
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        page.Controls.Add(body);

        body.Controls.Add(new Label
        {
            Text = "تمام فرمان‌ها و پاسخ‌ها با زمان ذخیره می‌شوند. محتوای ICCID/IMSI و IMEI داده شخصی است؛ قبل از ارسال به شخص دیگر آن را پاک کنید.",
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 12, 4, 0)
        }, 0, 0);

        _logView.Dock = DockStyle.Fill;
        _logView.Multiline = true;
        _logView.ReadOnly = true;
        _logView.ScrollBars = ScrollBars.Both;
        _logView.WordWrap = false;
        _logView.RightToLeft = RightToLeft.No;
        _logView.Font = new Font("Cascadia Mono", 9.5f);
        _logView.BackColor = Color.White;
        _logView.Text = InitialLogPlaceholder;
        body.Controls.Add(_logView, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var export = NewActionButton("ذخیره فایل لاگ", Accent, 160);
        export.Click += (_, _) => ExportLog();
        var clear = NewActionButton("پاک کردن نمایش", Color.FromArgb(70, 92, 125), 140);
        clear.Click += (_, _) => _logView.Clear();
        var redact = NewActionButton("پاک‌سازی داده شخصی", Color.FromArgb(70, 92, 125), 190);
        redact.Click += (_, _) => _logView.Text = SessionLogger.Redact(_logView.Text);
        actions.Controls.AddRange([export, clear, redact]);
        body.Controls.Add(actions, 0, 2);
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
                   "• سطح منطقی UART سازگار (با مقسم مقاومتی اگر آداپتور ۵ ولت است) و آنتن مناسب\r\n" +
                   "• خازن ۱۰۰۰ میکروفاراد نزدیک پایه VBAT برای جلوگیری از ریست شدن هنگام ثبت شبکه\r\n\r\n" +
                   "روند کار\r\n\r\n" +
                   "۱) پورت و Baud را انتخاب و وصل شوید. ۲) در داشبورد «دریافت همه اطلاعات» را بزنید. " +
                   "۳) در تب «کارت و سیم‌کارت» وضعیت ICCID/IMSI و PIN را بررسی کنید. " +
                   "۴) در تب «بررسی IMEI» شناسه را از نظر ساختاری بیازمایید و چک‌لیست پشتیبان را بگیرید.\r\n\r\n" +
                   "نوشتن IMEI — آنچه واقعاً اتفاق می‌افتد\r\n\r\n" +
                   "۱) چک‌باکس تأیید را در تب «بررسی و نوشتن IMEI» بزنید تا قفل فرمان‌ها باز شود (AtGuard.ImeiWriteUnlocked).\r\n" +
                   "۲) سیم‌کارت را خارج کنید؛ بعضی firmwareها با سیم‌کارت حاضر خطا می‌دهند.\r\n" +
                   "۳) برنامه خودکار چند استراتژی را به‌ترتیب امتحان می‌کند: AT+CFUN=0 + AT+EGMR=1,7,\"…\" + AT+CFUN=1، سپس AT+EGMR مستقیم، سپس AT+SIMEI (اختصاصی SIMCom، با محدودیت تعداد نوشتن) و در آخر AT+QCFG مخصوص Quectel.\r\n" +
                   "۴) موفقیت فقط وقتی اعلام می‌شود که شناسه با AT+GSN/AT+CGSN بازخوانی و با هدف مقایسه شود. اگر فرمان‌ها OK بگیرند اما شناسه تغییر نکند، صادقانه «ناموفق» گزارش می‌شود.\r\n" +
                   "۵) IMEI قبلی همیشه در گزارش ذخیره می‌شود؛ برای بازگردانی همان مسیر را با مقدار قبلی تکرار کنید.\r\n\r\n" +
                   "اگر همه استراتژی‌ها ERROR دادند، firmware شما فرمان نوشتن ندارد؛ تنها مسیر باقی‌مانده فلش بسته اختصاصی همان revision با ابزار رسمی است.\r\n\r\n" +
                   "درباره سیم‌کارت\r\n\r\n" +
                   "کارت فیزیکی: خواندن ICCID/IMSI/PIN مجاز است؛ نوشتن شناسه روی کارت از طریق AT معمول نیست و انجام نمی‌شود. " +
                   "eSIM/eUICC و MFF2 با LPA/APDU مدیریت می‌شوند و SIM800L از AT+CCHO/AT+CSIM پشتیبانی کامل ندارد."
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
            if (_writing)
            {
                MessageBox.Show(this, "در حال نوشتن IMEI هستید؛ تا پایان کار اتصال را قطع نکنید.", "عملیات در جریان", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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

    private void ValidateImeiInput()
    {
        if (ImeiNumber.TryParse(_imeiInput.Text, out var imei, out var error))
        {
            _targetImei = imei;
            _imeiVerdict.ForeColor = Color.FromArgb(0, 130, 110);
            _imeiVerdict.Text = $"✓ ساختار درست است — TAC: {imei!.TacFormatted} | سریال: {imei.Serial} | رقم کنترلی: {imei.CheckDigit}";
        }
        else
        {
            _targetImei = null;
            _imeiVerdict.ForeColor = Color.FromArgb(180, 62, 62);
            _imeiVerdict.Text = "✗ " + error;
        }
    }

    private void CopyCurrentImei()
    {
        var current = _values.TryGetValue("imei", out var label) ? label.Text : string.Empty;
        var digits = new string(current.Where(char.IsDigit).ToArray());
        if (digits.Length != 15)
        {
            MessageBox.Show(this, "ابتدا اطلاعات دستگاه را از داشبورد دریافت کنید.", "IMEI موجود نیست", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _imeiInput.Text = digits[..15];
    }

    private FlashPlan? CreatePlan()
    {
        if (_targetImei is null && _imeiInput.TextLength > 0)
        {
            MessageBox.Show(this, "IMEI واردشده معتبر نیست؛ ابتدا آن را اصلاح کنید.", "IMEI نامعتبر", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return new FlashPlan
        {
            TargetImei = _targetImei,
            Firmware = _firmware,
            FirmwareVariant = "SIM800L 2/15",
            ModuleRespondsToAt = _device.IsOpen,
            HasValidBackup = _firmware is not null && File.Exists(_firmware.FullPath),
            ConsentGiven = _flashConsent.Checked
        };
    }

    private void BuildFlashPlan()
    {
        var plan = CreatePlan();
        if (plan is null) return;

        var lines = new List<string> { plan.ToReport(), string.Empty, "پشتیبان‌های لازم پیش از هر عملیات:", new string('-', 40) };
        foreach (var item in plan.BackupChecklist())
            lines.Add($"• {item.Name} — منبع: {item.Source} | فرمان: {item.ReadCommand}\r\n  دلیل: {item.Reason}");

        lines.Add(string.Empty);
        if (plan.HasBlockers)
            lines.Add("نتیجه: عملیات متوقف است. موارد Blocker بالا (تأیید، پشتیبان یا firmware) باید برطرف شوند.");
        else
        {
            lines.Add("نتیجه: فلش رسمی firmware پس از تأیید و با ابزار سازنده مجاز است.");
            lines.Add("توجه: مورد Unsupported درباره نوشتن IMEI با هیچ‌یک از فرمان‌های AT یا این برنامه قابل انجام نیست و گزارش آن فقط برای شفافیت است.");
        }

        _imeiPlan.Text = string.Join(Environment.NewLine, lines);
        if (_imeiPage is not null) _tabs.SelectedTab = _imeiPage;
    }

    private void SaveReport()
    {
        if (_imeiPlan.TextLength == 0)
        {
            MessageBox.Show(this, "ابتدا چک‌لیست را بسازید.");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "ذخیره گزارش",
            Filter = "Text|*.txt|Markdown|*.md",
            FileName = $"sim800l-report-{DateTime.Now:yyyyMMdd-HHmm}.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _imeiPlan.Text);
        _activity.Text = "گزارش ذخیره شد: " + dialog.FileName;
    }

    private async Task ProbeCardAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            SetBusy(true);
            var iccid = await _device.SendAsync("AT+CCID", 3000);
            var imsi = await _device.SendAsync("AT+CIMI", 3000);
            var pin = await _device.SendAsync("AT+CPIN?", 3000);
            var sim = await _device.SendAsync("AT+CSIM=0,00A40000023F00", 3000);

            AppendTerminal("AT+CCID", iccid);
            AppendTerminal("AT+CIMI", imsi);
            AppendTerminal("AT+CPIN?", pin);

            var uiccSupported = !sim.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
            _cardVerdict.Text = SimCardProfile.Describe(Simplified(iccid), Simplified(imsi)) +
                Environment.NewLine +
                $"وضعیت PIN: {Simplified(pin)}{Environment.NewLine}" +
                (uiccSupported
                    ? "APDU خام پاسخ داد؛ کارت با فرمان AT+CSIM تا حدی قابل دسترسی است."
                    : "APDU خام پاسخ نداد (ERROR)؛ eUICC/APDU از طریق AT+CSIM در این ماژول پشتیبانی نمی‌شود.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطای خواندن کارت", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private static string Simplified(string response)
    {
        var lines = response.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.Equals("OK", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return lines.Length == 0 ? "—" : string.Join(" ", lines);
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

    private void ExportLog()
    {
        if (_logView.TextLength == 0 || _logView.Text == InitialLogPlaceholder)
            _logView.Text = SessionLogger.Redact(_terminal.Text);
        using var dialog = new SaveFileDialog
        {
            Title = "ذخیره لاگ جلسه",
            Filter = "Text|*.txt|Log|*.log",
            FileName = $"sim800l-session-{DateTime.Now:yyyyMMdd-HHmm}.log"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _logView.Text);
        _activity.Text = "لاگ ذخیره شد: " + dialog.FileName;
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
