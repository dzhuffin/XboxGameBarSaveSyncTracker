using System;
using System.Linq;
using Microsoft.Gaming.XboxGameBar;
using SaveSync.Core;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SaveSync.Widget
{
    public sealed partial class WidgetPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan LaunchRetry = TimeSpan.FromSeconds(15);

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = PollInterval };
        private readonly string _statusDir = ApplicationData.Current.LocalFolder.Path;
        private XboxGameBarWidget _widget;
        private DateTime _lastLaunchAttemptUtc = DateTime.MinValue;
        private string _renderedSignature;

        public WidgetPage()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { _timer.Start(); Refresh(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _widget = e.Parameter as XboxGameBarWidget;
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e) => Rerender();
        private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rerender();
        private void OnSortChanged(object sender, SelectionChangedEventArgs e) => Rerender();

        private void Rerender()
        {
            // Handlers fire during XAML load, before the later named controls are connected.
            if (!IsLoaded) return;
            _renderedSignature = null;
            Refresh();
        }

        private void Refresh()
        {
            var now = DateTime.UtcNow;
            var snapshot = StatusFile.TryRead(_statusDir);
            var alive = snapshot != null && now - snapshot.GeneratedUtc < StaleAfter;

            if (!alive) EnsureCompanionRunning(now);

            if (snapshot == null)
            {
                ShowBanner("Starting the sync monitor…");
                return;
            }

            var items = GameItemFactory.Build(snapshot, ShowUninstalled.IsChecked == true, SearchBox.Text,
                (SortMode)Math.Max(0, SortBox.SelectedIndex), now);
            ShowBanner(!alive ? "Sync monitor isn't running — showing the last known state. Restarting it…"
                : items.Count == 0 ? (string.IsNullOrWhiteSpace(SearchBox.Text) ? "No games with Xbox cloud saves found." : "No games match your search.") : null);

            var signature = string.Join("|", items.Select(i => i.Title + i.StateText + i.Detail));
            if (signature == _renderedSignature) return;

            _renderedSignature = signature;
            GameList.ItemsSource = items;
        }

        private async void EnsureCompanionRunning(DateTime now)
        {
            if (now - _lastLaunchAttemptUtc < LaunchRetry) return;
            _lastLaunchAttemptUtc = now;
            try
            {
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("Monitor");
            }
            catch (Exception ex)
            {
                ShowBanner("Couldn't start the sync monitor: " + ex.Message);
            }
        }

        private void ShowBanner(string text)
        {
            Banner.Text = text ?? string.Empty;
            Banner.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
