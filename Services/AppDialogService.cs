using MaterialDesignThemes.Wpf;
using ShopManager.Views.Dialogs;

namespace ShopManager.Services;

public class AppDialogService : IAppDialogService
{
    public async Task<bool> ShowConfirmAsync(string title, string content,
        string confirmText = "確認", string cancelText = "取消")
    {
        var view = new ConfirmDialogView(title, content, confirmText, cancelText);
        var result = await DialogHost.Show(view, "RootDialog");
        // CommandParameter 為 bool.TrueString ("True")
        return result is string s && s == bool.TrueString;
    }
}
