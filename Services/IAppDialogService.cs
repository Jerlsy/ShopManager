namespace ShopManager.Services;

public interface IAppDialogService
{
    Task<bool> ShowConfirmAsync(string title, string content,
        string confirmText = "確認", string cancelText = "取消");
}
