using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace ShopManager.Views.ShopSettings;

public partial class NotesEditorWindow : Window
{
    public string? SavedHtml { get; private set; }

    private readonly string? _initialHtml;
    private bool _editorReady;

    public NotesEditorWindow(string? initialHtml)
    {
        InitializeComponent();
        _initialHtml = initialHtml;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await EditorWebView.EnsureCoreWebView2Async(env);
        EditorWebView.CoreWebView2.NavigationCompleted += async (_, _) =>
        {
            if (_editorReady) return;
            _editorReady = true;

            // 等 Quill 初始化完成後再注入內容
            await Task.Delay(300);
            if (!string.IsNullOrEmpty(_initialHtml))
                await EditorWebView.ExecuteScriptAsync($"setHtml({JsonSerializer.Serialize(_initialHtml)})");
        };
        EditorWebView.NavigateToString(BuildEditorHtml());
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = await EditorWebView.ExecuteScriptAsync("typeof getHtml === 'function' ? getHtml() : ''");
            var html = JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            SavedHtml = string.IsNullOrWhiteSpace(html) ? null : html;
            DialogResult = true;
        }
        catch
        {
            // 編輯器未就緒（CDN 尚未載入），直接關閉並保留原始內容
            SavedHtml = _initialHtml;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── HTML 模板 ──────────────────────────────────────────────────────────
    private static string BuildEditorHtml() => """
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="UTF-8">
          <link href="https://cdn.quilljs.com/1.3.7/quill.snow.css" rel="stylesheet">
          <style>
            * { margin:0; padding:0; box-sizing:border-box; }
            html, body { height:100%; font-family:'Microsoft JhengHei UI','Microsoft JhengHei',sans-serif; }

            /* ── Light ──────────────────────────────────────── */
            body                        { background:#fff; color:#212121; }
            .ql-toolbar.ql-snow         { background:#f5f5f5; border-color:#ddd; }
            .ql-container.ql-snow       { border-color:#ddd; height:calc(100vh - 42px); }
            .ql-editor                  { font-size:14px; line-height:1.7; }
            .ql-editor.ql-blank::before { color:#999; font-style:normal; }

            /* ── Dark ───────────────────────────────────────── */
            @media (prefers-color-scheme: dark) {
              body                          { background:#1e1e1e; color:#d4d4d4; }
              .ql-toolbar.ql-snow           { background:#2a2a2a; border-color:#444; }
              .ql-container.ql-snow         { background:#1e1e1e; border-color:#444; }
              .ql-editor                    { color:#d4d4d4; }
              .ql-snow .ql-stroke           { stroke:#bbb; }
              .ql-snow .ql-fill             { fill:#bbb; }
              .ql-snow .ql-picker           { color:#bbb; }
              .ql-snow .ql-picker-options   { background:#2a2a2a; border-color:#444; }
              .ql-snow .ql-picker-label:hover,
              .ql-toolbar.ql-snow .ql-picker.ql-expanded .ql-picker-label,
              .ql-toolbar.ql-snow button:hover { background:#383838; border-radius:3px; }
              .ql-editor.ql-blank::before   { color:#555; }
            }

            /* ── Scrollbar ───────────────────────────────────── */
            ::-webkit-scrollbar            { width:6px; }
            ::-webkit-scrollbar-track      { background:transparent; }
            ::-webkit-scrollbar-thumb      { background:#aaa; border-radius:3px; }
          </style>
        </head>
        <body>
          <div id="editor"></div>
          <script src="https://cdn.quilljs.com/1.3.7/quill.min.js"></script>
          <script>
            var quill = new Quill('#editor', {
              theme: 'snow',
              placeholder: '輸入備註…',
              modules: {
                toolbar: [
                  ['bold', 'italic', 'underline', 'strike'],
                  [{ color: [] }, { background: [] }],
                  [{ size: ['small', false, 'large', 'huge'] }],
                  [{ list: 'ordered' }, { list: 'bullet' }],
                  [{ align: [] }],
                  ['link'],
                  ['clean']
                ]
              }
            });

            window.getHtml = function() {
              var h = quill.root.innerHTML;
              return (h === '<p><br></p>') ? '' : h;
            };
            window.setHtml = function(html) {
              if (html) quill.root.innerHTML = html;
              quill.setSelection(quill.getLength(), 0);
            };
          </script>
        </body>
        </html>
        """;
}
