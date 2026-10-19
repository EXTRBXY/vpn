namespace NothingVpn.Desktop.Wpf;

internal static class TaskExtensions
{
    /// <summary>
    /// Запустить задачу без ожидания с UI-потока. Ошибка не теряется: async void передаёт её в SynchronizationContext
    /// диспетчера, и её обрабатывает <see cref="GlobalExceptionHandler"/>.
    /// </summary>
    public static async void Forget(this Task task) => await task;
}
