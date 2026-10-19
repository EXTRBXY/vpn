using NothingVpn.Desktop.Wpf;

namespace NothingVpn.Application.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_Failure_PropagatesAndEnablesCommandAgain()
    {
        var command = new AsyncRelayCommand(() => Task.FromException(new InvalidOperationException("boom")));
        var changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(null));

        Assert.Equal("boom", error.Message);
        Assert.True(command.CanExecute(null));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Execute_Failure_IsPostedToSynchronizationContext_NotLost()
    {
        // Так ошибка из кнопки попадает в диспетчер WPF, а оттуда — в глобальный обработчик.
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var command = new AsyncRelayCommand(() => Task.FromException(new InvalidOperationException("boom")));
            command.Execute(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var error = Assert.Single(context.Failures);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public async Task Execute_WhileRunning_DoesNotStartAgain()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var command = new AsyncRelayCommand(() =>
        {
            starts++;
            return release.Task;
        });

        var first = command.ExecuteAsync(null);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync(null);
        release.SetResult();
        await first;

        Assert.Equal(1, starts);
        Assert.True(command.CanExecute(null));
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public List<Exception> Failures { get; } = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try { d(state); }
            catch (Exception ex) { Failures.Add(ex); }
        }

        public override void Send(SendOrPostCallback d, object? state) => Post(d, state);
    }
}
