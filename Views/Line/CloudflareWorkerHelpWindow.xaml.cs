using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace ShopManager.Views.Line;

public partial class CloudflareWorkerHelpWindow : Window
{
    private const string WorkerCode =
        """
        export default {
          async fetch(request, env, ctx) {
            const url = new URL(request.url);

            // ShopManager 查詢好友／群組／多人聊天室清單
            if (request.method === 'GET' && url.pathname === '/followers') {
              if (request.headers.get('X-Api-Key') !== env.API_KEY)
                return new Response('Unauthorized', { status: 401 });

              const list = await env.FOLLOWERS_KV.list();
              const followers = await Promise.all(
                list.keys.map(k => env.FOLLOWERS_KV.get(k.name, 'json'))
              );
              return Response.json(followers.filter(Boolean));
            }

            // ── 圖片上傳（班表／薪資單推播用，LINE 圖片訊息需要一個公開網址）──
            if (request.method === 'POST' && url.pathname === '/upload-image') {
              if (request.headers.get('X-Api-Key') !== env.API_KEY)
                return new Response('Unauthorized', { status: 401 });

              const key  = `schedule-${Date.now()}.png`;
              const body = await request.arrayBuffer();
              await env.IMAGES_BUCKET.put(key, body, {
                httpMetadata: { contentType: 'image/png' }
              });
              // 換成你自己 R2 儲存桶「公開存取」開啟後產生的網址（見上一步）
              const imageUrl = `https://pub-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.r2.dev/${key}`;
              return Response.json({ url: imageUrl, key });
            }

            // ── 圖片刪除（推播完成後清除暫存）──────────────────────────────
            if (request.method === 'DELETE' && url.pathname === '/delete-image') {
              if (request.headers.get('X-Api-Key') !== env.API_KEY)
                return new Response('Unauthorized', { status: 401 });

              const key = url.searchParams.get('key');
              if (key) await env.IMAGES_BUCKET.delete(key);
              return new Response('OK');
            }

            // LINE Webhook 接收事件：立即回 200，實際處理丟到背景做，
            // 避免抓 Profile／查群組名稱等待太久讓 LINE 判定逾時重送
            if (request.method === 'POST' && url.pathname === '/webhook') {
              const body = await request.text();
              if (!await verifySignature(env.LINE_CHANNEL_SECRET, body, request.headers.get('X-Line-Signature')))
                return new Response('Invalid signature', { status: 401 });

              const { events } = JSON.parse(body);
              ctx.waitUntil(handleEvents(events, env));
              return new Response('OK');
            }

            return new Response('Not found', { status: 404 });
          },

          // 每天自動掃描修復抓取失敗、displayName 仍是「未知」的記錄
          async scheduled(event, env, ctx) {
            ctx.waitUntil(repairUnknownProfiles(env));
          }
        };

        // ── Webhook 事件處理 ────────────────────────────────────────────────
        // source.type 分三種：'user'（個人好友）、'group'（群組）、'room'（多人聊天室，
        // 沒有邀請群組名稱那種正式名稱，也沒有 API 可查，只能給預設名稱）。
        async function handleEvents(events, env) {
          for (const event of events) {
            try {
              const source = event.source;
              if (!source) continue;

              if (source.type === 'user') {
                const userId = source.userId;
                if (!userId) continue;

                if (event.type === 'follow') {
                  const existing = await env.FOLLOWERS_KV.get(userId, 'json');
                  const profile = await fetchProfileWithRetry(env.LINE_CHANNEL_ACCESS_TOKEN, userId, 1, 30_000);
                  await env.FOLLOWERS_KV.put(userId, JSON.stringify({
                    userId, type: 'user',
                    displayName: profile.displayName || existing?.displayName || '未知',
                    pictureUrl:  profile.pictureUrl  || existing?.pictureUrl  || null,
                    followedAt:  existing?.followedAt ?? new Date().toISOString()
                  }));
                } else if (event.type === 'message') {
                  const existing = await env.FOLLOWERS_KV.get(userId, 'json');
                  if (!existing) {
                    const profile = await fetchProfileWithRetry(env.LINE_CHANNEL_ACCESS_TOKEN, userId, 1, 30_000);
                    await env.FOLLOWERS_KV.put(userId, JSON.stringify({
                      userId, type: 'user',
                      displayName: profile.displayName ?? '未知',
                      pictureUrl:  profile.pictureUrl  ?? null,
                      followedAt:  new Date().toISOString()
                    }));
                  }
                } else if (event.type === 'unfollow') {
                  await env.FOLLOWERS_KV.delete(userId);
                }
              } else if (source.type === 'group' || source.type === 'room') {
                // 邀請機器人加入群組/多人聊天室 → join；移除或機器人自己離開 → leave
                const targetId = source.groupId || source.roomId;
                if (!targetId) continue;

                if (event.type === 'join') {
                  // LINE 官方帳號無法關閉「可被邀請加入群組」，任何人都能拉機器人進群組/聊天室。
                  // ALLOWED_GROUP_IDS 設定白名單（逗號分隔）後，不在清單內的一律自動退出，
                  // 不寫入 KV（不會出現在 ShopManager 候選名單裡）。留空＝不限制（沿用原行為）。
                  const allowList = (env.ALLOWED_GROUP_IDS || '')
                    .split(',').map(s => s.trim()).filter(Boolean);
                  if (allowList.length > 0 && !allowList.includes(targetId)) {
                    await leaveGroupOrRoom(env.LINE_CHANNEL_ACCESS_TOKEN, source.type, targetId);
                    continue;
                  }

                  // 群組有名稱可查；多人聊天室（room）LINE 沒有名稱這個概念，也沒有 API 可查，
                  // 只能用「多人聊天 + 加入時間」讓多個聊天室彼此分得出來
                  let displayName = source.type === 'group' ? '（未命名群組）' : `多人聊天 ${formatTaiwanTime(new Date())}`;
                  if (source.type === 'group') {
                    const summary = await fetchGroupSummary(env.LINE_CHANNEL_ACCESS_TOKEN, targetId);
                    if (summary?.groupName) displayName = summary.groupName;
                  }
                  await env.FOLLOWERS_KV.put(targetId, JSON.stringify({
                    userId: targetId, type: source.type,
                    displayName, pictureUrl: null,
                    followedAt: new Date().toISOString()
                  }));
                } else if (event.type === 'leave') {
                  await env.FOLLOWERS_KV.delete(targetId);
                }
              }
            } catch (err) {
              console.error('handleEvents error:', err);
            }
          }
        }

        // 重試版 fetchProfile：失敗後等 delayMs 再試 retries 次
        async function fetchProfileWithRetry(token, userId, retries, delayMs) {
          for (let attempt = 0; attempt <= retries; attempt++) {
            try {
              const res = await fetch(`https://api.line.me/v2/bot/profile/${userId}`, {
                headers: { Authorization: `Bearer ${token}` }
              });
              if (res.ok) return await res.json();
            } catch (_) { /* 網路錯誤 → 進入 retry */ }
            if (attempt < retries) await sleep(delayMs);
          }
          return {};
        }

        // 群組名稱查詢：LINE 只對「群組」提供，多人聊天室（room）沒有這支 API
        async function fetchGroupSummary(token, groupId) {
          try {
            const res = await fetch(`https://api.line.me/v2/bot/group/${groupId}/summary`, {
              headers: { Authorization: `Bearer ${token}` }
            });
            return res.ok ? await res.json() : null;
          } catch (_) { return null; }
        }

        // 白名單外的群組/聊天室：呼叫 LINE API 讓機器人自動退出
        async function leaveGroupOrRoom(token, type, targetId) {
          try {
            await fetch(`https://api.line.me/v2/bot/${type}/${targetId}/leave`, {
              method: 'POST',
              headers: { Authorization: `Bearer ${token}` }
            });
          } catch (err) { console.error('leaveGroupOrRoom error:', err); }
        }

        // 每天掃描 KV，修復抓取失敗的個人好友 displayName／pictureUrl，以及還沒查到名稱的群組
        async function repairUnknownProfiles(env) {
          let cursor;
          do {
            const list = await env.FOLLOWERS_KV.list({ cursor });
            for (const k of list.keys) {
              const data = await env.FOLLOWERS_KV.get(k.name, 'json');
              if (!data) continue;
              if (data.type === 'room') continue; // 多人聊天室沒有名稱 API 可補，略過

              if (data.type === 'group') {
                if (data.displayName && data.displayName !== '（未命名群組）') continue;
                const summary = await fetchGroupSummary(env.LINE_CHANNEL_ACCESS_TOKEN, k.name);
                if (summary?.groupName)
                  await env.FOLLOWERS_KV.put(k.name, JSON.stringify({ ...data, displayName: summary.groupName }));
                continue;
              }

              // type === 'user'（或舊資料沒有 type 欄位，視同 user）
              if (data.displayName !== '未知' && data.pictureUrl) continue;
              // 共嘗試 5 次（首次 + 4 次 retry），每次間隔 60 秒
              const profile = await fetchProfileWithRetry(env.LINE_CHANNEL_ACCESS_TOKEN, k.name, 4, 60_000);
              if (profile.displayName || profile.pictureUrl) {
                await env.FOLLOWERS_KV.put(k.name, JSON.stringify({
                  ...data,
                  displayName: profile.displayName || data.displayName,
                  pictureUrl:  profile.pictureUrl  || data.pictureUrl
                }));
              }
              // 5 次都失敗：跳過，等下次排程再試
            }
            cursor = list.cursor;
          } while (cursor);
        }

        function sleep(ms) {
          return new Promise(r => setTimeout(r, ms));
        }

        // 多人聊天室沒有名稱，用加入當下的台灣時間當顯示名稱區分不同聊天室
        function formatTaiwanTime(date) {
          const t = new Date(date.getTime() + 8 * 60 * 60 * 1000); // UTC+8
          const pad = n => String(n).padStart(2, '0');
          return `${t.getUTCFullYear()}/${pad(t.getUTCMonth() + 1)}/${pad(t.getUTCDate())} ` +
                 `${pad(t.getUTCHours())}:${pad(t.getUTCMinutes())}`;
        }

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
