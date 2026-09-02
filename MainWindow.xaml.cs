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
    private readonly StickyNotes.Services.SettingsService _settingsService = new();
    private StickyNotes.Core.Models.AppSettings _appSettings = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _fanHoverTimer;

    private Note? _currentNote;
    private SettingsWindow? _settingsWindow;
    private Note? _pendingDeleteNote;
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
        _appSettings = _settingsService.Load();
        ApplyEdgeLayout();

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

        foreach (var note in notes)
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

        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(850);
        _collapseTimer.Start();
    }
    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();

        if (_state != DeckState.Fan)
        {
            return;
        }

        bool overFan = FanDeck.IsMouseOver;

        bool overPreview =
            PreviewPopup.IsOpen &&
            (_previewPopupMouseOver || PreviewCard.IsMouseOver);

        if (overFan || overPreview)
        {
            _previewExitPending = false;
            return;
        }

        if (PreviewPopup.IsOpen)
        {
            _previewExitPending = false;
            ClosePreviewImmediately();

            // Keep titles visible briefly after preview closes.
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(500);
            _collapseTimer.Start();
            return;
        }

        // No preview and pointer stayed away: return to 12px rest strip.
        MoveToRestState();
    }

    private void ApplyEdgeLayout()
    {
        RestPill.HorizontalAlignment = HorizontalAlignment.Right;
        RestPill.CornerRadius = new CornerRadius(6, 0, 0, 6);

        FanDeck.HorizontalAlignment = HorizontalAlignment.Right;
        FanNotesList.HorizontalAlignment = HorizontalAlignment.Right;

        AddButton.HorizontalAlignment = HorizontalAlignment.Right;
        AddButton.Margin = new Thickness(0, 12, 8, 0);

        OpenState.ColumnDefinitions[0].Width = new GridLength(320);
        OpenState.ColumnDefinitions[1].Width = new GridLength(94);

        Grid.SetColumn(OpenNoteCard, 0);
        Grid.SetColumn(OpenDeckRail, 1);

        OpenNoteCard.HorizontalAlignment = HorizontalAlignment.Right;
        OpenDeckRail.HorizontalAlignment = HorizontalAlignment.Right;

        PreviewCard.CornerRadius = new CornerRadius(18, 0, 0, 18);

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdateDynamicEdgeCorners));
    }

    private void UpdateDynamicEdgeCorners()
    {
        var fanCorner = new CornerRadius(15, 0, 0, 15);

        for (int i = 0; i < FanNotesList.Items.Count; i++)
        {
            if (FanNotesList.ItemContainerGenerator.ContainerFromIndex(i)
                is ContentPresenter presenter &&
                FindFirstBorder(presenter) is Border fanBorder)
            {
                fanBorder.CornerRadius = fanCorner;
            }
        }

        var openCorner = new CornerRadius(13, 0, 0, 13);

        for (int i = 0; i < OpenDeckList.Items.Count; i++)
        {
            if (OpenDeckList.ItemContainerGenerator.ContainerFromIndex(i)
                is ListBoxItem item &&
                FindFirstBorder(item) is Border border)
            {
                border.CornerRadius = openCorner;
            }
        }
    }

    private static Border? FindFirstBorder(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Border border)
            {
                return border;
            }

            var nested = FindFirstBorder(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
        // Disabled: the + button must remain fixed while previews open/close.
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
        UpdateDynamicEdgeCorners();
        FanDeck.Opacity = 1;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(AnimateFanDeck)
        );
            _ = EnablePreviewAfterFanSettlesAsync();
}

    private async void CompleteFanTransitionAsync()
    {
        await Task.Delay(580);

        if (_state != DeckState.Fan)
        {
            return;
        }

        _fanTransitionInProgress = false;
        _fanPreviewReady = true;

        // If the pointer remained over a final settled tab,
        // begin the normal dwell-based preview intent.
        for (int i = 0; i < FanNotesList.Items.Count; i++)
        {
            if (FanNotesList.ItemContainerGenerator.ContainerFromIndex(i)
                is FrameworkElement container &&
                container.IsMouseOver)
            {
                if (ResolveFanTabBorderFromContainer(container) is Border fanTab)
                {
                    BeginPreviewIntent(fanTab);
                }
                break;
            }
        }
    }
    private async Task EnablePreviewAfterFanSettlesAsync()
    {
        await Task.Delay(580);

        if (_state != DeckState.Fan)
        {
            return;
        }

        _fanTransitionInProgress = false;
        _fanPreviewReady = true;

        await Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < FanNotesList.Items.Count; i++)
            {
                if (FanNotesList.ItemContainerGenerator.ContainerFromIndex(i)
                    is FrameworkElement container &&
                    container.IsMouseOver)
                {
                    if (ResolveFanTabBorderFromContainer(container) is Border fanTab)
                {
                    BeginPreviewIntent(fanTab);
                }
                    break;
                }
            }
        });
    }
    private void AnimateFanDeck()
    {
        var visibleItems = new List<ContentPresenter>();

        FanNotesScrollViewer.UpdateLayout();
        FanNotesList.UpdateLayout();

        for (int i = 0; i < FanNotesList.Items.Count; i++)
        {
            if (FanNotesList.ItemContainerGenerator.ContainerFromIndex(i)
                is not ContentPresenter item)
            {
                continue;
            }

            // Remove any leftover animation clocks from previous openings.
            item.BeginAnimation(OpacityProperty, null);
            item.Opacity = 1;

            if (item.RenderTransform is TranslateTransform previousTransform)
            {
                previousTransform.BeginAnimation(
                    TranslateTransform.XProperty,
                    null);
                previousTransform.X = 0;
            }

            Point p = item.TranslatePoint(
                new Point(0, 0),
                FanNotesScrollViewer);

            bool isVisible =
                p.Y < FanNotesScrollViewer.ActualHeight &&
                (p.Y + Math.Max(item.ActualHeight, 1)) > 0;

            if (!isVisible)
            {
                item.RenderTransform = new TranslateTransform(0, 0);
                continue;
            }

            visibleItems.Add(item);
        }

        visibleItems = visibleItems
            .OrderBy(item =>
                item.TranslatePoint(
                    new Point(0, 0),
                    FanNotesScrollViewer).Y)
            .ToList();

        for (int i = 0; i < visibleItems.Count; i++)
        {
            var item = visibleItems[i];

            // Keep opacity fixed at 1. Only animate position.
            item.Opacity = 1;

            var transform = new TranslateTransform(16, 0);
            item.RenderTransform = transform;

            TimeSpan delay =
                TimeSpan.FromMilliseconds(i * 40);

            var slide = new DoubleAnimation
            {
                From = 16,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(320),
                BeginTime = delay,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };

            slide.Completed += (_, _) =>
            {
                transform.BeginAnimation(
                    TranslateTransform.XProperty,
                    null);
                transform.X = 0;

                item.Opacity = 1;
            };

            transform.BeginAnimation(
                TranslateTransform.XProperty,
                slide);
        }
    }
    private CustomPopupPlacement[] PreviewPopup_CustomPopupPlacement(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        double yOffset = 0;

        // The + button lives below the fixed-height fan viewport.
        // Keep a reserved zone at the bottom of FanDeck so the popup
        // can never cover the + button.
        if (PreviewPopup.PlacementTarget is FrameworkElement target &&
            FanDeck.ActualHeight > 0)
        {
            Point targetTop =
                target.TranslatePoint(new Point(0, 0), FanDeck);

            const double reservedAddArea = 54;
            const double gapAboveAddButton = 8;

            double addAreaTop =
                Math.Max(
                    0,
                    FanDeck.ActualHeight -
                    reservedAddArea -
                    gapAboveAddButton);

            double previewBottom =
                targetTop.Y + popupSize.Height;

            if (previewBottom > addAreaTop)
            {
                yOffset = addAreaTop - previewBottom;
            }
        }

                // FORCE_LAST_NOTE_PREVIEW_UP
        // The final fan note sits closest to the fixed + area.
        // Always keep its preview clearly above that lower zone.
        if (PreviewPopup.PlacementTarget is FrameworkElement placementTarget &&
            FanNotesList.Items.Count > 0)
        {
            object lastNote = FanNotesList.Items[FanNotesList.Items.Count - 1];

            if (ReferenceEquals(placementTarget.DataContext, lastNote))
            {
                yOffset = Math.Min(yOffset, -145);
            }
        }
return new[]
        {
            new CustomPopupPlacement(
                new Point(
                    targetSize.Width - popupSize.Width,
                    yOffset),
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

    private bool IsFanTabBorder(Border border)
    {
        if (border.DataContext is not Core.Models.Note)
        {
            return false;
        }

        if (Math.Abs(border.Width - 46) > 0.5 ||
            Math.Abs(border.Height - 116) > 0.5)
        {
            return false;
        }

        DependencyObject? current = border;

        while (current is not null)
        {
            if (ReferenceEquals(current, FanNotesList))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private Border? ResolveFanTabBorderFromContainer(DependencyObject root)
    {
        if (root is Border rootBorder && IsFanTabBorder(rootBorder))
        {
            return rootBorder;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Border border && IsFanTabBorder(border))
            {
                return border;
            }

            var nested = ResolveFanTabBorderFromContainer(child);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private FrameworkElement? FindHoveredFanTab()
    {
        DependencyObject? current = Mouse.DirectlyOver as DependencyObject;

        while (current is not null)
        {
            if (current is Border border && IsFanTabBorder(border))
            {
                return border;
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

        if (sender is Border fanTab && IsFanTabBorder(fanTab))
        {
            BeginPreviewIntent(fanTab);
        }
    }

    private void FanNote_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Border fanTab || !IsFanTabBorder(fanTab))
        {
            return;
        }

        // Hiding the preview owner raises MouseLeave by itself.
        // Do not treat that as a real pointer exit.
        if (PreviewPopup.IsOpen &&
            ReferenceEquals(_previewedTab, fanTab))
        {
            return;
        }

        if (ReferenceEquals(_previewIntentTab, fanTab))
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


        _previewExitPending = false;
        PreviewPopup.IsOpen = true;

        await Dispatcher.InvokeAsync(
            () => {
        // Disabled: the + button must remain fixed while previews open/close.
    },
            DispatcherPriority.Loaded);

        if (intentVersion != _previewIntentVersion)
        {
            return;
        }

        AnimatePreviewIn();
    }

    private void UpdateAddButtonPreviewOffset(object note)
    {
        // The + button is permanently fixed below the fan viewport.
        // Preview opening must never move it.
    }

    private void ResetAddButtonPreviewOffset()
    {
        if (AddButton is not null)
        {
            AddButton.RenderTransform = Transform.Identity;
        }
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

    private void PreviewCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_state != DeckState.Fan)
        {
            return;
        }

        if (_previewedNote is not Note note)
        {
            return;
        }

        e.Handled = true;

        _collapseTimer.Stop();
        _previewIntentVersion++;
        _previewIntentTab = null;

        ClosePreviewImmediately();
        MoveToOpenState(note);
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
        _previewExitPending = true;

        if (_state != DeckState.Fan)
        {
            return;
        }

        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(850);
        _collapseTimer.Start();
    }

        

private void ClosePreviewImmediately()
    {
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
        ClosePreviewImmediately();
        DeleteConfirmPopup.IsOpen = false;
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
        UpdateDynamicEdgeCorners();
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
        var settings = new StickyNotes.Services.SettingsService().Load();
        var defaultColor = string.IsNullOrWhiteSpace(settings.DefaultColor)
            ? "Yellow"
            : settings.DefaultColor;

        var note = new Note
        {
            Title = "New Note",
            Content = string.Empty,
            Color = defaultColor,
            SortOrder = _notes.Count
        };

        await App.NoteStore.SaveNoteAsync(note);
        _notes.Add(note);

        FanNotesList.Items.Refresh();
        RestDashList.Items.Refresh();
        OpenDeckList.Items.Refresh();

        // Restore intended flow:
        // creating a note immediately opens the existing full editor.
        MoveToOpenState(note);
    }
    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingEditor || _currentNote is null)
        {
            return;
        }

        _currentNote.Title = TitleBox.Text;
        _currentNote.Content = ContentBox.Text;

        SaveStatusText.Text = "Saving...";

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

        bool hasUserData =
            !string.IsNullOrWhiteSpace(target.Content) ||
            (!string.IsNullOrWhiteSpace(target.Title) &&
             !string.Equals(target.Title.Trim(), "New Note", StringComparison.OrdinalIgnoreCase));

        if (hasUserData)
        {
            _pendingDeleteNote = target;
            DeleteConfirmPopup.PlacementTarget = OpenNoteCard;
            DeleteConfirmPopup.IsOpen = true;
            return;
        }

        await DeleteNoteNowAsync(target);
    }

    private async Task DeleteNoteNowAsync(Note target)
    {
        DeleteConfirmPopup.IsOpen = false;
        _pendingDeleteNote = null;
        _saveTimer.Stop();

        await App.NoteStore.DeleteNoteAsync(target.Id);
        _notes.Remove(target);

        _currentNote = null;
        OpenDeckList.SelectedItem = null;

        MoveToFanState();
    }

    private async void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteNote is not Note target)
        {
            DeleteConfirmPopup.IsOpen = false;
            return;
        }

        await DeleteNoteNowAsync(target);
    }

    private void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        DeleteConfirmPopup.IsOpen = false;
        _pendingDeleteNote = null;
    }

    private async void CloseNoteButton_Click(object sender, RoutedEventArgs e)
    {
        _saveTimer.Stop();
        DeleteConfirmPopup.IsOpen = false;
        ClosePreviewImmediately();

        OpenDeckList.SelectedItem = null;
        _currentNote = null;

        if (OpenNoteCard.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            OpenNoteCard.RenderTransform = transform;
        }

        OpenNoteCard.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        var fade = new DoubleAnimation
        {
            From = OpenNoteCard.Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };

        var slide = new DoubleAnimation
        {
            From = transform.X,
            To = 42,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };

        OpenNoteCard.BeginAnimation(OpacityProperty, fade);
        transform.BeginAnimation(TranslateTransform.XProperty, slide);

        await Task.Delay(260);

        MoveToRestState();

        OpenNoteCard.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        OpenNoteCard.Opacity = 1;
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ClosePreviewImmediately();

        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
            _settingsWindow.Focus();
            return;
        }

        _settingsWindow = new SettingsWindow();

        _settingsWindow.SettingsSaved += settings =>
        {
            _appSettings = settings;
            ApplyEdgeLayout();
            MoveToRestState();
        };

        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _appSettings = _settingsService.Load();
        ApplyEdgeLayout();
            ApplyEdgeLayout();
            MoveToRestState();
        };

        _settingsWindow.Show();
        _settingsWindow.Activate();
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













































