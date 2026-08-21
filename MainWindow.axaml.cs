using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ClassCodexDownloader;

public partial class MainWindow : Window
{
    private readonly TextBox _addonsPathTextBox;
    private readonly Button _runDownloaderButton;
    private readonly TextBox _logTextBox;

    private bool _isRunning;

    public MainWindow()
    {
        InitializeComponent();

        _addonsPathTextBox = this.FindControl<TextBox>("AddonsPathTextBox")
            ?? throw new InvalidOperationException("AddonsPathTextBox not found.");
        _runDownloaderButton = this.FindControl<Button>("RunDownloaderButton")
            ?? throw new InvalidOperationException("RunDownloaderButton not found.");
        _logTextBox = this.FindControl<TextBox>("LogTextBox")
            ?? throw new InvalidOperationException("LogTextBox not found.");

        LoadSettings();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void RunDownloaderButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_isRunning)
            {
                AppendErrorLine("An execution is already in progress. Please wait until it finishes.");
                return;
            }

            _isRunning = true;
            _runDownloaderButton.IsEnabled = false;
            SaveSettings();

            if (_logTextBox.Text?.Length > 0)
            {
                AppendLogLine(string.Empty);
                AppendLogLine("----------------------------------------");
            }

            try
            {
                await Downloader.RunAsync(_addonsPathTextBox.Text?.Trim() ?? string.Empty, AppendLogLine, AppendErrorLine);
            }
            catch (Exception ex)
            {
                AppendErrorLine($"Error while running downloader: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
                _runDownloaderButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            AppendErrorLine($"Error while running downloader: {ex.Message}");
        }
    }

    private void AddonsPathTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();
        _addonsPathTextBox.Text = string.IsNullOrWhiteSpace(settings.AddonsPath)
            ? SettingsStore.GetDefaultAddonsPath()
            : settings.AddonsPath;
    }

    private void SaveSettings()
    {
        SettingsStore.Save((_addonsPathTextBox.Text ?? string.Empty).Trim());
    }

    private void AppendLogLine(string message)
    {
        AppendToLog(message);
    }

    private void AppendErrorLine(string message)
    {
        AppendToLog(message);
    }

    private void AppendToLog(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _logTextBox.Text += message + Environment.NewLine;
            _logTextBox.CaretIndex = _logTextBox.Text?.Length ?? 0;
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _logTextBox.Text += message + Environment.NewLine;
            _logTextBox.CaretIndex = _logTextBox.Text?.Length ?? 0;
        });
    }
}
