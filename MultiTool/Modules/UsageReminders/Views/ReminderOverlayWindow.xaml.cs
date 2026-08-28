using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MultiTool.Modules.UsageReminders.Views;

public partial class ReminderOverlayWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _collapseTimer;
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string _timeText = string.Empty;
    private string _snoozeLabel = string.Empty;
    private string _collapsedLabel = string.Empty;

    public ReminderOverlayWindow(ReminderPresentation presentation)
    {
        InitializeComponent();
        OccurrenceId = presentation.OccurrenceId;
        DisplayTitle = presentation.Title;
        Message = presentation.Message;
        TimeText = presentation.IsPreview
            ? "Preview"
            : $"Scheduled for {presentation.ScheduledUtc.ToLocalTime():t}";
        SnoozeLabel = $"Snooze {presentation.SnoozeMinutes} min";
        CollapsedLabel = presentation.IsPreview
            ? $"Preview: {presentation.ProfileName}"
            : $"{presentation.ProfileName} reminder";
        SnoozeButton.Visibility = presentation.IsPreview
            ? Visibility.Collapsed
            : Visibility.Visible;
        DataContext = this;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _collapseTimer.Tick += HandleCollapseTimer;
        Loaded += HandleLoaded;
        Closed += HandleClosed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ReminderResponseEventArgs>? ResponseRequested;

    public event EventHandler? SizeModeChanged;

    public Guid OccurrenceId { get; }

    public string DisplayTitle { get => _title; private set => SetField(ref _title, value); }

    public string Message { get => _message; private set => SetField(ref _message, value); }

    public string TimeText { get => _timeText; private set => SetField(ref _timeText, value); }

    public string SnoozeLabel { get => _snoozeLabel; private set => SetField(ref _snoozeLabel, value); }

    public string CollapsedLabel { get => _collapsedLabel; private set => SetField(ref _collapsedLabel, value); }

    private void HandleLoaded(object sender, RoutedEventArgs eventArgs)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        SlideTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        _collapseTimer.Start();
    }

    private void HandleCollapseTimer(object? sender, EventArgs eventArgs)
    {
        _collapseTimer.Stop();
        ExpandedContent.Visibility = Visibility.Collapsed;
        CollapsedContent.Visibility = Visibility.Visible;
        SizeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleExpandClicked(object sender, RoutedEventArgs eventArgs)
    {
        CollapsedContent.Visibility = Visibility.Collapsed;
        ExpandedContent.Visibility = Visibility.Visible;
        SizeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleDoneClicked(object sender, RoutedEventArgs eventArgs) =>
        ResponseRequested?.Invoke(
            this,
            new ReminderResponseEventArgs(OccurrenceId, ReminderResponse.Acknowledge));

    private void HandleSnoozeClicked(object sender, RoutedEventArgs eventArgs) =>
        ResponseRequested?.Invoke(
            this,
            new ReminderResponseEventArgs(OccurrenceId, ReminderResponse.Snooze));

    private void HandleClosed(object? sender, EventArgs eventArgs)
    {
        _collapseTimer.Stop();
        _collapseTimer.Tick -= HandleCollapseTimer;
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
