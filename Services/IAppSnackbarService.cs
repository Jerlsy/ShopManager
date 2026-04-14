namespace ShopManager.Services;

public interface IAppSnackbarService
{
    void ShowSuccess(string message);
    void ShowError(string message);
}
