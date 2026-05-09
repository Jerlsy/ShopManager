namespace ShopManager.Services;

public interface IAppDialogService
{
    Task<bool> ShowConfirmAsync(string title, string content,
        string confirmText = "確認", string cancelText = "取消");

    /// <returns>true = 儲存並離開, false = 不儲存直接離開, null = 取消</returns>
    Task<bool?> ShowUnsavedChangesAsync();
}
