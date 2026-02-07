using System.Text.Json;
using LcusRelay.Core.Config;
using LcusRelay.Core.Relay;
using LcusRelay.Tray.Utils;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray.Forms;

public sealed class SettingsForm : Form
{
    private readonly ILogger _log;
    private readonly Func<AppConfig, Task> _onSave;
    private readonly Func<BlinkActionConfig, Task>? _onTestBlink;
    private AppConfig _cfg;

    // Relay
    private TextBox _txtPort = null!;
    private CheckBox _chkAutoDetect = null!;
    private NumericUpDown _numAddress = null!;
    private NumericUpDown _numBaud = null!;
    private NumericUpDown _numTimeout = null!;

    // App
    private CheckBox _chkStartup = null!;

    // Signals
    private CheckBox _chkMeetingEnabled = null!;
    private TextBox _txtMeetingProcs = null!;
    private NumericUpDown _numMeetingPoll = null!;

    private CheckBox _chkSoftwareSignalsEnabled = null!;
    private CheckBox _chkEmitRdpSignal = null!;
    private NumericUpDown _numSoftwarePoll = null!;

    private CheckBox _chkNinjaEnabled = null!;
    private CheckBox _chkNinjaRequireRdp = null!;
    private TextBox _txtNinjaProcs = null!;

    // Meeting reminders (pre-meeting)
    private CheckBox _chkPreMeetingEnabled = null!;
    private TextBox _txtMeetingCron = null!;
    private NumericUpDown _numLeadMin = null!;
    private NumericUpDown _numRepeatMin = null!;
    private NumericUpDown _numBlinkCount = null!;
    private NumericUpDown _numBlinkOnMs = null!;
    private NumericUpDown _numBlinkOffMs = null!;
    private TextBox _txtBlinkOnSeq = null!;
    private TextBox _txtBlinkOffSeq = null!;

    // JSON
    private TextBox _txtJson = null!;

    public SettingsForm(ILogger log,
        AppConfig current,
        Func<AppConfig, Task> onSave,
        Func<BlinkActionConfig, Task>? onTestBlink = null)
    {
        _log = log;
        _onSave = onSave;
        _onTestBlink = onTestBlink;

        // clone (immutabile per annullare)
        _cfg = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(current, ConfigJson.Options), ConfigJson.Options)
               ?? ConfigStore.CreateDefault();

