using System.Windows;
using System.Windows.Threading;

namespace ShopManager.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 更新狀態文字並強制立即算圖。啟動流程此時仍在同步執行（DB 遷移／佈景主題套用等），
    /// Dispatcher 訊息迴圈尚未真正開始跑，一般的屬性變更要等到迴圈跑起來才會畫到畫面上；
    /// 用 Render 優先權跑一個巢狀 Dispatcher frame，逼出這一次的版面配置＋算圖。
    /// </summary>
    public void SetStatus(string text)
    {
        StatusText.Text = text;
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
}
