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

        if (e.Args.Any(arg =>
                string.Equals(
                    arg,
                    "--settings",
                    StringComparison.OrdinalIgnoreCase)))
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Show();

            settingsWindow.Closed += (_, _) => Shutdown();
        }
    }
}

