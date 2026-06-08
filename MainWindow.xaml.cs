using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;

namespace AdGroupUserExporter;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AdUserResult> _results = [];
    private readonly ICollectionView _resultsView;

    public MainWindow()
    {
        InitializeComponent();
        _resultsView = CollectionViewSource.GetDefaultView(_results);
        _resultsView.Filter = FilterResult;
        ResultsGrid.ItemsSource = _resultsView;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var groupPattern = GroupPatternTextBox.Text.Trim();
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
            _results.Clear();
            foreach (var result in results)
            {
                _results.Add(result);
            }

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

    private List<AdUserResult> RunPowerShellSearch(string groupPattern, string searchBase, string server, bool onlyEnabled)
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
        startInfo.ArgumentList.Add("-GroupPattern");
        startInfo.ArgumentList.Add(groupPattern);

        AddOptionalArgument(startInfo, "-SearchBase", searchBase);
        AddOptionalArgument(startInfo, "-Server", server);

        if (onlyEnabled)
        {
            startInfo.ArgumentList.Add("-OnlyEnabled");
        }

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
            return [];
        }

        return JsonSerializer.Deserialize<List<AdUserResult>>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
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
        _resultsView.Refresh();
        UpdateActionButtons();
        StatusTextBlock.Text = $"{_results.Count} Benutzer geladen, {GetFilteredResults().Count} sichtbar.";
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

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var filteredResults = GetFilteredResults();
        if (filteredResults.Count == 0)
        {
            return;
        }

        Clipboard.SetText(ToDelimitedText(filteredResults, "\t"));
        StatusTextBlock.Text = $"{filteredResults.Count} sichtbare Benutzer in die Zwischenablage kopiert.";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var filteredResults = GetFilteredResults();
        if (filteredResults.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV-Dateien (*.csv)|*.csv|Alle Dateien (*.*)|*.*",
            FileName = $"ad-users-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, ToDelimitedText(filteredResults, ";"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        StatusTextBlock.Text = $"{filteredResults.Count} sichtbare Benutzer nach CSV exportiert.";
    }

    private List<AdUserResult> GetFilteredResults()
    {
        return _resultsView.Cast<AdUserResult>().ToList();
    }

    private static string ToDelimitedText(IReadOnlyCollection<AdUserResult> results, string delimiter)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(delimiter, AdUserResult.Headers.Select(value => Escape(value, delimiter))));

        foreach (var result in results)
        {
            builder.AppendLine(string.Join(delimiter, result.ToFields().Select(value => Escape(value, delimiter))));
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
        CopyButton.IsEnabled = !isBusy && GetFilteredResults().Count > 0;
        ExportButton.IsEnabled = !isBusy && GetFilteredResults().Count > 0;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusTextBlock.Text = status;
        }
    }

    private void UpdateActionButtons()
    {
        var hasVisibleRows = GetFilteredResults().Count > 0;
        CopyButton.IsEnabled = hasVisibleRows;
        ExportButton.IsEnabled = hasVisibleRows;
    }
}
