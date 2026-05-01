using MaterialDesignThemes.Wpf;

namespace ShopManager.Services;

public class AppSnackbarService : IAppSnackbarService
{
    private SnackbarMessageQueue? _queue;

    public void SetQueue(SnackbarMessageQueue queue) => _queue = queue;

    public void ShowSuccess(string message) =>
        _queue?.Enqueue(message, null, null, null, false, true, TimeSpan.FromSeconds(3));

    public void ShowError(string message) =>
        _queue?.Enqueue("⚠ " + message, null, null, null, false, true, TimeSpan.FromSeconds(4));

    public void ShowWarning(string message) =>
        _queue?.Enqueue("⚠ " + message, null, null, null, false, true, TimeSpan.FromSeconds(5));
}
