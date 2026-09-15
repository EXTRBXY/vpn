using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NothingVpn.Desktop.Wpf;
using NothingVpn.Application.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Presentation;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;

namespace NothingVpn.Application.Tests;

public sealed class WpfSettingsBindingTests
{
    [Fact]
    public void SettingsView_SelectionDeletionEditingAndSaving_UpdateActualBindings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/NothingVpn.Desktop.Wpf;component/Themes/Theme.xaml", UriKind.Relative)
                });
                var h = new SettingsViewModelTests.Harness();
                var vm = h.Create();
                var view = new SettingsView { DataContext = vm };
                view.Measure(new System.Windows.Size(850, 700));
                view.Arrange(new Rect(0, 0, 850, 700));
                Pump(view);

                var list = Descendants<ListBox>(view).Single(x => ReferenceEquals(x.ItemsSource, vm.TunApps));
                list.SelectedIndex = 1;
                Pump(view);
                Assert.Same(vm.TunApps[1], vm.SelectedTunApp);
                var remove = Descendants<Button>(view).Single(x => Equals(x.Content, "Удалить") && x.GetBindingExpression(UIElement.IsEnabledProperty)?.ParentBinding.Path.Path == nameof(SettingsViewModel.CanRemoveTunApp));
                remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(view);
                Assert.Equal(2, list.Items.Count);
                Assert.DoesNotContain(vm.TunApps, x => x.Path.EndsWith("two.exe"));
                Assert.Same(vm.SelectedTunApp, list.SelectedItem);

                var mtu = Descendants<TextBox>(view).Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == nameof(SettingsViewModel.MtuText));
                mtu.Text = "invalid";
                Pump(view);
                Assert.Equal("invalid", vm.MtuText);
                vm.SaveCommand.Execute(null);
                Assert.Equal(0, h.Store.SaveCalls);
                mtu.Text = "1400";

                var grid = Descendants<DataGrid>(view).First(x => ReferenceEquals(x.ItemsSource, vm.BuiltinRuleSets));
                Assert.False(grid.CanUserDeleteRows);
                grid.UpdateLayout();
                var actions = Descendants<ComboBox>(grid).Where(x => x.GetBindingExpression(Selector.SelectedValueProperty)?.ParentBinding.Path.Path == "Action").ToArray();
                Assert.Equal(2, actions.Length);
                actions[0].SelectedValue = "block";
                Pump(view);
                Assert.Equal("block", vm.BuiltinRuleSets[0].Action);
                Assert.Equal("direct", vm.BuiltinRuleSets[1].Action);

                var save = Descendants<Button>(view).Single(x => Equals(x.Content, "Сохранить все настройки"));
                save.Command.Execute(null);
                Pump(view);
                Assert.Equal(1, h.Store.SaveCalls);
                Assert.Equal(1400, h.Store.State.TunMtu);
                Assert.Equal(2, list.Items.Count);
                Assert.Equal("block", h.Store.State.UserRuleSets[0].Action);
                CheckHomeScreenBindings(h);
                CheckBuiltinRuleSetDownloads();
                CheckSettingsWheelScrolling();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF binding test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CheckSettingsWheelScrolling()
    {
        var vm = new SettingsViewModelTests.Harness().Create();
        var view = new SettingsView { DataContext = vm };
        view.Measure(new System.Windows.Size(850, 700));
        view.Arrange(new Rect(0, 0, 850, 700));
        Pump(view);
        var page = (ScrollViewer)view.FindName("SettingsScroll");
        var list = Descendants<ListBox>(view).Single(x => ReferenceEquals(x.ItemsSource, vm.TunApps));
        var inner = Descendants<ScrollViewer>(list).Single();
        list.SelectedIndex = 1;
        list.BringIntoView();
        Pump(view);
        Assert.True(page.ScrollableHeight > 0);
        Assert.Equal(0, inner.ScrollableHeight);
        var before = page.VerticalOffset;
        Wheel(-120);
        Assert.True(page.VerticalOffset > before, "The page must scroll down over a short app list.");
        before = page.VerticalOffset;
        Wheel(120);
        Assert.True(page.VerticalOffset < before, "The page must scroll up over a short app list.");
        Assert.Same(vm.TunApps[1], vm.SelectedTunApp);

        for (var i = 0; i < 30; i++) vm.TunApps.Add(TunAppListItem.FromPath($@"C:\Apps\scroll-{i}.exe"));
        Pump(view);
        Assert.True(inner.ScrollableHeight > 0);
        inner.ScrollToTop();
        Pump(view);
        before = page.VerticalOffset;
        Wheel(-120);
        Assert.True(inner.VerticalOffset > 0, "A long list must retain its own scrolling.");
        Assert.Equal(before, page.VerticalOffset);

        inner.ScrollToBottom();
        Pump(view);
        before = page.VerticalOffset;
        Wheel(-120);
        Assert.True(page.VerticalOffset > before, "At the list's bottom, wheel input must reach the page.");
        inner.ScrollToTop();
        Pump(view);
        before = page.VerticalOffset;
        Wheel(120);
        Assert.True(page.VerticalOffset < before, "At the list's top, wheel input must reach the page.");

        void Wheel(int delta)
        {
            var source = Descendants<TextBlock>(list).First();
            var args = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta)
            {
                RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent
            };
            source.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent;
                source.RaiseEvent(args);
            }
            Pump(view);
        }
    }

    private static void CheckBuiltinRuleSetDownloads()
    {
        var h = new SettingsViewModelTests.Harness();
        h.Files.FileExists = false;
        h.Store.State.UserRuleSets[0].Enabled = false;
        var vm = h.Create();
        var view = new SettingsView { DataContext = vm };
        view.Measure(new System.Windows.Size(850, 700));
        view.Arrange(new Rect(0, 0, 850, 700));
        Pump(view);
        var grid = Descendants<DataGrid>(view).Single(x => ReferenceEquals(x.ItemsSource, vm.BuiltinRuleSets));
        var checkbox = Descendants<System.Windows.Controls.CheckBox>(grid).First();
        checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump(view);
        Assert.Single(h.Files.Downloads);
        Assert.False(h.Files.ConditionalRequest);
        Assert.False(vm.BuiltinRuleSets[0].Enabled);
        Assert.False(vm.SaveCommand.CanExecute(null));
        h.Files.FileExists = true;
        h.Files.Download.SetResult(new RuleSetDownloadResult(true, false, "download-etag", null));
        Assert.True(SpinWait.SpinUntil(() => { Pump(view); return vm.CanEditRuleSets; }, TimeSpan.FromSeconds(5)));
        Assert.True(vm.BuiltinRuleSets[0].Enabled);
        Assert.True(Descendants<System.Windows.Controls.CheckBox>(grid).First().IsChecked);
        Assert.Equal("download-etag", vm.BuiltinRuleSets[0].RemoteEtag);
        vm.SaveCommand.Execute(null);
        Assert.True(h.Create().BuiltinRuleSets[0].Enabled);

        grid.SelectedIndex = 0;
        grid.CurrentCell = new DataGridCellInfo(grid.Items[0], grid.Columns[1]);
        grid.BeginEdit(); // A name cell may still be editing when the download button is used.
        Pump(view);
        var download = Descendants<Button>(view).Single(x => Equals(x.Content, "Скачать или обновить"));
        Assert.True(download.IsEnabled);
        download.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(SpinWait.SpinUntil(() => { Pump(view); return vm.CanEditRuleSets; }, TimeSpan.FromSeconds(5)));
        Assert.Equal(2, h.Files.Downloads.Count);
        Assert.True(h.Files.ConditionalRequest);
        Assert.Contains("обновлён", vm.Message);

        var failed = new SettingsViewModelTests.Harness();
        failed.Files.FileExists = false;
        failed.Store.State.UserRuleSets[0].Enabled = false;
        failed.Files.Download.SetResult(new RuleSetDownloadResult(false, false, null, "HTTP 503"));
        var failedVm = failed.Create();
        view.DataContext = failedVm;
        Pump(view);
        grid = Descendants<DataGrid>(view).Single(x => ReferenceEquals(x.ItemsSource, failedVm.BuiltinRuleSets));
        checkbox = Descendants<System.Windows.Controls.CheckBox>(grid).First();
        checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump(view);
        Assert.Single(failed.Files.Downloads);
        Assert.False(checkbox.IsChecked);
        Assert.False(failedVm.BuiltinRuleSets[0].Enabled);
        Assert.Contains("HTTP 503", failedVm.Message);
    }

    private static void CheckHomeScreenBindings(SettingsViewModelTests.Harness h)
    {
        var screen = new FakeScreen(h);
        var connection = new FakeConnection();
        var settings = h.Create();
        var profiles = new ProfileViewModel(new FakeProfiles(screen), new SubscriptionViewModel(new EmptySubscriptions()));
        var main = new MainViewModel(screen, connection, profiles, settings, null!, new InMemoryLogStore(), () => { });
        var combo = new ComboBox { DataContext = main };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(MainViewModel.Profiles)));
        combo.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(MainViewModel.SelectedProfile)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        Pump(combo);
        screen.SelectedIds.Clear();
        settings.SaveCommand.Execute(null); // Triggers a refresh of the bound home list.
        Pump(combo);
        Assert.Empty(screen.SelectedIds);
        Assert.Equal("p1", main.SelectedProfile?.Id);
        Assert.Same(main.SelectedProfile, combo.SelectedItem);

        main.ShowSettingsCommand.Execute(null);
        settings.InterfaceName = "Keep draft";
        main.ShowHomeCommand.Execute(null);
        main.ShowSettingsCommand.Execute(null);
        Assert.Equal("Keep draft", settings.InterfaceName);

        var start = main.ConnectFromTrayAsync();
        Assert.False(main.CanEdit);
        Assert.False(main.ToggleConnectionCommand.CanExecute(null));
        main.ConnectFromTrayAsync().GetAwaiter().GetResult();
        Assert.Equal(1, connection.Starts);
        var exit = main.StopForExitAsync();
        Assert.False(exit.IsCompleted);
        connection.CompleteStart();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Pump(combo);
            return start.IsCompleted && exit.IsCompleted;
        }, TimeSpan.FromSeconds(5)));
        start.GetAwaiter().GetResult();
        exit.GetAwaiter().GetResult();
        Assert.Equal(1, connection.Stops);
        Assert.False(connection.IsRunning);
        Assert.True(main.CanEdit);
    }

    private sealed class FakeScreen(SettingsViewModelTests.Harness h) : IConnectionScreenController
    {
        public VpnProfile Profile { get; } = new() { Id = "p1", Name = "Test" };
        public List<string?> SelectedIds { get; } = [];
        public ConnectionScreenSnapshot Load() => new(h.Settings.GetState(), [Profile], Profile);
        public void Save(AppStateModel state) => h.Settings.SaveState(state);
        public void SelectProfile(AppStateModel state, string? profileId) => SelectedIds.Add(profileId);
        public void SelectMode(AppStateModel state, string mode) => h.Settings.UpdateMode(mode);
    }

    private sealed class FakeConnection : IConnectionController
    {
        public event EventHandler<bool>? ConnectionStateChanged;
        private readonly TaskCompletionSource<ConnectionStartOutcome> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning { get; private set; }
        public bool IsAdministrator => false;
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public VpnConnectionStatus GetStatus() => new() { IsRunning = IsRunning };
        public Task<ConnectionStartOutcome> StartAsync(string profileId, string mode, CancellationToken cancellationToken = default) { Starts++; return _start.Task; }
        public void CompleteStart() { IsRunning = true; ConnectionStateChanged?.Invoke(this, true); _start.SetResult(new ConnectionStartOutcome(true, false)); }
        public Task StopAsync(CancellationToken cancellationToken = default) { Stops++; IsRunning = false; ConnectionStateChanged?.Invoke(this, false); return Task.CompletedTask; }
    }

    private sealed class FakeProfiles(FakeScreen screen) : IProfileManagementController
    {
        public ProfileManagementSnapshot Load() => new([screen.Profile], "p1", null);
        public bool TryParse(string link, out VpnProfile profile) => throw new NotSupportedException();
        public VpnProfile Add(string link, string? nameOverride) => throw new NotSupportedException();
        public VpnProfile Edit(string existingProfileId, string link, string? nameOverride) => throw new NotSupportedException();
        public ProfileManagementSnapshot Delete(string profileId) => throw new NotSupportedException();
    }

    private sealed class EmptySubscriptions : ISubscriptionManagementController
    {
        public IReadOnlyList<SubscriptionModel> Load() => [];
        public SubscriptionModel Save(string? id, string name, string url, bool enabled) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public Task<SubscriptionRefreshResult> RefreshAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SubscriptionRefreshResult>> RefreshAllAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static void Pump(FrameworkElement view)
    {
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
