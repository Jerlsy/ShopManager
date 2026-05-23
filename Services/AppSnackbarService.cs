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

    // 帶可點擊動作按鈕（如「復原」）的 snackbar：5 秒停留以方便使用者反應
    public void ShowSuccessWithAction(string message, string actionLabel, Action action) =>
        _queue?.Enqueue(message, actionLabel, (Action<object?>)(_ => action()), null, false, true, TimeSpan.FromSeconds(5));
}
