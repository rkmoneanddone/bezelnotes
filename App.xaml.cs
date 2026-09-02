using System;
using System.Linq;
using System.Windows;
using System.Windows.Shell;
using StickyNotes.Storage;

namespace StickyNotes;

public partial class App : Application
{
    public static SQLiteNoteStore NoteStore { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await NoteStore.InitializeAsync();
        ConfigureJumpList();

        bool settingsOnly = e.Args.Any(arg =>
            string.Equals(
                arg,
                "--settings",
                StringComparison.OrdinalIgnoreCase));

        if (settingsOnly)
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            var settingsWindow = new SettingsWindow();
            MainWindow = settingsWindow;
            settingsWindow.Show();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
    private static void ConfigureJumpList()
    {
        try
        {
            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            var jumpList = new JumpList
            {
                ShowRecentCategory = false,
                ShowFrequentCategory = false
            };

            jumpList.JumpItems.Add(new JumpTask
            {
                Title = "Settings",
                Description = "Open BezelStickNotes Settings",
                ApplicationPath = executablePath,
                Arguments = "--settings",
                IconResourcePath = executablePath
            });

            JumpList.SetJumpList(Current, jumpList);
            jumpList.Apply();
        }
        catch
        {
            // Jump Lists are a convenience only; app startup must never fail.
        }
    }
}



