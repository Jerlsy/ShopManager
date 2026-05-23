namespace ShopManager.Services;

public interface IAppSnackbarService
{
    void ShowSuccess(string message);
    void ShowError(string message);
    void ShowWarning(string message);
    void ShowSuccessWithAction(string message, string actionLabel, Action action);
}