        Text = "LcusRelay – Settings";
        Width = 820;
        Height = 660;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            // ignore
        }

        BuildUi();
        LoadFromConfigToControls();
    }

    private void BuildUi()
    {
        // Header (stile Windows, minimale)
        var header = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(14, 14, 14, 10) };
        Controls.Add(header);

        var pic = new PictureBox
        {
            Width = 44,
            Height = 44,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Left = 0,
            Top = 0
        };
        try
        {
            using var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            pic.Image = ico?.ToBitmap();
        }
        catch { /* ignore */ }
        header.Controls.Add(pic);

        var lblTitle = new Label
        {
            Text = "LcusRelay",
            AutoSize = true,
            Left = 56,
            Top = 2,
            Font = new Font(Font, FontStyle.Bold)
        };
        header.Controls.Add(lblTitle);

        var lblSub = new Label
        {
            Text = "Automazione lampada/relay – tray app",
            AutoSize = true,
            Left = 56,
            Top = 28
        };
        header.Controls.Add(lblSub);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        Controls.Add(tabs);

        var tabBasic = new TabPage("Basic") { Padding = new Padding(12) };
        var tabJson = new TabPage("Advanced (JSON)") { Padding = new Padding(12) };
        tabs.TabPages.Add(tabBasic);
        tabs.TabPages.Add(tabJson);

        // BASIC
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        tabBasic.Controls.Add(scroll);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        scroll.Controls.Add(stack);

        stack.Controls.Add(BuildRelayGroup());
        stack.Controls.Add(BuildSignalsGroup());
        stack.Controls.Add(BuildMeetingReminderGroup());
        stack.Controls.Add(BuildAppGroup());

        // JSON tab
        _txtJson = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f)
        };
        tabJson.Controls.Add(_txtJson);

        // Bottom buttons
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12) };
        Controls.Add(bottom);

        var btnCancel = new Button { Text = "Cancel", Width = 110, Height = 32, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        btnCancel.Left = bottom.Width - btnCancel.Width - 12;
        btnCancel.Top = 12;
        btnCancel.Click += (_, __) => Close();
        btnCancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.Add(btnCancel);

        var btnSave = new Button { Text = "Save", Width = 110, Height = 32, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        btnSave.Left = btnCancel.Left - btnSave.Width - 10;
        btnSave.Top = 12;
        btnSave.Click += async (_, __) => await SaveAndClose();
        btnSave.DialogResult = DialogResult.OK;
        bottom.Controls.Add(btnSave);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        bottom.Resize += (_, __) =>
        {
            btnCancel.Left = bottom.Width - btnCancel.Width - 12;
            btnSave.Left = btnCancel.Left - btnSave.Width - 10;
        };
    }

    private GroupBox BuildRelayGroup()
    {
        var g = new GroupBox { Text = "Hardware (Relay)", Width = 760, Height = 210, Padding = new Padding(12) };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            AutoSize = false
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        // Row 0: Port
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "COM Port (es. COM5)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _txtPort = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        t.Controls.Add(_txtPort, 1, 0);
        var btnDetect = new Button { Text = "Auto-detect CH340", Anchor = AnchorStyles.Right };
        btnDetect.Click += (_, __) => DetectPort();
        t.Controls.Add(btnDetect, 2, 0);

        // Row 1: autodetect
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _chkAutoDetect = new CheckBox { Text = "AutoDetect CH340 se port vuota", AutoSize = true, Anchor = AnchorStyles.Left };
        t.SetColumnSpan(_chkAutoDetect, 2);
        t.Controls.Add(_chkAutoDetect, 1, 1);

        // Row 2..4: numeric
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        t.Controls.Add(new Label { Text = "Address", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _numAddress = new NumericUpDown { Minimum = 1, Maximum = 255, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numAddress, 1, 2);

        t.Controls.Add(new Label { Text = "BaudRate", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        _numBaud = new NumericUpDown { Minimum = 1200, Maximum = 115200, Increment = 1200, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numBaud, 1, 3);

        t.Controls.Add(new Label { Text = "Timeout (ms)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        _numTimeout = new NumericUpDown { Minimum = 50, Maximum = 5000, Increment = 50, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numTimeout, 1, 4);

        // Row 5: test buttons
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var testPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnOn = new Button { Text = "Lamp ON", Width = 110 };
        btnOn.Click += async (_, __) => await TestRelay(true);
        var btnOff = new Button { Text = "Lamp OFF", Width = 110 };
        btnOff.Click += async (_, __) => await TestRelay(false);
        var btnBlink = new Button { Text = "Test Blink", Width = 120 };
        btnBlink.Click += async (_, __) => await TestBlinkFromUi();
        testPanel.Controls.Add(btnOn);
        testPanel.Controls.Add(btnOff);
        testPanel.Controls.Add(btnBlink);
        t.SetColumnSpan(testPanel, 2);
        t.Controls.Add(testPanel, 1, 5);

        g.Controls.Add(t);
        return g;
    }

    private GroupBox BuildSignalsGroup()
    {
        var g = new GroupBox { Text = "Signals (processi / RDP)", Width = 760, Height = 250, Padding = new Padding(12) };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            AutoSize = false
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        int row = 0;

        // Meeting
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Meeting detection", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _chkMeetingEnabled = new CheckBox { Text = "Enabled", AutoSize = true, Anchor = AnchorStyles.Left };
        t.Controls.Add(_chkMeetingEnabled, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Meeting processes", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _txtMeetingProcs = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "Teams, Zoom, ..." };
        t.SetColumnSpan(_txtMeetingProcs, 2);
        t.Controls.Add(_txtMeetingProcs, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Meeting poll (s)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numMeetingPoll = new NumericUpDown { Minimum = 1, Maximum = 60, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numMeetingPoll, 1, row);
        row++;

        // SoftwareSignals + RDP
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Software signals", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _chkSoftwareSignalsEnabled = new CheckBox { Text = "Enabled", AutoSize = true, Anchor = AnchorStyles.Left };
        t.Controls.Add(_chkSoftwareSignalsEnabled, 1, row);
        _chkEmitRdpSignal = new CheckBox { Text = "Emit RDP signal", AutoSize = true, Anchor = AnchorStyles.Left };
        t.Controls.Add(_chkEmitRdpSignal, 2, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Software poll (s)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numSoftwarePoll = new NumericUpDown { Minimum = 1, Maximum = 60, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numSoftwarePoll, 1, row);
        row++;

        // Ninja
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Ninja Desktop", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _chkNinjaEnabled = new CheckBox { Text = "Enabled", AutoSize = true, Anchor = AnchorStyles.Left };
        t.Controls.Add(_chkNinjaEnabled, 1, row);
        _chkNinjaRequireRdp = new CheckBox { Text = "Only when RDP", AutoSize = true, Anchor = AnchorStyles.Left };
        t.Controls.Add(_chkNinjaRequireRdp, 2, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Ninja processes", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _txtNinjaProcs = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "NinjaRMMAgentPatcher, ..." };
        t.SetColumnSpan(_txtNinjaProcs, 2);
        t.Controls.Add(_txtNinjaProcs, 1, row);

        g.Controls.Add(t);
        return g;
    }

    private GroupBox BuildMeetingReminderGroup()
    {
        var g = new GroupBox { Text = "Meeting reminders (pre)", Width = 760, Height = 300, Padding = new Padding(12) };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 9,
            AutoSize = false
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        var row = 0;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Enabled", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _chkPreMeetingEnabled = new CheckBox { Text = "Enable pre-meeting reminders", AutoSize = true, Anchor = AnchorStyles.Left };
        t.SetColumnSpan(_chkPreMeetingEnabled, 2);
        t.Controls.Add(_chkPreMeetingEnabled, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Meeting start cron", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _txtMeetingCron = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "0 9 * * 1-5" };
        t.SetColumnSpan(_txtMeetingCron, 2);
        t.Controls.Add(_txtMeetingCron, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Lead (minutes)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numLeadMin = new NumericUpDown { Minimum = 1, Maximum = 180, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numLeadMin, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Repeat every (min)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numRepeatMin = new NumericUpDown { Minimum = 1, Maximum = 60, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numRepeatMin, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Blink count", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numBlinkCount = new NumericUpDown { Minimum = 1, Maximum = 50, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numBlinkCount, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Blink ON (ms)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numBlinkOnMs = new NumericUpDown { Minimum = 20, Maximum = 10000, Increment = 10, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numBlinkOnMs, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Blink OFF (ms)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _numBlinkOffMs = new NumericUpDown { Minimum = 20, Maximum = 10000, Increment = 10, Width = 140, Anchor = AnchorStyles.Left };
        t.Controls.Add(_numBlinkOffMs, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "ON sequence (csv)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _txtBlinkOnSeq = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "es. 150,250,400" };
        t.SetColumnSpan(_txtBlinkOnSeq, 2);
        t.Controls.Add(_txtBlinkOnSeq, 1, row);
        row++;

        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "OFF sequence (csv)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _txtBlinkOffSeq = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "es. 150,250,400" };
        t.Controls.Add(_txtBlinkOffSeq, 1, row);
        var btnTest = new Button { Text = "Test Blink", Width = 120, Anchor = AnchorStyles.Right };
        btnTest.Click += async (_, __) => await TestBlinkFromUi();
        t.Controls.Add(btnTest, 2, row);

        g.Controls.Add(t);
        return g;
    }

    private GroupBox BuildAppGroup()
    {
        var g = new GroupBox { Text = "App", Width = 760, Height = 170, Padding = new Padding(12) };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        // Startup
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.Controls.Add(new Label { Text = "Start with Windows", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _chkStartup = new CheckBox { Text = "Current user", AutoSize = true, Anchor = AnchorStyles.Left };
        t.Controls.Add(_chkStartup, 1, 0);

        // Buttons
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnEdit = new Button { Text = "Open config.json", Width = 150 };
        btnEdit.Click += (_, __) =>
        {
            if (!ConfigStore.TryOpenConfigInEditor(_log, out var error))
            {
                MessageBox.Show(this, $"Impossibile aprire config.json: {error}", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        var btnReload = new Button { Text = "Reload JSON → Basic", Width = 170 };
        btnReload.Click += (_, __) => LoadFromJsonTextbox();
        btnPanel.Controls.Add(btnEdit);
        btnPanel.Controls.Add(btnReload);
        t.SetColumnSpan(btnPanel, 2);
        t.Controls.Add(btnPanel, 1, 1);

        // Note
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var note = new Label
        {
            Text = "Suggerimento: puoi definire regole su trigger come 'signal:rdp:on' o 'signal:ninja-desktop:on' nel JSON.",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        t.SetColumnSpan(note, 3);
        t.Controls.Add(note, 0, 2);

        g.Controls.Add(t);
        return g;
    }

    private void LoadFromConfigToControls()
    {
        _txtPort.Text = _cfg.Serial.Port ?? "";
        _chkAutoDetect.Checked = _cfg.Serial.AutoDetectCh340;
        _numAddress.Value = Math.Clamp(_cfg.Serial.Address, 1, 255);
        _numBaud.Value = Math.Clamp(_cfg.Serial.BaudRate, 1200, 115200);
        _numTimeout.Value = Math.Clamp(_cfg.Serial.TimeoutMs, 50, 5000);
        _chkStartup.Checked = _cfg.StartWithWindows;

        // Meeting
        _chkMeetingEnabled.Checked = _cfg.MeetingSignal.Enabled;
        _numMeetingPoll.Value = Math.Clamp(_cfg.MeetingSignal.PollSeconds, 1, 60);
        _txtMeetingProcs.Text = string.Join(", ", _cfg.MeetingSignal.ProcessNames ?? new List<string>());

        // Software signals
        _chkSoftwareSignalsEnabled.Checked = _cfg.SoftwareSignals.Enabled;
        _chkEmitRdpSignal.Checked = _cfg.SoftwareSignals.EmitRdpSignal;
        _numSoftwarePoll.Value = Math.Clamp(_cfg.SoftwareSignals.PollSeconds, 1, 60);

        var ninja = GetOrCreateNinjaSignal();
        _chkNinjaEnabled.Checked = ninja.Enabled;
        _chkNinjaRequireRdp.Checked = ninja.RequireRdp;
        _txtNinjaProcs.Text = string.Join(", ", ninja.ProcessNames ?? new List<string>());

        // Pre-meeting reminders
        var rem = GetOrCreateMeetingReminderRule();
        _chkPreMeetingEnabled.Checked = _cfg.MeetingReminders.Enabled && rem.Enabled;
        _txtMeetingCron.Text = string.IsNullOrWhiteSpace(rem.MeetingCron) ? "0 9 * * 1-5" : rem.MeetingCron;
        _numLeadMin.Value = Math.Clamp(rem.LeadMinutes, 1, 180);
        _numRepeatMin.Value = Math.Clamp(rem.RepeatEveryMinutes, 1, 60);

        _numBlinkCount.Value = Math.Clamp(rem.Blink.Count, 1, 50);
        _numBlinkOnMs.Value = Math.Clamp(rem.Blink.OnMs, 20, 10000);
        _numBlinkOffMs.Value = Math.Clamp(rem.Blink.OffMs, 20, 10000);
        _txtBlinkOnSeq.Text = JoinCsv(rem.Blink.OnMsSequence);
        _txtBlinkOffSeq.Text = JoinCsv(rem.Blink.OffMsSequence);

        _txtJson.Text = JsonSerializer.Serialize(_cfg, ConfigJson.Options);
    }

    private void LoadFromControlsToConfig()
    {
        _cfg.Serial.Port = string.IsNullOrWhiteSpace(_txtPort.Text) ? null : _txtPort.Text.Trim();
        _cfg.Serial.AutoDetectCh340 = _chkAutoDetect.Checked;
        _cfg.Serial.Address = (int)_numAddress.Value;
        _cfg.Serial.BaudRate = (int)_numBaud.Value;
        _cfg.Serial.TimeoutMs = (int)_numTimeout.Value;

        _cfg.StartWithWindows = _chkStartup.Checked;

        // Meeting
        _cfg.MeetingSignal.Enabled = _chkMeetingEnabled.Checked;
        _cfg.MeetingSignal.PollSeconds = (int)_numMeetingPoll.Value;
        _cfg.MeetingSignal.ProcessNames = SplitCsv(_txtMeetingProcs.Text);

        // Software signals
        _cfg.SoftwareSignals.Enabled = _chkSoftwareSignalsEnabled.Checked;
        _cfg.SoftwareSignals.EmitRdpSignal = _chkEmitRdpSignal.Checked;
        _cfg.SoftwareSignals.PollSeconds = (int)_numSoftwarePoll.Value;

        var ninja = GetOrCreateNinjaSignal();
        ninja.Enabled = _chkNinjaEnabled.Checked;
        ninja.RequireRdp = _chkNinjaRequireRdp.Checked;
        ninja.ProcessNames = SplitCsv(_txtNinjaProcs.Text);

        // Pre-meeting reminders
        var rem = GetOrCreateMeetingReminderRule();
        _cfg.MeetingReminders.Enabled = _chkPreMeetingEnabled.Checked;
        _cfg.MeetingReminders.PollSeconds = Math.Clamp(_cfg.MeetingReminders.PollSeconds, 5, 60);

        rem.Enabled = _chkPreMeetingEnabled.Checked;
        rem.MeetingCron = string.IsNullOrWhiteSpace(_txtMeetingCron.Text) ? "0 9 * * 1-5" : _txtMeetingCron.Text.Trim();
        rem.LeadMinutes = (int)_numLeadMin.Value;
        rem.RepeatEveryMinutes = (int)_numRepeatMin.Value;

        rem.Blink.Count = (int)_numBlinkCount.Value;
        rem.Blink.OnMs = (int)_numBlinkOnMs.Value;
        rem.Blink.OffMs = (int)_numBlinkOffMs.Value;
        rem.Blink.OnMsSequence = ParseCsvInt(_txtBlinkOnSeq.Text);
        rem.Blink.OffMsSequence = ParseCsvInt(_txtBlinkOffSeq.Text);
        rem.Blink.RestoreInitialState = true;

        // keep JSON in sync
        _txtJson.Text = JsonSerializer.Serialize(_cfg, ConfigJson.Options);
    }

    private SoftwareSignalDefinition GetOrCreateNinjaSignal()
    {
        _cfg.SoftwareSignals.Signals ??= new List<SoftwareSignalDefinition>();

        var ninja = _cfg.SoftwareSignals.Signals
            .FirstOrDefault(s => string.Equals(s.Name, "ninja-desktop", StringComparison.OrdinalIgnoreCase));

        if (ninja is null)
        {
            ninja = new SoftwareSignalDefinition
            {
                Name = "ninja-desktop",
                Enabled = false,
                RequireRdp = true,
                ProcessNames = new List<string>()
            };

            _cfg.SoftwareSignals.Signals.Add(ninja);
        }

        return ninja;
    }

    private MeetingReminderRuleConfig GetOrCreateMeetingReminderRule()
    {
        _cfg.MeetingReminders ??= new MeetingRemindersConfig();
        _cfg.MeetingReminders.Rules ??= new List<MeetingReminderRuleConfig>();

        var rule = _cfg.MeetingReminders.Rules
            .FirstOrDefault(r => string.Equals(r.Name, "default", StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            rule = new MeetingReminderRuleConfig
            {
                Name = "default",
                Enabled = false,
                MeetingCron = "0 9 * * 1-5",
                LeadMinutes = 10,
                RepeatEveryMinutes = 2,
                Blink = new BlinkActionConfig
                {
                    Count = 3,
                    OnMs = 250,
                    OffMs = 250,
                    RestoreInitialState = true
                }
            };

            _cfg.MeetingReminders.Rules.Add(rule);
        }

        rule.Blink ??= new BlinkActionConfig();
        return rule;
    }

    private static List<string> SplitCsv(string? csv)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(csv)) return list;

        foreach (var raw in csv.Split(','))
        {
            var v = (raw ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
        }

        return list;
    }

    private static string JoinCsv(IEnumerable<int>? values)
    {
        if (values is null) return string.Empty;
        return string.Join(",", values);
    }

    private static List<int>? ParseCsvInt(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;

        var list = new List<int>();
        foreach (var token in csv.Split(','))
        {
            var raw = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            if (int.TryParse(raw, out var v))
            {
                list.Add(Math.Clamp(v, 20, 10000));
            }
        }

        return list.Count > 0 ? list : null;
    }

    private void LoadFromJsonTextbox()
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(_txtJson.Text, ConfigJson.Options);
            if (cfg is null) throw new InvalidOperationException("JSON non valido.");
            _cfg = cfg;
            LoadFromConfigToControls();
            MessageBox.Show(this, "OK: JSON caricato nei controlli Basic.", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"JSON non valido: {ex.Message}", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DetectPort()
    {
        var port = PortDetection.TryFindCh340ComPort(_log);
        if (string.IsNullOrWhiteSpace(port))
        {
            MessageBox.Show(this, "CH340/USB-Serial non trovato. Passa COM manualmente o verifica i driver.", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtPort.Text = port;
        MessageBox.Show(this, $"Trovato: {port}", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task TestRelay(bool on)
    {
        try
        {
            LoadFromControlsToConfig();

            var port = _cfg.Serial.Port;
            if (string.IsNullOrWhiteSpace(port) && _cfg.Serial.AutoDetectCh340)
            {
                port = PortDetection.TryFindCh340ComPort(_log);
            }

            if (string.IsNullOrWhiteSpace(port))
            {
                MessageBox.Show(this, "Port non configurata.", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var relay = new LcusSerialRelayController(new SerialRelaySettings
            {
                PortName = port,
                Address = _cfg.Serial.Address,
                BaudRate = _cfg.Serial.BaudRate,
                TimeoutMs = _cfg.Serial.TimeoutMs
            });

            await relay.SetAsync(on);
            MessageBox.Show(this, $"OK: {(on ? "ON" : "OFF")} su {port} (addr={_cfg.Serial.Address})", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Test relay failed.");
            MessageBox.Show(this, $"Errore: {ex.Message}", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task TestBlinkFromUi()
    {
        try
        {
            if (_onTestBlink is null)
            {
                MessageBox.Show(this,
                    "Test Blink non disponibile in questa build.",
                    "LcusRelay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            LoadFromControlsToConfig();
            var rem = GetOrCreateMeetingReminderRule();

            var blink = new BlinkActionConfig
            {
                Count = rem.Blink.Count,
                OnMs = rem.Blink.OnMs,
                OffMs = rem.Blink.OffMs,
                OnMsSequence = rem.Blink.OnMsSequence,
                OffMsSequence = rem.Blink.OffMsSequence,
                RestoreInitialState = true
            };

            await _onTestBlink(blink);

            MessageBox.Show(this,
                "Blink test inviato. Controlla la lampada/relay.",
                "LcusRelay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Test blink failed.");
            MessageBox.Show(this,
                $"Errore blink: {ex.Message}",
                "LcusRelay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task SaveAndClose()
    {
        try
        {
            // Prima prova ad interpretare JSON (se l'utente l'ha modificato).
            var cfgFromJson = JsonSerializer.Deserialize<AppConfig>(_txtJson.Text, ConfigJson.Options);
            if (cfgFromJson is not null)
            {
                _cfg = cfgFromJson;
            }

            LoadFromControlsToConfig();

            ConfigStore.Save(_log, _cfg);
            await _onSave(_cfg);
            Close();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Save settings failed.");
            MessageBox.Show(this, $"Salvataggio fallito: {ex.Message}", "LcusRelay", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
