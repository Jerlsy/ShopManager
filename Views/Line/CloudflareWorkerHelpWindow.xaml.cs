using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace ShopManager.Views.Line;

public partial class CloudflareWorkerHelpWindow : Window
{
    private const string WorkerCode =
        """
        export default {
          async fetch(request, env) {
            const url = new URL(request.url);

            // ShopManager 查詢好友清單
            if (request.method === 'GET' && url.pathname === '/followers') {
              if (request.headers.get('X-Api-Key') !== env.API_KEY)
                return new Response('Unauthorized', { status: 401 });

              const list = await env.FOLLOWERS_KV.list();
              const followers = await Promise.all(
                list.keys.map(k => env.FOLLOWERS_KV.get(k.name, 'json'))
              );
              return Response.json(followers.filter(Boolean));
            }

            // LINE Webhook 接收事件
            if (request.method === 'POST' && url.pathname === '/webhook') {
              const body = await request.text();
              if (!await verifySignature(env.LINE_CHANNEL_SECRET, body, request.headers.get('X-Line-Signature')))
                return new Response('Invalid signature', { status: 401 });

              const { events } = JSON.parse(body);
              for (const event of events) {
                const userId = event.source?.userId;
                if (!userId) continue;

                if (event.type === 'follow' || event.type === 'message') {
                  const existing = event.type === 'message'
                    ? await env.FOLLOWERS_KV.get(userId)
                    : null;
                  if (!existing) {
                    const profile = await fetchProfile(env.LINE_CHANNEL_ACCESS_TOKEN, userId);
                    await env.FOLLOWERS_KV.put(userId, JSON.stringify({
                      userId,
                      displayName: profile.displayName ?? '未知',
                      pictureUrl:  profile.pictureUrl  ?? null,
                      followedAt:  new Date().toISOString()
                    }));
                  }
                } else if (event.type === 'unfollow') {
                  await env.FOLLOWERS_KV.delete(userId);
                }
              }
              return new Response('OK');
            }

            return new Response('Not found', { status: 404 });
          }
        };

        async function verifySignature(secret, body, signature) {
          if (!signature) return false;
          const enc = new TextEncoder();
          const key = await crypto.subtle.importKey(
            'raw', enc.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']
          );
          const mac = await crypto.subtle.sign('HMAC', key, enc.encode(body));
          const expected = btoa(String.fromCharCode(...new Uint8Array(mac)));
          return expected === signature;
        }

        async function fetchProfile(token, userId) {
          const res = await fetch(`https://api.line.me/v2/bot/profile/${userId}`, {
            headers: { Authorization: `Bearer ${token}` }
          });
          return res.ok ? res.json() : {};
        }
        """;

    public CloudflareWorkerHelpWindow()
    {
        InitializeComponent();
        WorkerCodeBox.Text = WorkerCode;
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(WorkerCode);
        CopyCodeLabel.Text = "已複製 ✓";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { CopyCodeLabel.Text = "複製程式碼"; timer.Stop(); };
        timer.Start();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://dash.cloudflare.com") { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
