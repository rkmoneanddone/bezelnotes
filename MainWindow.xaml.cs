using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StickyNotes.Core.Models;

namespace StickyNotes;

public partial class MainWindow : Window
{
    private bool _fanScrollActive;
    private int _fanScrollVersion;

    private enum DeckState
    {
        Rest,
        Fan,
        Open
    }

    private readonly ObservableCollection<Note> _notes = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _fanHoverTimer;

    private Note? _currentNote;
    private bool _loadingEditor;
    private DeckState _state = DeckState.Rest;
    private bool _fanTransitionInProgress;
    private int _previewVersion;
    private object? _previewedNote;
    private bool _previewPopupMouseOver;
    private bool _fanPreviewReady;
    private int _previewIntentVersion;
    private FrameworkElement? _previewIntentTab;
    private bool _previewExitPending;
    private FrameworkElement? _previewedTab;

    private const double RestWidth = 14;
    private const double FanWidth = 60;
    private const double OpenWidth = 420;

    public MainWindow()
    {
        InitializeComponent();

        RestDashList.ItemsSource = _notes;
        FanNotesList.ItemsSource = _notes;
        OpenDeckList.ItemsSource = _notes;

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _saveTimer.Tick += SaveTimer_Tick;

        _collapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(560)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;

        _fanHoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _fanHoverTimer.Tick += FanHoverTimer_Tick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadNotesAsync();
        MoveToRestState(initial: true);
    }

    private async Task LoadNotesAsync()
    {
        _notes.Clear();

        var notes = await App.NoteStore.GetNotesAsync();

        foreach (var note in notes.Take(8))
        {
            _notes.Add(note);
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();

        if (_state != DeckState.Rest)
        {
            return;
        }

        if (_fanTransitionInProgress)
        {
            return;
        }

        _fanHoverTimer.Stop();
        _fanHoverTimer.Start();
    }

    private void FanHoverTimer_Tick(object? sender, EventArgs e)
    {
        _fanHoverTimer.Stop();

        if (_state != DeckState.Rest || _fanTransitionInProgress)
        {
            return;
        }

        if (!IsMouseOver)
        {
            return;
        }

        _fanTransitionInProgress = true;
        MoveToFanState();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _fanHoverTimer.Stop();

        // Point 2 stability:
        // never collapse the fan from the window-level MouseLeave event.
        // The window width changes when fan opens, which can fire MouseLeave even
        // while the cursor is still visually over the deck.
    }

    private void FanDeck_MouseEnter(object sender, MouseEventArgs e)
    {
        _previewExitPending = false;
        _collapseTimer.Stop();
    }

    private void FanDeck_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_state != DeckState.Fan)
        {
            return;
        }

