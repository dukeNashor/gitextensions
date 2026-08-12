using GitCommands;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

internal sealed partial class MultiRepositoryStatusSettingsPage : SettingsPageWithHeader
{
    private readonly CheckBox _autoFetchEnabled = new()
    {
        AutoSize = true,
        Text = "Automatically fetch repositories while the system is idle"
    };

    private readonly NumericUpDown _idleMinutes = CreateNumberControl(1, 1440);
    private readonly NumericUpDown _fetchIntervalMinutes = CreateNumberControl(1, 1440);
    private readonly NumericUpDown _concurrency = CreateNumberControl(1, 16);
    private readonly NumericUpDown _timeoutSeconds = CreateNumberControl(10, 3600);
    private readonly TableLayoutPanel _settingsTable = new();
    private readonly Label _concurrencyLabel = CreateSettingLabel("Maximum concurrent repositories");
    private readonly Label _explanation = new()
    {
        AutoSize = true,
        MaximumSize = new Size(700, 0),
        Text = "The overview checks categorised repositories and valid uncategorised repositories from recent history. Automatic and manual fetches access all remotes configured for every repository in the overview."
    };
    private readonly Label _fetchIntervalLabel = CreateSettingLabel("Repeat after this many minutes while idle");
    private readonly Label _idleMinutesLabel = CreateSettingLabel("Start after this many idle minutes");
    private readonly Label _timeoutLabel = CreateSettingLabel("Fetch timeout per repository in seconds");

    public MultiRepositoryStatusSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        Name = nameof(MultiRepositoryStatusSettingsPage);
        Text = "Repository status overview";
        AutoScroll = true;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        _settingsTable.AutoSize = true;
        _settingsTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _settingsTable.ColumnCount = 2;
        _settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _settingsTable.Dock = DockStyle.Top;
        _settingsTable.Padding = new Padding(0, 12, 0, 0);
        _settingsTable.Controls.Add(_autoFetchEnabled, 0, 0);
        _settingsTable.SetColumnSpan(_autoFetchEnabled, 2);
        AddSettingRow(1, _idleMinutesLabel, _idleMinutes);
        AddSettingRow(2, _fetchIntervalLabel, _fetchIntervalMinutes);
        AddSettingRow(3, _concurrencyLabel, _concurrency);
        AddSettingRow(4, _timeoutLabel, _timeoutSeconds);

        Controls.Add(_settingsTable);
        Controls.Add(_explanation);
        _autoFetchEnabled.CheckedChanged += (_, _) => UpdateEnabledState();
        InitializeComplete();
    }

    protected override void SettingsToPage()
    {
        _autoFetchEnabled.Checked = AppSettings.MultiRepositoryStatusAutoFetchEnabled;
        _idleMinutes.Value = Math.Clamp(AppSettings.MultiRepositoryStatusIdleMinutes, (int)_idleMinutes.Minimum, (int)_idleMinutes.Maximum);
        _fetchIntervalMinutes.Value = Math.Clamp(AppSettings.MultiRepositoryStatusFetchIntervalMinutes, (int)_fetchIntervalMinutes.Minimum, (int)_fetchIntervalMinutes.Maximum);
        _concurrency.Value = Math.Clamp(AppSettings.MultiRepositoryStatusFetchConcurrency, (int)_concurrency.Minimum, (int)_concurrency.Maximum);
        _timeoutSeconds.Value = Math.Clamp(AppSettings.MultiRepositoryStatusFetchTimeoutSeconds, (int)_timeoutSeconds.Minimum, (int)_timeoutSeconds.Maximum);
        UpdateEnabledState();
        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        AppSettings.MultiRepositoryStatusAutoFetchEnabled = _autoFetchEnabled.Checked;
        AppSettings.MultiRepositoryStatusIdleMinutes = (int)_idleMinutes.Value;
        AppSettings.MultiRepositoryStatusFetchIntervalMinutes = (int)_fetchIntervalMinutes.Value;
        AppSettings.MultiRepositoryStatusFetchConcurrency = (int)_concurrency.Value;
        AppSettings.MultiRepositoryStatusFetchTimeoutSeconds = (int)_timeoutSeconds.Value;
        base.PageToSettings();
    }

    private static NumericUpDown CreateNumberControl(decimal minimum, decimal maximum)
        => new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Width = 90,
            TextAlign = HorizontalAlignment.Right
        };

    private static Label CreateSettingLabel(string text)
        => new()
        {
            AutoSize = true,
            Margin = new Padding(24, 8, 12, 3),
            Text = text
        };

    private void AddSettingRow(int row, Label label, Control control)
    {
        _settingsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _settingsTable.Controls.Add(label, 0, row);
        control.Margin = new Padding(3, 4, 3, 3);
        _settingsTable.Controls.Add(control, 1, row);
    }

    private void UpdateEnabledState()
    {
        _idleMinutes.Enabled = _autoFetchEnabled.Checked;
        _fetchIntervalMinutes.Enabled = _autoFetchEnabled.Checked;
    }
}
