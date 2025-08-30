using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Humanizer;
using Humanizer.Localisation;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;
using LenovoLegionToolkit.Lib.Features;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class BatteryPage
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    // Animation properties
    private Storyboard? _chargingAnimation;
    private DoubleAnimation? _batteryFillAnimation;
    private DoubleAnimation? _healthArcAnimation;

    // UI element references
    private FrameworkElement? _batteryTemperatureCardControl;
    private Button? _modeConservationButtonRef;
    private Button? _modeNormalButtonRef;
    private Button? _modeRapidButtonRef;
    private readonly BatteryFeature _batteryFeature = IoCContainer.Resolve<BatteryFeature>();

    public BatteryPage()
    {
        InitializeComponent();

        IsVisibleChanged += BatteryPage_IsVisibleChanged;

        // Initialize animations
        InitializeAnimations();

    // Cache mode buttons after InitializeComponent
    _modeConservationButtonRef = FindName("_modeConservationButton") as Button;
    _modeNormalButtonRef = FindName("_modeNormalButton") as Button;
    _modeRapidButtonRef = FindName("_modeRapidButton") as Button;
    }

    private void InitializeAnimations()
    {
        // Charging animation
        _chargingAnimation = new Storyboard();
        var opacityAnimation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(opacityAnimation, _chargingOverlay);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
        _chargingAnimation.Children.Add(opacityAnimation);

        // Battery fill animation
        _batteryFillAnimation = new DoubleAnimation
        {
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // Health arc animation
        _healthArcAnimation = new DoubleAnimation
        {
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }

    private async void BatteryPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Refresh();
            return;
        }

        if (_cts is not null)
            await _cts.CancelAsync();

        _cts = null;

        if (_refreshTask is not null)
            await _refreshTask;

        _refreshTask = null;
    }

    private void Refresh()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var token = _cts.Token;

        _refreshTask = Task.Run(async () =>
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery information refresh started...");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var batteryInfo = Battery.GetBatteryInformation();
                    var powerAdapterStatus = await Power.IsPowerAdapterConnectedAsync();
                    var onBatterySince = Battery.GetOnBatterySince();
                    Dispatcher.Invoke(() => Set(batteryInfo, powerAdapterStatus, onBatterySince));

                    // Refresh battery mode selection state occasionally (cheap call via driver/registry)
                    await UpdateBatteryModeSelectionAsync();

                    // Slightly slower polling to reduce CPU wakeups without hurting UX
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery information refresh failed.", ex);
                }
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery information refresh stopped.");
        }, token);
    }

    private void Set(BatteryInformation batteryInfo, PowerAdapterStatus powerAdapterStatus, DateTime? onBatterySince)
    {
        // Update battery percentage and progress bar
        var percentage = batteryInfo.BatteryPercentage;
    _percentRemaining!.Text = $"{percentage}%";
    _batteryProgressBar!.Value = percentage;

        // Update battery fill animation
        var batteryFill = FindName("_batteryFill") as Border;
        if (batteryFill != null)
        {
            var targetWidth = percentage / 100.0 * 16;
            if (Math.Abs(batteryFill.Width - targetWidth) > 0.1)
            {
                if (_batteryFillAnimation is { } fillAnim)
                {
                    fillAnim.From = batteryFill.Width;
                    fillAnim.To = targetWidth;
                    batteryFill.BeginAnimation(FrameworkElement.WidthProperty, fillAnim);
                }
            }
        }

        // Update status text
        var statusText = FindName("_status") as TextBlock;
        if (statusText != null)
            statusText.Text = GetStatusText(batteryInfo);

        // Update quick status indicator
        UpdateQuickStatus(batteryInfo, powerAdapterStatus);

        // Handle charging animation
        var chargingOverlay = FindName("_chargingOverlay") as Border;
        if (batteryInfo.IsCharging && batteryInfo.DischargeRate > 0)
        {
            if (_chargingAnimation != null && _chargingAnimation.GetCurrentState() != ClockState.Active)
                _chargingAnimation.Begin();
        }
        else
        {
            _chargingAnimation?.Stop();
            if (chargingOverlay != null)
                chargingOverlay.Opacity = 0;
        }

        // Update warnings
        var lowBatteryText = FindName("_lowBattery") as TextBlock;
        if (lowBatteryText != null)
            lowBatteryText.Visibility = batteryInfo.IsLowBattery ? Visibility.Visible : Visibility.Collapsed;

        var lowWattageChargerText = FindName("_lowWattageCharger") as TextBlock;
        if (lowWattageChargerText != null)
            lowWattageChargerText.Visibility = powerAdapterStatus == PowerAdapterStatus.ConnectedLowWattage ? Visibility.Visible : Visibility.Collapsed;

        var warningsPanel = FindName("_warningsPanel") as StackPanel;
        if (warningsPanel != null)
            warningsPanel.Visibility = (batteryInfo.IsLowBattery || powerAdapterStatus == PowerAdapterStatus.ConnectedLowWattage)
                ? Visibility.Visible : Visibility.Collapsed;

        // Update temperature
        if (batteryInfo.BatteryTemperatureC is not null)
        {
            var batteryTemperatureText = FindName("_batteryTemperatureText") as TextBlock;
            if (batteryTemperatureText != null)
                batteryTemperatureText.Text = GetTemperatureText(batteryInfo.BatteryTemperatureC);

            var batteryTemperatureCardControl = FindName("_batteryTemperatureCardControl") as FrameworkElement;
            _batteryTemperatureCardControl = batteryTemperatureCardControl;
            if (batteryTemperatureCardControl != null)
                batteryTemperatureCardControl.Visibility = Visibility.Visible;
        }
        else
        {
            var batteryTemperatureCardControl = FindName("_batteryTemperatureCardControl") as FrameworkElement;
            if (batteryTemperatureCardControl != null)
                batteryTemperatureCardControl.Visibility = Visibility.Collapsed;
        }

        // Update time remaining
        var timeRemainingText = FindName("_timeRemainingText") as TextBlock;
        if (timeRemainingText != null)
            timeRemainingText.Text = GetTimeRemainingText(batteryInfo);

        // Update discharge rate
        var batteryDischargeRateText = FindName("_batteryDischargeRateText") as TextBlock;
        if (batteryDischargeRateText != null)
            batteryDischargeRateText.Text = $"{batteryInfo.DischargeRate / 1000.0:+0.00;-0.00;0.00} W";

        // Update min/max discharge rates
        var batteryMinDischargeRateText = FindName("_batteryMinDischargeRateText") as TextBlock;
        if (batteryMinDischargeRateText != null)
            batteryMinDischargeRateText.Text = $"{batteryInfo.MinDischargeRate / 1000.0:+0.00;-0.00;0.00} W";

        var batteryMaxDischargeRateText = FindName("_batteryMaxDischargeRateText") as TextBlock;
        if (batteryMaxDischargeRateText != null)
            batteryMaxDischargeRateText.Text = $"{batteryInfo.MaxDischargeRate / 1000.0:+0.00;-0.00;0.00} W";

        // Update capacities
        var batteryCapacityText = FindName("_batteryCapacityText") as TextBlock;
        if (batteryCapacityText != null)
            batteryCapacityText.Text = $"{batteryInfo.EstimateChargeRemaining / 1000.0:0.00} Wh";

        var batteryFullChargeCapacityText = FindName("_batteryFullChargeCapacityText") as TextBlock;
        if (batteryFullChargeCapacityText != null)
            batteryFullChargeCapacityText.Text = $"{batteryInfo.FullChargeCapacity / 1000.0:0.00} Wh";

        var batteryDesignCapacityText = FindName("_batteryDesignCapacityText") as TextBlock;
        if (batteryDesignCapacityText != null)
            batteryDesignCapacityText.Text = $"{batteryInfo.DesignCapacity / 1000.0:0.00} Wh";

        // Update battery health
        var healthPercentage = batteryInfo.BatteryHealth;
        var batteryHealthText = FindName("_batteryHealthText") as TextBlock;
        if (batteryHealthText != null)
            batteryHealthText.Text = $"{healthPercentage:0.00}%";

        var healthProgressBar = FindName("_healthProgressBar") as ProgressBar;
        if (healthProgressBar != null)
            healthProgressBar.Value = healthPercentage;

        // Update health arc animation
        var healthArc = FindName("_healthArc") as Path;
        if (healthArc != null && Math.Abs(healthArc.Width - healthPercentage / 100.0 * 20) > 0.1)
        {
            if (_healthArcAnimation is { } arcAnim)
            {
                arcAnim.From = healthArc.Width;
                arcAnim.To = healthPercentage / 100.0 * 20;
                healthArc.BeginAnimation(FrameworkElement.WidthProperty, arcAnim);
            }
        }

        // Update health status text
        UpdateHealthStatus(healthPercentage);

        // Update on battery since
        if (!batteryInfo.IsCharging && onBatterySince.HasValue)
        {
            var onBatterySinceValue = onBatterySince.Value;
            var dateText = onBatterySinceValue.ToString("G", Resource.Culture);
            var duration = DateTime.Now.Subtract(onBatterySinceValue);
            _onBatterySinceText!.Text = $"{dateText} ({duration.Humanize(2, Resource.Culture, minUnit: TimeUnit.Second)})";
        }
        else
        {
            _onBatterySinceText!.Text = "-";
        }

        // Update cycle count
        _batteryCycleCountText!.Text = $"{batteryInfo.CycleCount}";

        // Update manufacture date
        if (batteryInfo.ManufactureDate is not null)
        {
            _batteryManufactureDateText!.Text = batteryInfo.ManufactureDate?.ToString(LocalizationHelper.ShortDateFormat) ?? "-";
            _batteryManufactureDateCardControl!.Visibility = Visibility.Visible;
        }
        else
        {
            _batteryManufactureDateCardControl!.Visibility = Visibility.Collapsed;
        }

        // Update first use date
        if (batteryInfo.FirstUseDate is not null)
        {
            _batteryFirstUseDateText!.Text = batteryInfo.FirstUseDate?.ToString(LocalizationHelper.ShortDateFormat) ?? "-";
            _batteryFirstUseDateCardControl!.Visibility = Visibility.Visible;
        }
        else
        {
            _batteryFirstUseDateCardControl!.Visibility = Visibility.Collapsed;
        }
    }

    private async Task UpdateBatteryModeSelectionAsync()
    {
        try
        {
            if (!IsVisible)
                return;

            if (!await _batteryFeature.IsSupportedAsync())
                return;

            var state = await _batteryFeature.GetStateAsync();
            Dispatcher.Invoke(() => HighlightSelectedMode(state));
        }
        catch { /* best-effort */ }
    }

    private void HighlightSelectedMode(BatteryState state)
    {
        void Style(Button? b, bool selected)
        {
            if (b == null) return;
            b.BorderBrush = (Brush)FindResource(selected ? "AccentTextFillColorPrimaryBrush" : "CardStrokeColorDefaultBrush");
            b.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            b.Background = (Brush)FindResource(selected ? "ControlFillColorDefaultBrush" : "SubtleFillColorTransparentBrush");
        }

        Style(_modeConservationButtonRef, state == BatteryState.Conservation);
        Style(_modeNormalButtonRef, state == BatteryState.Normal);
        Style(_modeRapidButtonRef, state == BatteryState.RapidCharge);
    }

    private async void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is null)
            return;

        BatteryState? target = btn.Tag.ToString() switch
        {
            "Conservation" => BatteryState.Conservation,
            "Normal" => BatteryState.Normal,
            "RapidCharge" => BatteryState.RapidCharge,
            _ => null
        };

        if (target is null)
            return;

        try
        {
            if (!await _batteryFeature.IsSupportedAsync())
                return;

            var current = await _batteryFeature.GetStateAsync();
            if (current == target.Value)
                return;

            // Briefly disable buttons to avoid double clicks
            SetModeButtonsEnabled(false);
            await _batteryFeature.SetStateAsync(target.Value);
            HighlightSelectedMode(target.Value);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace(System.Runtime.CompilerServices.FormattableStringFactory.Create("Failed to set battery mode."), ex);
        }
        finally
        {
            SetModeButtonsEnabled(true);
        }
    }

    private void SetModeButtonsEnabled(bool enabled)
    {
        if (_modeConservationButtonRef != null) _modeConservationButtonRef.IsEnabled = enabled;
        if (_modeNormalButtonRef != null) _modeNormalButtonRef.IsEnabled = enabled;
        if (_modeRapidButtonRef != null) _modeRapidButtonRef.IsEnabled = enabled;
    }

    private void UpdateQuickStatus(BatteryInformation batteryInfo, PowerAdapterStatus powerAdapterStatus)
    {
        if (batteryInfo.IsCharging)
        {
            _statusIcon.Symbol = SymbolRegular.BatteryCharge24;
            _statusIcon.Foreground = (Brush)FindResource("SystemFillColorSuccessBrush");
            _quickStatus.Text = "Charging";
        }
        else if (batteryInfo.IsLowBattery)
        {
            _statusIcon.Symbol = SymbolRegular.Warning24;
            _statusIcon.Foreground = (Brush)FindResource("SystemFillColorCautionBrush");
            _quickStatus.Text = "Low Battery";
        }
        else if (powerAdapterStatus == PowerAdapterStatus.ConnectedLowWattage)
        {
            _statusIcon.Symbol = SymbolRegular.PlugDisconnected24;
            _statusIcon.Foreground = (Brush)FindResource("SystemFillColorCautionBrush");
            _quickStatus.Text = "Slow Charging";
        }
        else
        {
            _statusIcon.Symbol = SymbolRegular.Battery1024;
            _statusIcon.Foreground = (Brush)FindResource("SystemFillColorSuccessBrush");
            _quickStatus.Text = "On Battery";
        }
    }

    private void UpdateHealthStatus(double healthPercentage)
    {
        string status;
        Brush color;

        if (healthPercentage >= 90)
        {
            status = "Excellent";
            color = (Brush)FindResource("SystemFillColorSuccessBrush");
        }
        else if (healthPercentage >= 80)
        {
            status = "Good";
            color = (Brush)FindResource("SystemFillColorSuccessBrush");
        }
        else if (healthPercentage >= 70)
        {
            status = "Fair";
            color = (Brush)FindResource("SystemFillColorCautionBrush");
        }
        else if (healthPercentage >= 60)
        {
            status = "Poor";
            color = (Brush)FindResource("SystemFillColorCautionBrush");
        }
        else
        {
            status = "Replace Soon";
            color = (Brush)FindResource("SystemFillColorCriticalBrush");
        }

        // Update the text block in the health card
        var healthStatusText = (TextBlock)FindName("_batteryHealthText");
        if (healthStatusText != null)
        {
            // Find the status text block (assuming it's the third text block in the health card)
            var healthCard = (FrameworkElement)FindName("_batteryHealthText");
            if (healthCard != null && healthCard.Parent is Grid parentGrid)
            {
                foreach (var child in parentGrid.Children)
                {
                    if (child is TextBlock percentageTextBlock && percentageTextBlock.Name == "_batteryHealthText")
                    {
                        // This is the percentage text, skip it
                        continue;
                    }
                    else if (child is TextBlock statusTextBlock && (statusTextBlock.Text.Contains("Excellent") ||
                             statusTextBlock.Text.Contains("Good") ||
                             statusTextBlock.Text.Contains("Fair") ||
                             statusTextBlock.Text.Contains("Poor") ||
                             statusTextBlock.Text.Contains("Replace")))
                    {
                        statusTextBlock.Text = status;
                        statusTextBlock.Foreground = color;
                        break;
                    }
                }
            }
        }
    }

    private static string GetStatusText(BatteryInformation batteryInfo)
    {
        if (batteryInfo.IsCharging)
        {
            if (batteryInfo.DischargeRate > 0)
                return Resource.BatteryPage_ACAdapterConnectedAndCharging;

            return Resource.BatteryPage_ACAdapterConnectedNotCharging;
        }

        if (batteryInfo.BatteryLifeRemaining < 0)
            return Resource.BatteryPage_EstimatingBatteryLife;

        var time = TimeSpan.FromSeconds(batteryInfo.BatteryLifeRemaining).Humanize(2, Resource.Culture);
        return string.Format(Resource.BatteryPage_EstimatedBatteryLifeRemaining, time);
    }

    private string GetTimeRemainingText(BatteryInformation batteryInfo)
    {
        if (batteryInfo.IsCharging)
        {
            if (batteryInfo.DischargeRate > 0)
                return "Charging";
            return "Fully Charged";
        }

        if (batteryInfo.BatteryLifeRemaining < 0)
            return "Calculating...";

        var time = TimeSpan.FromSeconds(batteryInfo.BatteryLifeRemaining);
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}h {(int)time.Minutes}m";
        else
            return $"{(int)time.TotalMinutes}m";
    }

    private string GetTemperatureText(double? temperature)
    {
        if (_batteryTemperatureCardControl != null)
            _batteryTemperatureCardControl.Tag = temperature;

        if (temperature is null)
            return "—";

        if (_settings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            temperature *= 9.0 / 5.0;
            temperature += 32;
            return $"{temperature:0.0} {Resource.Fahrenheit}";
        }

        return $"{temperature:0.0} {Resource.Celsius}";
    }
}
