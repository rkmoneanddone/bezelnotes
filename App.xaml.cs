using System;
using System.Linq;
using System.Windows;
using StickyNotes.Storage;

namespace StickyNotes;

public partial class App : Application
{
    public static SQLiteNoteStore NoteStore { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await NoteStore.InitializeAsync();

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
}


