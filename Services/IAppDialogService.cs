namespace ShopManager.Services;

public interface IAppDialogService
{
    Task<bool> ShowConfirmAsync(string title, string content,
        string confirmText = "確認", string cancelText = "取消");

    /// <returns>true = 儲存並離開, false = 不儲存直接離開, null = 取消</returns>
    Task<bool?> ShowUnsavedChangesAsync();

    /// <returns>true = 主要動作, false = 次要動作（紅字）, null = 取消</returns>
    Task<bool?> ShowChoiceAsync(string title, string content,
        string primaryText, string secondaryText, string cancelText = "取消");
}
