using ClassCodexDownloader;

var hasErrors = false;

try
{
    var addonsPath = SettingsStore.ResolveAddonsPath();

    await Downloader.RunAsync(
        addonsPath,
        Console.WriteLine,
        message =>
        {
            hasErrors = true;
            Console.Error.WriteLine(message);
        });

    return hasErrors ? 1 : 0;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Fatal error while running downloader: {ex.Message}");
    return 2;
}