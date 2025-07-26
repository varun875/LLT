using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Dashboard;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using Wpf.Ui.Common; // For SymbolRegular
using Wpf.Ui.Controls; // For Button and other WPF UI controls

namespace LenovoLegionToolkit.WPF.Pages;

public partial class DashboardPage
{
    // Define a constant for the layout width threshold to improve readability and maintainability.
    private const double ExpandLayoutThreshold = 1000.0;

    // Resolve DashboardSettings using IoCContainer. This assumes IoCContainer is properly initialized.
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();

    // List to keep track of dynamically added DashboardGroupControl instances.
    private readonly List<DashboardGroupControl> _dashboardGroupControls = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardPage"/> class.
    /// </summary>
    public DashboardPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Event handler for when the DashboardPage is initialized.
    /// Triggers an asynchronous refresh of the dashboard content.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event data.</param>
    private async void DashboardPage_Initialized(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    /// <summary>
    /// Asynchronously refreshes the dashboard content, including sensors, groups, and the customize button.
    /// </summary>
    private async Task RefreshAsync()
    {
        // Indicate that content is loading to the user.
        _loader.IsLoading = true;

        // Create a list to hold initialization tasks for dashboard group controls.
        // Add a small delay to ensure the loading indicator is visible for a moment.
        var initializedTasks = new List<Task> { Task.Delay(TimeSpan.FromSeconds(1)) };

        // Scroll to the top of the page if a ScrollHost is available.
        // The ScrollHost is typically a ScrollViewer wrapping the content in XAML.
        ScrollHost?.ScrollToTop();

        // Set visibility of the sensors control based on user settings.
        _sensors.Visibility = _dashboardSettings.Store.ShowSensors ? Visibility.Visible : Visibility.Collapsed;

        // Clear existing controls and grid definitions to prepare for new content.
        _dashboardGroupControls.Clear();
        _content.ColumnDefinitions.Clear();
        _content.RowDefinitions.Clear();
        _content.Children.Clear();

        // Get the dashboard groups from settings, or use default groups if none are defined.
        var groups = _dashboardSettings.Store.Groups ?? DashboardGroup.DefaultGroups;

        // Log the groups if trace logging is enabled.
        if (Log.Instance.IsTraceEnabled)
        {
            // Explicitly cast to FormattableString to resolve the compilation error.
            Log.Instance.Trace((FormattableString)$"Groups:");
            foreach (var group in groups)
                // Explicitly cast to FormattableString to resolve the compilation error.
                Log.Instance.Trace((FormattableString)$" - {group}");
        }

        // Define two columns for the main content grid.
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });

        // Dynamically add DashboardGroupControl for each group.
        foreach (var group in groups)
        {
            // Add a new row definition for each group. Height is auto to fit content.
            _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

            // Create a new control for the dashboard group.
            var control = new DashboardGroupControl(group);
            _content.Children.Add(control); // Add to the grid children.
            _dashboardGroupControls.Add(control); // Add to our tracking list.
            initializedTasks.Add(control.InitializedTask); // Add its initialization task to the list.
        }

        // Add an additional row for the "Customize" button at the bottom.
        _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

        // Create a WPF UI Button for "Customize Dashboard" instead of a Hyperlink for better Material Design integration.
        var editDashboardButton = new Wpf.Ui.Controls.Button
        {
            // Corrected: Directly assign the SymbolRegular enum value to the Icon property.
            Icon = SymbolRegular.Edit24,
            Content = Resource.DashboardPage_Customize,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Appearance = ControlAppearance.Secondary // Use a secondary appearance for a less prominent button
        };

        // Attach the click event handler for the customize button.
        editDashboardButton.Click += (_, _) =>
        {
            // Create and show the EditDashboardWindow.
            var window = new EditDashboardWindow { Owner = Window.GetWindow(this) };
            // Subscribe to the Apply event of the edit window to refresh the dashboard when changes are applied.
            window.Apply += async (_, _) => await RefreshAsync();
            window.ShowDialog(); // Show as a modal dialog.
        };

        // Position the button in the grid.
        Grid.SetRow(editDashboardButton, groups.Length); // Place in the row after all groups.
        Grid.SetColumn(editDashboardButton, 0); // Start at the first column.
        Grid.SetColumnSpan(editDashboardButton, 2); // Span across both columns.

        // Add the button to the grid children.
        _content.Children.Add(editDashboardButton);

        // Lay out the groups based on the current width.
        LayoutGroups(ActualWidth);

        // Wait for all dashboard group controls to be initialized.
        // Any exceptions during initialization will be propagated here.
        await Task.WhenAll(initializedTasks);

        // Hide the loading indicator.
        _loader.IsLoading = false;
    }

    /// <summary>
    /// Event handler for when the DashboardPage's size changes.
    /// Triggers a re-layout of the groups if the width has changed.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event data containing new size information.</param>
    private void DashboardPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Only re-layout if the width of the page has changed.
        if (!e.WidthChanged)
            return;

        LayoutGroups(e.NewSize.Width);
    }

    /// <summary>
    /// Lays out the dashboard groups in either an expanded (two-column) or collapsed (single-column) view
    /// based on the provided width.
    /// </summary>
    /// <param name="width">The current width of the dashboard page.</param>
    private void LayoutGroups(double width)
    {
        if (width > ExpandLayoutThreshold)
        {
            Expand(); // If width is greater than threshold, expand to two columns.
        }
        else
        {
            Collapse(); // Otherwise, collapse to a single column.
        }
    }

    /// <summary>
    /// Arranges dashboard groups in a two-column layout.
    /// </summary>
    private void Expand()
    {
        // Ensure the second column has a star width to make it visible.
        // We know _content.ColumnDefinitions will have at least two columns from RefreshAsync.
        if (_content.ColumnDefinitions.Count > 1)
        {
            _content.ColumnDefinitions[1].Width = new(1, GridUnitType.Star);
        }

        // Iterate through controls and set their row and column for a two-column layout.
        // Even indices go to column 0, odd indices go to column 1.
        // Rows are calculated to stack pairs of controls.
        for (var index = 0; index < _dashboardGroupControls.Count; index++)
        {
            var control = _dashboardGroupControls[index];
            Grid.SetRow(control, index / 2); // Integer division ensures controls share rows.
            Grid.SetColumn(control, index % 2); // Modulo 2 alternates columns (0 or 1).
        }
    }

    /// <summary>
    /// Arranges dashboard groups in a single-column layout.
    /// </summary>
    private void Collapse()
    {
        // Set the second column's width to 0 to effectively hide it.
        // We know _content.ColumnDefinitions will have at least two columns from RefreshAsync.
        if (_content.ColumnDefinitions.Count > 1)
        {
            _content.ColumnDefinitions[1].Width = new(0, GridUnitType.Pixel);
        }

        // Iterate through controls and set their row and column for a single-column layout.
        // All controls go to column 0, each in its own row.
        for (var index = 0; index < _dashboardGroupControls.Count; index++)
        {
            var control = _dashboardGroupControls[index];
            Grid.SetRow(control, index); // Each control gets its own row.
            Grid.SetColumn(control, 0); // All controls are in the first column.
        }
    }
}