        // IMPORTANT:
        // Opening a preview hides the active tab, which can fire MouseLeave
        // even when the physical mouse has not moved. Never collapse from
        // FanDeck_MouseLeave while a preview is open.
        if (PreviewPopup.IsOpen)
        {
            return;
        }

        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(700);
        _collapseTimer.Start();
    }
    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();

        if (_state != DeckState.Fan)
        {
            return;
        }

        if (FanDeck.IsMouseOver ||
            _previewPopupMouseOver ||
            (PreviewPopup.IsOpen && PreviewCard.IsMouseOver))
        {
            _previewExitPending = false;
            return;
        }

        if (PreviewPopup.IsOpen)
        {
            // Never close an open preview merely because hiding its tab caused
            // a synthetic FanDeck/Tab MouseLeave.
            if (!_previewExitPending)
            {
                return;
            }

            _previewExitPending = false;

            // Stage 1: close only preview, keep fan open.
            ClosePreviewImmediately();

            _collapseTimer.Interval = TimeSpan.FromMilliseconds(700);
            _collapseTimer.Start();
            return;
        }

        // Stage 2: fan goes back to rest only after pointer remains outside.
        if (!FanDeck.IsMouseOver)
        {
            MoveToRestState();
        }
    }

    private void PositionWindowForWidth(double width)
    {
        var work = SystemParameters.WorkArea;

        Width = width;
        Left = work.Right - width;
        Top = work.Top + Math.Max(20, (work.Height - Height) / 2);
    }

    private void MoveToRestState(bool initial = false)
    {
        _fanPreviewReady = false;
        _previewIntentVersion++;
        _previewIntentTab = null;
        ResetAddButtonPreviewOffset();
        ClosePreviewImmediately();
        _fanTransitionInProgress = false;
        _collapseTimer.Stop();

        _state = DeckState.Rest;

        OpenState.Visibility = Visibility.Collapsed;
        FanDeck.Visibility = Visibility.Collapsed;

        RestPill.Visibility = Visibility.Visible;
        RestPill.Opacity = 1;

        PositionWindowForWidth(RestWidth);

        if (!initial)
        {
            RestPill.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(90))
            );
        }
    }

    private void MoveToFanState()
    {
        _fanPreviewReady = false;
        _previewIntentVersion++;
        _previewIntentTab = null;
        _fanTransitionInProgress = true;
        _state = DeckState.Fan;

        RestPill.Visibility = Visibility.Collapsed;
        OpenState.Visibility = Visibility.Collapsed;

        PositionWindowForWidth(FanWidth);

        FanDeck.Visibility = Visibility.Visible;
        FanDeck.Opacity = 1;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(AnimateFanDeck)
        );
            _ = EnablePreviewAfterFanSettlesAsync();
}

    private async void CompleteFanTransitionAsync()
    {
        var itemCount = Math.Max(1, FanNotesList.Items.Count);
        var settleMs = 680 + ((itemCount - 1) * 170) + 120;

        await Task.Delay(settleMs);

        if (_state != DeckState.Fan)
        {
            return;
        }

        _fanTransitionInProgress = false;
        _fanPreviewReady = true;

        // The cursor may already be stationary over a tab after the fan settles.
        // Detect the actual final tab under the pointer instead of relying on
        // MouseEnter events fired while tabs were still sliding.
        for (int i = 0; i < FanNotesList.Items.Count; i++)
        {
            if (FanNotesList.ItemContainerGenerator.ContainerFromIndex(i)
                is FrameworkElement container &&
                container.IsMouseOver)
            {
                BeginPreviewIntent(container);
                break;
            }
        }
    }
    private async Task EnablePreviewAfterFanSettlesAsync()
    {
        var itemCount = Math.Max(1, FanNotesList.Items.Count);

        // Current locked fan motion:
        // 680ms per tab + 170ms stagger.
        var settleMs = 680 + ((itemCount - 1) * 170) + 150;

        await Task.Delay(settleMs);

        if (_state != DeckState.Fan)
        {
            return;
        }

        _fanTransitionInProgress = false;
        _fanPreviewReady = true;

        // If the cursor is already sitting over a final, settled tab,
        // start its normal 260ms preview-intent countdown.
        await Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < FanNotesList.Items.Count; i++)
            {
                var container =
                    FanNotesList.ItemContainerGenerator.ContainerFromIndex(i)
                    as FrameworkElement;

                if (container is null || !container.IsMouseOver)
                {
                    continue;
                }

                BeginPreviewIntent(container);
                break;
            }
        });
    }
    private void AnimateFanDeck()
    {
        var items = new List<ContentPresenter>();

        for (int i = 0; i < FanNotesList.Items.Count; i++)
        {
            if (FanNotesList.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter item)
            {
                items.Add(item);
            }
        }

        // Force visual order from top to bottom.
        items = items
            .OrderBy(item => item.TranslatePoint(new Point(0, 0), FanDeck).Y)
            .ToList();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            var transform = new TranslateTransform(20, 0);
            item.RenderTransform = transform;
            item.Opacity = 0;

            var delay = TimeSpan.FromMilliseconds(i * 170);

            var slide = new DoubleAnimation
            {
                From = 20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(680),
                BeginTime = delay,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(520),
                BeginTime = delay
            };

            transform.BeginAnimation(TranslateTransform.XProperty, slide);
            item.BeginAnimation(OpacityProperty, fade);
        }
    }
    private CustomPopupPlacement[] PreviewPopup_CustomPopupPlacement(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        return new[]
        {
            new CustomPopupPlacement(
                new Point(targetSize.Width - popupSize.Width, 0),
                PopupPrimaryAxis.Horizontal)
        };
    }
        private void FanNotesScrollViewer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        _fanScrollActive = true;
        int version = ++_fanScrollVersion;

        // Cancel any hover intent that started before/during this wheel step.
        _previewIntentVersion++;

        // A preview should never stay open while the note stack is moving.
        if (PreviewPopup.IsOpen)
        {
            ClosePreviewImmediately();
        }

        _ = ResumePreviewAfterFanScrollAsync(version);
    }

    private async Task ResumePreviewAfterFanScrollAsync(int version)
    {
        // Debounce consecutive wheel events. Preview stays disabled until
        // the wheel has been quiet for this period.
        await Task.Delay(360);

        if (version != _fanScrollVersion)
        {
            return;
        }

        _fanScrollActive = false;

        // If the mouse is now stationary over a note, start the normal
        // hover-intent timer. The existing BeginPreviewIntent delay still
        // applies, so scrolling can never immediately pop a preview.
        FrameworkElement? hoveredTab = FindHoveredFanTab();

        if (hoveredTab is not null)
        {
            BeginPreviewIntent(hoveredTab);
        }
    }

    private FrameworkElement? FindHoveredFanTab()
    {
        DependencyObject? current = Mouse.DirectlyOver as DependencyObject;

        while (current is not null)
        {
            if (current is FrameworkElement element &&
                element.DataContext is Core.Models.Note &&
                element.IsMouseOver)
            {
                // Ensure this visual actually belongs to FanNotesList.
                DependencyObject? ancestor = current;

                while (ancestor is not null)
                {
                    if (ReferenceEquals(ancestor, FanNotesList))
                    {
                        return element;
                    }

                    ancestor = VisualTreeHelper.GetParent(ancestor);
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

private void FanNote_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_fanScrollActive)
        {
            return;
        }
        if (_state != DeckState.Fan || !_fanPreviewReady)
        {
            return;
        }

        _collapseTimer.Stop();

        if (sender is FrameworkElement tab)
        {
            BeginPreviewIntent(tab);
        }
    }

    private void FanNote_MouseLeave(object sender, MouseEventArgs e)
    {
        // Once this tab owns an open preview, hiding the tab naturally causes
        // MouseLeave. That is not a real user exit and must be ignored.
        if (PreviewPopup.IsOpen &&
            ReferenceEquals(_previewedTab, sender))
        {
            return;
        }

        if (ReferenceEquals(_previewIntentTab, sender))
        {
            _previewIntentVersion++;
            _previewIntentTab = null;
        }
    }

    private void BeginPreviewIntent(FrameworkElement tab)
    {
        if (_fanScrollActive)
        {
            return;
        }
        if (_state != DeckState.Fan || !_fanPreviewReady || tab.DataContext is null)
        {
            return;
        }

        // If this tab already owns the preview, leave everything untouched.
        if (PreviewPopup.IsOpen &&
            ReferenceEquals(_previewedTab, tab) &&
            ReferenceEquals(_previewedNote, tab.DataContext))
        {
            return;
        }

        _previewIntentTab = tab;
        var intentVersion = ++_previewIntentVersion;

        _ = OpenPreviewAfterIntentAsync(tab, intentVersion);
    }

    private async Task OpenPreviewAfterIntentAsync(
        FrameworkElement tab,
        int intentVersion)
    {
        // Gives the user a visible "fan first, preview second" pause.
        await Task.Delay(260);

        if (intentVersion != _previewIntentVersion ||
            _state != DeckState.Fan ||
            !_fanPreviewReady ||
            !tab.IsMouseOver ||
            tab.DataContext is null)
        {
            return;
        }

        await OpenPreviewForTabAsync(tab, intentVersion);
    }

    private async Task OpenPreviewForTabAsync(
        FrameworkElement tab,
        int intentVersion)
    {
        var note = tab.DataContext;

        if (note is null)
        {
            return;
        }

        // Fade/slide only the previous preview. Never collapse the fan.
        if (PreviewPopup.IsOpen)
        {
            await AnimatePreviewOutAsync();

            if (intentVersion != _previewIntentVersion ||
                _state != DeckState.Fan ||
                !tab.IsMouseOver)
            {
                return;
            }

            if (_previewedTab is not null)
            {
                SetPreviewTabHidden(_previewedTab, false);
                _previewedTab = null;
            }

            PreviewPopup.IsOpen = false;
        }

        if (intentVersion != _previewIntentVersion ||
            !tab.IsMouseOver)
        {
            return;
        }

        _previewedTab = tab;
        _previewedNote = note;

        PreviewCard.DataContext = note;
        PreviewPopup.PlacementTarget = tab;
        PreviewPopup.CustomPopupPlacementCallback =
            PreviewPopup_CustomPopupPlacement;

        // Only the selected tab disappears; all other tabs stay closed.
        SetPreviewTabHidden(tab, true);

        UpdateAddButtonPreviewOffset(note);

        _previewExitPending = false;
        PreviewPopup.IsOpen = true;
        UpdatePreviewAddButtonVisibility(note);

        await Dispatcher.InvokeAsync(
            () => { },
            DispatcherPriority.Loaded);

        if (intentVersion != _previewIntentVersion)
        {
            return;
        }

        AnimatePreviewIn();
    }

    private void UpdateAddButtonPreviewOffset(object note)
    {
        ResetAddButtonPreviewOffset();

        if (FanNotesList.Items.Count == 0)
        {
            return;
        }

        var lastNote = FanNotesList.Items[FanNotesList.Items.Count - 1];

        if (ReferenceEquals(lastNote, note))
        {
            // Popup is taller than a closed tab. Move the existing + just below
            // its lower edge so it remains visible and clickable.
            AddButton.RenderTransform = new TranslateTransform(0, 48);
        }
    }

    private void ResetAddButtonPreviewOffset()
    {
        if (AddButton is null)
        {
            return;
        }

        AddButton.RenderTransform = Transform.Identity;
    }
    private void SetPreviewTabHidden(FrameworkElement tab, bool hidden)
    {
        // Fan-opening animations can continue to hold Opacity/transform
        // values even after they visually finish. Remove those clocks first
        // so the preview state owns the final visibility.
        tab.BeginAnimation(OpacityProperty, null);

        if (tab.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);

            if (!hidden)
            {
                translate.X = 0;
            }
        }

        tab.Opacity = hidden ? 0 : 1;
        tab.IsHitTestVisible = !hidden;
    }
        private void UpdatePreviewAddButtonVisibility(object note)
    {
        if (FanNotesList.Items.Count == 0)
        {
            PreviewAddButton.Visibility = Visibility.Collapsed;
            return;
        }

        var lastItem = FanNotesList.Items[FanNotesList.Items.Count - 1];

        PreviewAddButton.Visibility =
            ReferenceEquals(lastItem, note)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

private void AnimatePreviewIn()
    {
        PreviewCard.BeginAnimation(OpacityProperty, null);

        if (PreviewCard.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            PreviewCard.RenderTransform = transform;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);

        PreviewCard.Opacity = 0;
        transform.X = 14;

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        var slide = new DoubleAnimation
        {
            From = 14,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(330),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        PreviewCard.BeginAnimation(OpacityProperty, fade);
        transform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private async Task AnimatePreviewOutAsync()
    {
        if (!PreviewPopup.IsOpen)
        {
            return;
        }

        if (PreviewCard.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            PreviewCard.RenderTransform = transform;
        }

        var fade = new DoubleAnimation
        {
            From = PreviewCard.Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };

        var slide = new DoubleAnimation
        {
            From = transform.X,
            To = 8,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };

        PreviewCard.BeginAnimation(OpacityProperty, fade);
        transform.BeginAnimation(TranslateTransform.XProperty, slide);

        await Task.Delay(175);
    }

    private void PreviewPopup_MouseEnter(object sender, MouseEventArgs e)
    {
        _previewPopupMouseOver = true;
        _previewExitPending = false;
        _collapseTimer.Stop();
    }

    private void PreviewPopup_MouseLeave(object sender, MouseEventArgs e)
    {
        _previewPopupMouseOver = false;

        if (_state != DeckState.Fan)
        {
            return;
        }

        _previewExitPending = true;

        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(700);
        _collapseTimer.Start();
    }

        

private void ClosePreviewImmediately()
    {
        PreviewAddButton.Visibility = Visibility.Collapsed;
        _previewExitPending = false;
        _previewIntentVersion++;
        _previewIntentTab = null;
        ResetAddButtonPreviewOffset();
        _previewVersion++;

        if (_previewedTab is not null)
        {
            SetPreviewTabHidden(_previewedTab, false);
            _previewedTab = null;
        }

        _previewedNote = null;
        _previewPopupMouseOver = false;

        if (PreviewPopup.IsOpen)
        {
            PreviewPopup.IsOpen = false;
        }

        PreviewCard.BeginAnimation(OpacityProperty, null);

        if (PreviewCard.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 14;
        }

        PreviewCard.Opacity = 0;
    }
    private void MoveToOpenState(Note note)
    {
        _collapseTimer.Stop();
        _saveTimer.Stop();

        _state = DeckState.Open;
        _currentNote = note;

        _loadingEditor = true;
        TitleBox.Text = note.Title;
        ContentBox.Text = note.Content;
        SaveStatusText.Text = string.Empty;
        _loadingEditor = false;

        OpenNoteCard.DataContext = note;

        RestPill.Visibility = Visibility.Collapsed;
        FanDeck.Visibility = Visibility.Collapsed;

        PositionWindowForWidth(OpenWidth);

        OpenState.Visibility = Visibility.Visible;
        OpenState.Opacity = 1;

        var cardTransform = new TranslateTransform(36, 0);
        OpenNoteCard.RenderTransform = cardTransform;
        OpenNoteCard.Opacity = 0;

        cardTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From = 36,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(320),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            }
        );

        OpenNoteCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        );

        TitleBox.Focus();
    }

    // Point 2 uses ItemsControl for the fan deck.
    // It intentionally has no selection/click behavior yet.
    private void OpenDeckList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpenDeckList.SelectedItem is Note note && note != _currentNote)
        {
            MoveToOpenState(note);
        }
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var note = new Note
        {
            Title = "New Note",
            Content = string.Empty,
            Color = "Yellow",
            SortOrder = _notes.Count
        };

        await App.NoteStore.SaveNoteAsync(note);
        _notes.Add(note);

        // Point 2 only: remain in the title deck.
        // Full note opening will be added in a later step.
        FanNotesList.Items.Refresh();
        RestDashList.Items.Refresh();
    }
    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingEditor || _currentNote is null)
        {
            return;
        }

        _currentNote.Title = TitleBox.Text;
        _currentNote.Content = ContentBox.Text;

        SaveStatusText.Text = "Savingâ€¦";

        _saveTimer.Stop();
        _saveTimer.Start();

        FanNotesList.Items.Refresh();
        OpenDeckList.Items.Refresh();
        RestDashList.Items.Refresh();
    }

    private async void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();

        if (_currentNote is null)
        {
            return;
        }

        await App.NoteStore.SaveNoteAsync(_currentNote);
        SaveStatusText.Text = "Saved";
    }

    private async void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote is null || sender is not Button button)
        {
            return;
        }

        _currentNote.Color = button.Tag?.ToString() ?? "Yellow";

        await App.NoteStore.SaveNoteAsync(_currentNote);

        OpenNoteCard.DataContext = null;
        OpenNoteCard.DataContext = _currentNote;

        FanNotesList.Items.Refresh();
        OpenDeckList.Items.Refresh();
        RestDashList.Items.Refresh();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote is null)
        {
            return;
        }

        var target = _currentNote;

        _saveTimer.Stop();

        await App.NoteStore.DeleteNoteAsync(target.Id);

        _notes.Remove(target);

        _currentNote = null;
        OpenDeckList.SelectedItem = null;

        MoveToFanState();
    }

    private void CloseNoteButton_Click(object sender, RoutedEventArgs e)
    {
        _saveTimer.Stop();

        OpenDeckList.SelectedItem = null;
        _currentNote = null;

        MoveToFanState();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_state == DeckState.Open)
            {
                CloseNoteButton_Click(sender, e);
            }
            else if (_state == DeckState.Fan)
            {
                MoveToRestState();
            }

            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.Q)
        {
            Application.Current.Shutdown();
        }
    }
}






















