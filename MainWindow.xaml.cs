using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AdGroupUserExporter;

public partial class MainWindow : Window
{
    private const int MaxPatternHistoryItems = 25;
    private bool _isInitializingTheme;
    private ResultMode _resultMode = ResultMode.UserSearch;
    private static readonly string PatternHistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdGroupUserExporter",
        "group-pattern-history.json");
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdGroupUserExporter",
        "settings.json");

    private readonly ObservableCollection<AdUserResult> _results = [];
    private readonly ObservableCollection<GroupComparisonResult> _comparisonResults = [];
    private readonly ObservableCollection<string> _groupPatterns = [];
    private readonly ICollectionView _resultsView;
    private readonly ICollectionView _comparisonResultsView;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateComboBoxColors();
        GroupPatternComboBox.ItemsSource = _groupPatterns;
        LoadPatternHistory();
        LoadThemeSetting();
        _resultsView = CollectionViewSource.GetDefaultView(_results);
        _resultsView.Filter = FilterResult;
        ResultsGrid.ItemsSource = _resultsView;
        _comparisonResultsView = CollectionViewSource.GetDefaultView(_comparisonResults);
        _comparisonResultsView.Filter = FilterComparisonResult;
        ComparisonGrid.ItemsSource = _comparisonResultsView;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var groupPattern = GetCurrentGroupPattern();
        if (string.IsNullOrWhiteSpace(groupPattern))
        {
            MessageBox.Show(this, "Bitte ein Gruppenmuster angeben.", "Eingabe fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var searchBase = SearchBaseTextBox.Text.Trim();
        var server = ServerTextBox.Text.Trim();
        var onlyEnabled = OnlyEnabledCheckBox.IsChecked == true;

        SetBusy(true, "Suche laeuft...");

        try
        {
            var results = await Task.Run(() => RunPowerShellSearch(groupPattern, searchBase, server, onlyEnabled));
            AddPatternToHistory(groupPattern);
            _results.Clear();
            foreach (var result in results)
            {
                _results.Add(result);
            }

            SetResultMode(ResultMode.UserSearch);
            _resultsView.Refresh();
            UpdateActionButtons();
            StatusTextBlock.Text = $"{_results.Count} Benutzer geladen, {GetFilteredResults().Count} sichtbar.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "AD-Abfrage fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "Fehler bei der AD-Abfrage.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CompareUsersButton_Click(object sender, RoutedEventArgs e)
    {
        var userA = CompareUserATextBox.Text.Trim();
        var userB = CompareUserBTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userA) || string.IsNullOrWhiteSpace(userB))
        {
            MessageBox.Show(this, "Bitte beide Benutzer angeben.", "Eingabe fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var searchBase = SearchBaseTextBox.Text.Trim();
        var server = ServerTextBox.Text.Trim();

        SetBusy(true, "User-Gruppen werden verglichen...");

        try
        {
            var results = await Task.Run(() => RunPowerShellGroupComparison(userA, userB, searchBase, server));
            _comparisonResults.Clear();
            foreach (var result in results)
            {
                _comparisonResults.Add(result);
            }

            SetResultMode(ResultMode.GroupComparison);
            _comparisonResultsView.Refresh();
            UpdateActionButtons();
            var visibleCount = GetFilteredComparisonResults().Count;
            var sharedCount = _comparisonResults.Count(result => result.Status == "Beide");
            StatusTextBlock.Text = $"{_comparisonResults.Count} Gruppen verglichen, {sharedCount} gemeinsam, {visibleCount} sichtbar.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "User-Vergleich fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "Fehler beim User-Vergleich.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string GetCurrentGroupPattern()
    {
        return GroupPatternComboBox.Text.Trim();
    }

    private void LoadPatternHistory()
    {
        var patterns = ReadPatternHistory();
        if (patterns.Count == 0)
        {
            patterns.Add("abc*_1a*");
        }

        _groupPatterns.Clear();
        foreach (var pattern in patterns)
        {
            _groupPatterns.Add(pattern);
        }

        GroupPatternComboBox.Text = _groupPatterns.FirstOrDefault() ?? string.Empty;
    }

    private static List<string> ReadPatternHistory()
    {
        if (!File.Exists(PatternHistoryPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(PatternHistoryPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => pattern.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxPatternHistoryItems)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void AddPatternToHistory(string groupPattern)
    {
        RemovePatternFromHistory(groupPattern, save: false);
        _groupPatterns.Insert(0, groupPattern);

        while (_groupPatterns.Count > MaxPatternHistoryItems)
        {
            _groupPatterns.RemoveAt(_groupPatterns.Count - 1);
        }

        GroupPatternComboBox.Text = groupPattern;
        SavePatternHistory();
    }

    private void RemovePatternButton_Click(object sender, RoutedEventArgs e)
    {
        var groupPattern = GetCurrentGroupPattern();
        if (string.IsNullOrWhiteSpace(groupPattern))
        {
            return;
        }

        if (!RemovePatternFromHistory(groupPattern, save: true))
        {
            StatusTextBlock.Text = "Gruppenmuster war nicht im Verlauf.";
            return;
        }

        GroupPatternComboBox.Text = _groupPatterns.FirstOrDefault() ?? string.Empty;
        StatusTextBlock.Text = "Gruppenmuster aus dem Verlauf entfernt.";
    }

    private bool RemovePatternFromHistory(string groupPattern, bool save)
    {
        var existingPattern = _groupPatterns.FirstOrDefault(pattern =>
            string.Equals(pattern, groupPattern, StringComparison.CurrentCultureIgnoreCase));

        if (existingPattern is null)
        {
            return false;
        }

        _groupPatterns.Remove(existingPattern);
        if (save)
        {
            SavePatternHistory();
        }

        return true;
    }

    private void SavePatternHistory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PatternHistoryPath)!);
        var json = JsonSerializer.Serialize(_groupPatterns.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PatternHistoryPath, json, Encoding.UTF8);
    }

    private void LoadThemeSetting()
    {
        _isInitializingTheme = true;

        var theme = ReadThemeSetting();
        ApplyTheme(theme);
        UpdateComboBoxColors();
        foreach (var item in ThemeComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), theme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        _isInitializingTheme = false;
    }

    private static string ReadThemeSetting()
    {
        if (!File.Exists(SettingsPath))
        {
            return "Light";
        }

        try
        {
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings?.Theme is "Dark" ? "Dark" : "Light";
        }
        catch
        {
            return "Light";
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializingTheme || ThemeComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        var theme = item.Tag?.ToString() == "Dark" ? "Dark" : "Light";
        ApplyTheme(theme);
        UpdateComboBoxColors();
        SaveThemeSetting(theme);
    }

    private void ApplyTheme(string theme)
    {
        if (theme == "Dark")
        {
            SetThemeResources(
                windowBackground: "#1F2328",
                panelBackground: "#252A31",
                inputBackground: "#15191F",
                alternateRowBackground: "#20262D",
                headerBackground: "#30363D",
                buttonBackground: "#30363D",
                disabledBackground: "#252A31",
                border: "#57606A",
                text: "#E6EDF3",
                mutedText: "#8B949E",
                selectionBackground: "#1F6FEB",
                selectionText: "#FFFFFF");
            return;
        }

        SetThemeResources(
            windowBackground: "#F6F8FA",
            panelBackground: "#FFFFFF",
            inputBackground: "#FFFFFF",
            alternateRowBackground: "#F6F8FA",
            headerBackground: "#F6F8FA",
            buttonBackground: "#F6F8FA",
            disabledBackground: "#F6F8FA",
            border: "#D0D7DE",
            text: "#24292F",
            mutedText: "#6E7781",
            selectionBackground: "#0969DA",
            selectionText: "#FFFFFF");
    }

    private void SetThemeResources(
        string windowBackground,
        string panelBackground,
        string inputBackground,
        string alternateRowBackground,
        string headerBackground,
        string buttonBackground,
        string disabledBackground,
        string border,
        string text,
        string mutedText,
        string selectionBackground,
        string selectionText)
    {
        var windowBackgroundBrush = BrushFromHex(windowBackground);
        var panelBackgroundBrush = BrushFromHex(panelBackground);
        var inputBackgroundBrush = BrushFromHex(inputBackground);
        var alternateRowBackgroundBrush = BrushFromHex(alternateRowBackground);
        var headerBackgroundBrush = BrushFromHex(headerBackground);
        var buttonBackgroundBrush = BrushFromHex(buttonBackground);
        var disabledBackgroundBrush = BrushFromHex(disabledBackground);
        var borderBrush = BrushFromHex(border);
        var textBrush = BrushFromHex(text);
        var mutedTextBrush = BrushFromHex(mutedText);
        var selectionBackgroundBrush = BrushFromHex(selectionBackground);
        var selectionTextBrush = BrushFromHex(selectionText);

        Resources["WindowBackgroundBrush"] = windowBackgroundBrush;
        Resources["PanelBackgroundBrush"] = panelBackgroundBrush;
        Resources["InputBackgroundBrush"] = inputBackgroundBrush;
        Resources["AlternateRowBackgroundBrush"] = alternateRowBackgroundBrush;
        Resources["HeaderBackgroundBrush"] = headerBackgroundBrush;
        Resources["ButtonBackgroundBrush"] = buttonBackgroundBrush;
        Resources["DisabledBackgroundBrush"] = disabledBackgroundBrush;
        Resources["BorderBrush"] = borderBrush;
        Resources["TextBrush"] = textBrush;
        Resources["MutedTextBrush"] = mutedTextBrush;
        Resources["SelectionBackgroundBrush"] = selectionBackgroundBrush;
        Resources["SelectionTextBrush"] = selectionTextBrush;

        Resources[SystemColors.WindowBrushKey] = inputBackgroundBrush;
        Resources[SystemColors.ControlBrushKey] = buttonBackgroundBrush;
        Resources[SystemColors.ControlDarkBrushKey] = borderBrush;
        Resources[SystemColors.ControlLightBrushKey] = panelBackgroundBrush;
        Resources[SystemColors.ControlTextBrushKey] = textBrush;
        Resources[SystemColors.GrayTextBrushKey] = mutedTextBrush;
        Resources[SystemColors.HighlightBrushKey] = selectionBackgroundBrush;
        Resources[SystemColors.HighlightTextBrushKey] = selectionTextBrush;
    }

    private void UpdateComboBoxColors()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyComboBoxColors(GroupPatternComboBox);
            ApplyComboBoxColors(ThemeComboBox);
        }, DispatcherPriority.Loaded);
    }

    private void ApplyComboBoxColors(System.Windows.Controls.ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        comboBox.Background = (Brush)Resources["InputBackgroundBrush"];
        comboBox.Foreground = (Brush)Resources["TextBrush"];
        comboBox.BorderBrush = (Brush)Resources["BorderBrush"];

        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is System.Windows.Controls.TextBox editableTextBox)
        {
            editableTextBox.Background = (Brush)Resources["InputBackgroundBrush"];
            editableTextBox.Foreground = (Brush)Resources["TextBrush"];
            editableTextBox.BorderBrush = (Brush)Resources["BorderBrush"];
            editableTextBox.CaretBrush = (Brush)Resources["TextBrush"];
        }

        ApplyComboBoxVisualTreeColors(comboBox);
    }

    private void ApplyComboBoxVisualTreeColors(DependencyObject parent)
    {
        var inputBackground = (Brush)Resources["InputBackgroundBrush"];
        var buttonBackground = (Brush)Resources["ButtonBackgroundBrush"];
        var border = (Brush)Resources["BorderBrush"];
        var text = (Brush)Resources["TextBrush"];

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            switch (child)
            {
                case System.Windows.Controls.Border borderElement:
                    borderElement.Background = inputBackground;
                    borderElement.BorderBrush = border;
                    break;
                case ToggleButton toggleButton:
                    toggleButton.Background = buttonBackground;
                    toggleButton.Foreground = text;
                    toggleButton.BorderBrush = border;
                    break;
                case System.Windows.Controls.TextBox textBox:
                    textBox.Background = inputBackground;
                    textBox.Foreground = text;
                    textBox.BorderBrush = border;
                    textBox.CaretBrush = text;
                    break;
                case System.Windows.Controls.TextBlock textBlock:
                    textBlock.Foreground = text;
                    break;
            }

            ApplyComboBoxVisualTreeColors(child);
        }
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static void SaveThemeSetting(string theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(new AppSettings { Theme = theme }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    private List<AdUserResult> RunPowerShellSearch(string groupPattern, string searchBase, string server, bool onlyEnabled)
    {
        return RunPowerShell<List<AdUserResult>>(startInfo =>
        {
            startInfo.ArgumentList.Add("-GroupPattern");
            startInfo.ArgumentList.Add(groupPattern);

            AddOptionalArgument(startInfo, "-SearchBase", searchBase);
            AddOptionalArgument(startInfo, "-Server", server);

            if (onlyEnabled)
            {
                startInfo.ArgumentList.Add("-OnlyEnabled");
            }
        }) ?? [];
    }

    private List<GroupComparisonResult> RunPowerShellGroupComparison(string userA, string userB, string searchBase, string server)
    {
        return RunPowerShell<List<GroupComparisonResult>>(startInfo =>
        {
            startInfo.ArgumentList.Add("-CompareUserA");
            startInfo.ArgumentList.Add(userA);
            startInfo.ArgumentList.Add("-CompareUserB");
            startInfo.ArgumentList.Add(userB);

            AddOptionalArgument(startInfo, "-SearchBase", searchBase);
            AddOptionalArgument(startInfo, "-Server", server);
        }) ?? [];
    }

    private T? RunPowerShell<T>(Action<ProcessStartInfo> addArguments)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "Get-AdUsersFromGroupPattern.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("PowerShell-Script wurde nicht gefunden.", scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = FindPowerShellExecutable(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        addArguments(startInfo);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell konnte nicht gestartet werden.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "PowerShell wurde mit Fehler beendet." : error.Trim());
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static string FindPowerShellExecutable()
    {
        var systemPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
    }

    private static void AddOptionalArgument(ProcessStartInfo startInfo, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value.Trim());
    }

    private void FilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_resultMode == ResultMode.GroupComparison)
        {
            _comparisonResultsView.Refresh();
            StatusTextBlock.Text = $"{_comparisonResults.Count} Gruppen verglichen, {GetFilteredComparisonResults().Count} sichtbar.";
        }
        else
        {
            _resultsView.Refresh();
            StatusTextBlock.Text = $"{_results.Count} Benutzer geladen, {GetFilteredResults().Count} sichtbar.";
        }

        UpdateActionButtons();
    }

    private bool FilterResult(object item)
    {
        if (item is not AdUserResult result)
        {
            return false;
        }

        var filter = FilterTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(filter) || result.Contains(filter);
    }

    private bool FilterComparisonResult(object item)
    {
        if (item is not GroupComparisonResult result)
        {
            return false;
        }

        var filter = FilterTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(filter) || result.Contains(filter);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resultMode == ResultMode.GroupComparison)
        {
            var filteredComparisonResults = GetFilteredComparisonResults();
            if (filteredComparisonResults.Count == 0)
            {
                return;
            }

            var comparisonGroupNames = GetVisibleGroupNames(filteredComparisonResults);
            Clipboard.SetText(string.Join(Environment.NewLine, comparisonGroupNames));
            StatusTextBlock.Text = $"{comparisonGroupNames.Count} sichtbare GroupName-Werte in die Zwischenablage kopiert.";
            return;
        }

        var filteredResults = GetFilteredResults();
        if (filteredResults.Count == 0)
        {
            return;
        }

        var groupNames = GetVisibleGroupNames(filteredResults);
        Clipboard.SetText(string.Join(Environment.NewLine, groupNames));
        StatusTextBlock.Text = $"{groupNames.Count} sichtbare GroupName-Werte in die Zwischenablage kopiert.";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resultMode == ResultMode.GroupComparison)
        {
            var filteredComparisonResults = GetFilteredComparisonResults();
            if (filteredComparisonResults.Count == 0)
            {
                return;
            }

            var comparisonDialog = CreateCsvSaveFileDialog("ad-user-group-comparison");
            if (comparisonDialog.ShowDialog(this) != true)
            {
                return;
            }

            File.WriteAllText(comparisonDialog.FileName, ToDelimitedText(GroupComparisonResult.Headers, filteredComparisonResults.Select(result => result.ToFields()), ";"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusTextBlock.Text = $"{filteredComparisonResults.Count} sichtbare Gruppen nach CSV exportiert.";
            return;
        }

        var filteredResults = GetFilteredResults();
        if (filteredResults.Count == 0)
        {
            return;
        }

        var dialog = CreateCsvSaveFileDialog("ad-users");

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, ToDelimitedText(AdUserResult.Headers, filteredResults.Select(result => result.ToFields()), ";"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        StatusTextBlock.Text = $"{filteredResults.Count} sichtbare Benutzer nach CSV exportiert.";
    }

    private static SaveFileDialog CreateCsvSaveFileDialog(string filePrefix)
    {
        return new SaveFileDialog
        {
            Filter = "CSV-Dateien (*.csv)|*.csv|Alle Dateien (*.*)|*.*",
            FileName = $"{filePrefix}-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
    }

    private List<AdUserResult> GetFilteredResults()
    {
        return _resultsView.Cast<AdUserResult>().ToList();
    }

    private List<GroupComparisonResult> GetFilteredComparisonResults()
    {
        return _comparisonResultsView.Cast<GroupComparisonResult>().ToList();
    }

    private static List<string> GetVisibleGroupNames(IEnumerable<AdUserResult> results)
    {
        return results
            .Select(result => result.GroupName)
            .Where(groupName => !string.IsNullOrWhiteSpace(groupName))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(groupName => groupName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<string> GetVisibleGroupNames(IEnumerable<GroupComparisonResult> results)
    {
        return results
            .Select(result => result.GroupName)
            .Where(groupName => !string.IsNullOrWhiteSpace(groupName))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(groupName => groupName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ToDelimitedText(IEnumerable<string> headers, IEnumerable<string[]> rows, string delimiter)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(delimiter, headers.Select(value => Escape(value, delimiter))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(delimiter, row.Select(value => Escape(value, delimiter))));
        }

        return builder.ToString();
    }

    private static string Escape(string? value, string delimiter)
    {
        value ??= string.Empty;
        var mustQuote = value.Contains('"') || value.Contains('\r') || value.Contains('\n') || value.Contains(delimiter);
        var escaped = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{escaped}\"" : escaped;
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        SearchButton.IsEnabled = !isBusy;
        CompareUsersButton.IsEnabled = !isBusy;
        var hasVisibleRows = HasVisibleRows();
        CopyButton.IsEnabled = !isBusy && hasVisibleRows;
        ExportButton.IsEnabled = !isBusy && hasVisibleRows;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusTextBlock.Text = status;
        }
    }

    private void UpdateActionButtons()
    {
        var hasVisibleRows = HasVisibleRows();
        CopyButton.IsEnabled = hasVisibleRows;
        ExportButton.IsEnabled = hasVisibleRows;
    }

    private bool HasVisibleRows()
    {
        return _resultMode == ResultMode.GroupComparison
            ? GetFilteredComparisonResults().Count > 0
            : GetFilteredResults().Count > 0;
    }

    private void SetResultMode(ResultMode resultMode)
    {
        _resultMode = resultMode;
        ResultsGrid.Visibility = resultMode == ResultMode.UserSearch ? Visibility.Visible : Visibility.Collapsed;
        ComparisonGrid.Visibility = resultMode == ResultMode.GroupComparison ? Visibility.Visible : Visibility.Collapsed;
    }

    private enum ResultMode
    {
        UserSearch,
        GroupComparison
    }

    private sealed class AppSettings
    {
        public string Theme { get; set; } = "Light";
    }
}
