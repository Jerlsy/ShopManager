namespace ShopManager.Services;

/// <summary>
/// 郵件轉推播機制要部署到 Google Apps Script 的完整原始碼，嵌在部署指南視窗裡讓使用者複製貼上。
/// 這支腳本只讀寫 Drive 上固定檔名的規則/心跳檔，規則異動全部由 ShopManager 端覆蓋同一份 JSON，
/// 腳本本身部署一次之後不需要再修改或重新部署。
/// </summary>
public static class GmailAppsScriptTemplate
{
    public const string SourceCode = """
        /**
         * ShopManager × LINE 郵件轉推播
         *
         * 讀取 ShopManager 上傳到 Google Drive 的規則 JSON，撈出符合條件的 Gmail 信件、
         * 擷取內容、推播給指定業主的 LINE，再打上「已推播」標籤。規則/Token 異動由
         * ShopManager 端覆蓋同一個雲端 JSON 檔案，這支程式部署一次之後不用再重新部署。
         *
         * 標籤（每條規則各一組）：
         *   LinePush/Done/{ruleId}    已成功推播，不再處理
         *   LinePush/Try1、Try2/…     推播失敗的累計次數
         *   LinePush/Failed/{ruleId}  連續失敗達上限已放棄；手動撕掉這個標籤即可重推
         *
         * 心跳檔（執行狀態）寫回設定裡指定的 statusFileId，那個檔案由 ShopManager 建立，
         * 不要改成自己 DriveApp.createFile —— ShopManager 只看得到自己建立的檔案，
         * 這邊另外建一個的話它永遠讀不到，部署狀態會一直顯示「尚未部署」。
         */

        const CONFIG_FILE_ID_PROP = 'CONFIG_FILE_ID';
        const STATUS_FILE_NAME = 'shopmanager-mail-status.json';

        /** 推播失敗最多重試幾次就放棄。LINE token 失效這種永久性錯誤不該每 5 分鐘重打一次。 */
        const MAX_ATTEMPTS = 3;

        /**
         * 只處理近幾天內的信件，不掃整個信箱歷史。
         *
         * 沒有這個限制的話，第一次部署、或之後新增一條規則時，會把符合條件的所有歷史信件
         * 一次性推播出去——例如新增一條「主旨含存款入帳通知」的規則，信箱裡三年份的舊信會
         * 瞬間灌爆業主的 LINE。7 天同時也是服務中斷後回來要補處理多久份的緩衝：一般 gmail.com
         * 帳號的每日配額用完、或帳號授權失效這幾天沒能執行，回復後 7 天內的信仍補得到。
         */
        const RECENT_DAYS = 7;

        /**
         * 唯一的排程進入點：撈信 → 推播 → 打「已推播」標籤，一次做完。
         *
         * 刻意不拆成「標記」「推播」兩支：拆開等於兩倍的觸發次數（一般 gmail.com 帳號每天只有
         * 90 分鐘的觸發器執行時間），延遲是兩段間隔相加，還多出兩種競態——心跳檔互相覆蓋，
         * 以及「移除 Pending → 加上 Done」空窗期被重新標記造成的重複推播。合併之後全部消失。
         *
         * 重試靠標籤而不是狀態：搜尋條件排除已推播標籤，推播失敗就不打標籤，下一輪自然重撈。
         */
        function forwardMail() {
          // 上一輪還沒跑完就跳過這輪（信件多時單次執行可能超過排程間隔），避免同一封被推兩次
          const lock = LockService.getScriptLock();
          if (!lock.tryLock(10000)) {
            console.log('上一輪尚未結束，略過這一輪');
            return;
          }
          try {
            forwardMailLocked_();
          } finally {
            lock.releaseLock();
          }
        }

        function forwardMailLocked_() {
          let config = null, statusFile = null, configError = null;
          try {
            config = readConfig_();
            statusFile = DriveApp.getFileById(config.statusFileId);
          } catch (e) {
            configError = String(e);
          }

          // 設定讀不到時仍盡力把錯誤寫進心跳檔——不然 ShopManager 只會顯示「尚未部署」，
          // 使用者完全看不出是設定檔過期還是根本沒部署。真的連心跳檔都拿不到才放棄。
          if (configError) {
            console.error('讀取設定失敗：' + configError);
            if (!statusFile) {
              try {
                const files = DriveApp.getFilesByName(STATUS_FILE_NAME);
                if (files.hasNext()) statusFile = files.next();
              } catch (ignored) { /* 找不到就真的沒辦法回報了 */ }
            }
            if (!statusFile) return;
            writeStatus_(statusFile, { lastRunOk: false, lastError: configError, pushedCountLastRun: 0 });
            return;
          }

          let pushed = 0, givenUp = 0;
          let ok = true, error = null;
          try {
            (config.rules || []).forEach(rule => {
              const query = buildQuery_(rule);
              if (!query) return;                                   // 沒有條件的規則會撈到整個信箱
              const owners = rule.owners || [];
              if (owners.length === 0) return;                      // 沒有推播對象，推了也沒意義

              const done   = getOrCreateLabel_(doneLabelName_(rule.id));
              const failed = failedLabelName_(rule.id);

              // 已推播、以及重試上限後放棄的，都不再撈進來
              GmailApp.search(`${query} -label:${done.getName()} -label:${failed}`).forEach(thread => {
                const msg = thread.getMessages().slice(-1)[0];

                // 用 getBody()（HTML）自己轉，不要用 getPlainBody()：Gmail 的轉換不理會 CSS，
                // 銀行拿 font-size:0 藏的隱形字元會混進值裡（存入帳號前面那個「/」就是）。
                const pairs = extractPairs_(msg.getBody() || msg.getPlainBody(), rule.contentFields);

                // 全部推成功才算成功。有人失敗就整條算失敗、下一輪重試——寧可少數人收到重複通知，
                // 也不要有人漏收（要做到「只補推失敗的那位」得為每位對象各開一組標籤，不值得）。
                let allOk = true;
                owners.forEach(owner => {
                  if (!pushToLine_(config.lineChannelAccessToken, owner.userId, msg, owner.name, pairs)) {
                    allOk = false;
                  }
                });

                const attempts = countAttempts_(thread, rule.id) + 1;
                clearAttemptLabels_(thread, rule.id);

                if (allOk) {
                  thread.addLabel(done);
                  pushed++;
                } else if (attempts >= MAX_ATTEMPTS) {
                  // 連續失敗到上限就貼「Failed」放棄，不再無限重打。
                  // 排除掉問題後（例如換了 LINE token），在 Gmail 手動撕掉這個標籤就會重推。
                  thread.addLabel(getOrCreateLabel_(failed));
                  givenUp++;
                  console.error(`推播失敗 ${attempts} 次，放棄：${msg.getSubject()}`);
                } else {
                  thread.addLabel(getOrCreateLabel_(attemptLabelName_(rule.id, attempts)));
                  console.warn(`推播失敗第 ${attempts} 次，下一輪重試：${msg.getSubject()}`);
                }
              });
            });
          } catch (e) {
            ok = false;
            error = String(e);
          }

          writeStatus_(statusFile, {
            lastRunOk: ok, lastError: error,
            pushedCountLastRun: pushed, givenUpCountLastRun: givenUp,
          });
        }

        function writeStatus_(statusFile, fields) {
          const status = readStatus_(statusFile);
          status.lastRunAt = new Date().toISOString();
          status.lastRunOk = fields.lastRunOk;
          status.lastError = fields.lastError;
          status.pushedCountLastRun = fields.pushedCountLastRun;
          status.givenUpCountLastRun = fields.givenUpCountLastRun || 0;
          statusFile.setContent(JSON.stringify(status));
        }

        // ── 重試次數以標籤記錄 ────────────────────────────────────────────────
        // 放在標籤而不是 ScriptProperties：在 Gmail 裡直接看得到哪些信卡住、卡在第幾次，
        // 而且信件被刪除時記錄會跟著消失，不會殘留一堆對不到信的計數。

        function attemptLabelName_(ruleId, n) { return `LinePush/Try${n}/${ruleId}`; }
        function failedLabelName_(ruleId)     { return `LinePush/Failed/${ruleId}`; }

        /** 這封信到目前為止失敗過幾次（0 = 這是第一次嘗試） */
        function countAttempts_(thread, ruleId) {
          const names = thread.getLabels().map(l => l.getName());
          for (let n = MAX_ATTEMPTS - 1; n >= 1; n--) {
            if (names.indexOf(attemptLabelName_(ruleId, n)) >= 0) return n;
          }
          return 0;
        }

        function clearAttemptLabels_(thread, ruleId) {
          const names = thread.getLabels().map(l => l.getName());
          for (let n = 1; n < MAX_ATTEMPTS; n++) {
            const name = attemptLabelName_(ruleId, n);
            if (names.indexOf(name) >= 0) thread.removeLabel(GmailApp.getUserLabelByName(name));
          }
        }

        function setupTriggers() {
          ScriptApp.getProjectTriggers().forEach(t => {
            if (t.getHandlerFunction() === 'forwardMail') ScriptApp.deleteTrigger(t);
          });
          // 每 5 分鐘一次。Apps Script 的 everyMinutes 只接受 1、5、10、15、30。
          // 想更即時可以改成 1，但一般 gmail.com 帳號的觸發器每天只有 90 分鐘總執行時間，
          // 每分鐘跑（一天 1440 次）在規則變多時有機會把配額用完而整個停擺。
          ScriptApp.newTrigger('forwardMail').timeBased().everyMinutes(5).create();
          console.log('已建立觸發器：forwardMail 每 5 分鐘');
        }

        /**
         * 自我診斷：在編輯器選這個函式按執行，「執行記錄」會一次列出所有可能卡住的環節。
         * 推播沒動作時先跑這支，不要猜。
         */
        function diagnose() {
          const log = [];
          const fileId = PropertiesService.getScriptProperties().getProperty(CONFIG_FILE_ID_PROP);
          log.push(fileId ? '✓ CONFIG_FILE_ID = ' + fileId
                          : '✗ 沒有設定 CONFIG_FILE_ID（部署步驟第 3 點）');

          let config = null;
          if (fileId) {
            try {
              config = JSON.parse(DriveApp.getFileById(fileId).getBlob().getDataAsString());
              log.push('✓ 讀得到設定檔，更新時間 ' + (config.updatedAt || '(無)'));
            } catch (e) {
              log.push('✗ 讀不到設定檔：' + e);
            }
          }

          if (config) {
            log.push(config.statusFileId
              ? '✓ statusFileId = ' + config.statusFileId
              : '✗ 設定檔沒有 statusFileId → 請在 ShopManager 按一次「更新上傳到雲端」重新產生');
            log.push(config.lineChannelAccessToken ? '✓ 有 LINE token' : '✗ 沒有 LINE token');

            if (config.statusFileId) {
              try {
                DriveApp.getFileById(config.statusFileId).getBlob().getDataAsString();
                log.push('✓ 心跳檔可存取');
              } catch (e) {
                log.push('✗ 心跳檔存取失敗：' + e);
              }
            }

            const rules = config.rules || [];
            log.push('規則數量：' + rules.length);
            rules.forEach(r => {
              const q = buildQuery_(r);
              const owners = (r.owners || []).length;
              if (!q) { log.push(`  ✗ 規則 ${r.id}：沒有任何寄件者/主旨條件，會被略過`); return; }
              if (owners === 0) { log.push(`  ✗ 規則 ${r.id}：沒有推播對象，會被略過`); return; }
              const done = doneLabelName_(r.id), failed = failedLabelName_(r.id);
              const hits = GmailApp.search(q).length;
              const pending = GmailApp.search(`${q} -label:${done} -label:${failed}`).length;
              const givenUp = GmailApp.search(`label:${failed}`).length;
              log.push(`  ✓ 規則 ${r.id}：查詢「${q}」符合 ${hits} 封，其中 ${pending} 封待推播；推播對象 ${owners} 位` +
                       (givenUp > 0 ? `；⚠ ${givenUp} 封重試 ${MAX_ATTEMPTS} 次失敗已放棄（撕掉 ${failed} 標籤可重推）` : ''));
            });
          }

          const triggers = ScriptApp.getProjectTriggers()
            .filter(t => t.getHandlerFunction() === 'forwardMail')
            .map(t => t.getHandlerFunction());
          log.push(triggers.length > 0
            ? '✓ 已安裝觸發器：forwardMail'
            : '✗ 沒有安裝觸發器 → 請執行一次 setupTriggers');

          console.log(log.join('\n'));
          return log.join('\n');
        }

        /** 已推播標籤：兼作「不要重複推播」的判斷依據，撕掉標籤就會重推一次 */
        function doneLabelName_(ruleId) { return `LinePush/Done/${ruleId}`; }

        /** 只處理近 7 天內的信件，見 RECENT_DAYS 說明 */
        function buildQuery_(rule) {
          const parts = [`newer_than:${RECENT_DAYS}d`];
          if (rule.senderContains)  parts.push(`from:(${rule.senderContains})`);
          if (rule.subjectContains) parts.push(`subject:(${rule.subjectContains})`);
          // 兩個條件都沒填時只剩時間限制，等於撈近 7 天全部的信，跟「沒有任何條件」一樣危險，直接視為無效
          return parts.length > 1 ? parts.join(' ') : '';
        }

        function getOrCreateLabel_(name) {
          return GmailApp.getUserLabelByName(name) || GmailApp.createLabel(name);
        }

        // ── 正文擷取 ──────────────────────────────────────────────────────────
        //
        // 分兩層：通用清理（HTML→文字、去轉寄標頭/圖片/連結）對任何信都成立；
        // 「哪些才是重點」則由規則自己指定欄位名稱（存入金額、授權金額…），
        // 因為每個寄件者的版面差異太大，沒有可靠的通用判斷方式。
        //
        // ⚠️ 規則需與 ShopManager 的 MailBodyExtractor.cs 保持一致，設定頁的「測試」用那支預覽。

        function htmlToText_(html) {
          let t = (html || '').replace(/<(script|style|head)[^>]*>[\s\S]*?<\/\1>/gi, '');
          // 視覺上隱藏的元素連同內容丟掉：銀行會用 font-size:0 在數值中間插隱形字元干擾解析
          // （中國信託的存入帳號前面那個「/」就是），行銷信也常用 display:none 藏預覽文字。
          t = t.replace(/<(\w+)[^>]*style\s*=\s*(['"])[^'"]*(display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0(?![.1-9])|mso-hide\s*:\s*all|opacity\s*:\s*0(?![.1-9]))[^'"]*\2[^>]*>[\s\S]*?<\/\1>/gi, '');
          // HTML 原始碼裡的換行／縮排只是排版空白，先壓成空格；少了這步，
          // 「<td>存入帳號</td>\n<td>822-…</td>」會被拆成兩行，標籤與值就對不起來。
          t = t.replace(/\s+/g, ' ');
          t = t.replace(/<br\s*\/?>/gi, '\n');
          t = t.replace(/<\/(p|div|tr|table|h[1-6]|li)>/gi, '\n');
          t = t.replace(/<\/t[dh]>/gi, '\t');   // 儲存格間用 tab，避免標籤與值黏在一起
          t = t.replace(/<[^>]+>/g, '');
          return t.replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<')
                  .replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'")
                  .replace(/&#(\d+);/g, (m, d) => String.fromCharCode(d));
        }

        function toLines_(body) {
          let text = (body || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
          if (/<[a-z][\s\S]*>/i.test(text)) text = htmlToText_(text);
          text = text.replace(/\[image:[^\]]*\]/g, '');
          text = text.replace(/<https?:\/\/[^>]*>/g, '');
          text = text.replace(/https?:\/\/\S+/g, '');

          // 轉寄標頭逐行判斷，不用「找到空行為止」——郵件換行實務上很亂（\r\n、\r\r\n、
          // 欄位之間夾空行都有），拿空行當界線會在第一個換行就誤判結束。
          const SEPARATOR = /^-{3,}\s*(轉寄的郵件|轉寄郵件|Forwarded message|原始郵件)\s*-{3,}$/;
          const HEADER_FIELD = /^(寄件者|日期|主旨|收件者|副本|密件副本|From|Sent|Date|Subject|To|Cc|Bcc)\s*[:：]/i;

          const lines = [];
          let inForwardHeader = false;
          for (const raw of text.split('\n')) {
            const line = raw.replace(/[ \t 　]+/g, ' ').trim();
            if (SEPARATOR.test(line)) { inForwardHeader = true; continue; }
            if (inForwardHeader) {
              if (line.length === 0) continue;              // 標頭欄位之間的空行
              if (HEADER_FIELD.test(line)) continue;        // 寄件者／日期／主旨／收件者…
              inForwardHeader = false;                      // 第一行真正的內容，標頭結束
            }

            if (line.length > 1) lines.push(line);
          }
          return lines;
        }

        /**
         * 依樣板產生卡片列，回傳 [{label, value, isHeading}]。樣板每一行是：
         *   [文字]  → 字面文字，不進比對，原樣顯示在這個順序位置（當標題／分隔說明用）
         *   其他    → 欄位名稱，去信件內文找它後面的值
         * 樣板留空、或一個欄位都沒抓到值時，退回清理後的全文。
         */
        function extractPairs_(body, template) {
          const lines = toLines_(body);
          const fullText = () => {
            const j = lines.join('\n');
            return [{ label: '', value: j.length > 900 ? j.slice(0, 900) + '…' : j, isHeading: true }];
          };
          if (!template || template.length === 0) return fullText();

          const isLiteral = s => { const t = s.trim(); return t.length >= 2 && t[0] === '[' && t.slice(-1) === ']'; };
          const stripBrackets = s => { const t = s.trim(); return isLiteral(t) ? t.slice(1, -1).trim() : t; };

          // 只有真正要比對的欄位名稱才參與「同一行的下一個欄位」切斷判斷
          const fieldNames = template.filter(t => !isLiteral(t)).map(t => t.trim());

          const rows = [];
          template.forEach(entry => {
            if (isLiteral(entry)) {
              const text = stripBrackets(entry);
              if (text) rows.push({ label: '', value: text, isHeading: true });
              return;
            }
            const value = findValue_(lines, entry.trim(), fieldNames);
            if (value !== null) rows.push({ label: entry.trim(), value: value, isHeading: false });
          });

          if (!rows.some(r => !r.isHeading)) return fullText();
          return rows;
        }

        function findValue_(lines, label, fieldNames) {
          const LEAD = /^[\s:：．・\-—–|｜=＝]+/;
          const TRAIL = /[\s:：．・\-—–|｜]+$/;

          for (let i = 0; i < lines.length; i++) {
            const pos = lines[i].indexOf(label);
            if (pos < 0) continue;

            const afterLabel = lines[i].slice(pos + label.length);
            let rest = afterLabel.replace(LEAD, '');

            // 同一行還有其他欄位名稱時，值只取到下一個欄位之前
            let cut = rest.length;
            fieldNames.forEach(other => {
              if (other === label) return;
              const p = rest.indexOf(other);
              if (p >= 0) cut = Math.min(cut, p);
            });
            rest = rest.slice(0, cut);

            // 值被排版拆到下一行（標籤與值分屬不同 <tr>）才往下抓。條件是「標籤後面原本有分隔符、
            // 只是分隔符後面沒東西」；若標籤後面根本沒分隔符，那多半是標題行，硬抓下一行只會拿到不相干的內文。
            const hadSeparator = afterLabel.trim().length > 0;
            if (!rest.trim() && hadSeparator && i + 1 < lines.length) rest = lines[i + 1];

            const value = rest.trim().replace(TRAIL, '');
            if (!value) continue;   // 這次命中沒有值，繼續往後找下一個出現的位置
            return value.length > 200 ? value.slice(0, 200) + '…' : value;
          }
          return null;
        }

        // ── LINE 推播（Flex 卡片）──────────────────────────────────────────────

        function buildFlexBubble_(msg, ownerName, pairs) {
          const rows = pairs.map(p => {
            // [文字] 的字面標題：整列一行、深色粗體，當作卡片內的小節標題
            if (p.isHeading && p.label === '') {
              return { type: 'text', text: p.value, size: 'sm', color: '#333333', weight: 'bold', wrap: true };
            }
            return { type: 'box', layout: 'horizontal', spacing: 'sm', contents: [
              { type: 'text', text: p.label, size: 'sm', color: '#888888', flex: 4, wrap: true },
              { type: 'text', text: p.value, size: 'sm', color: '#222222', flex: 6, wrap: true, weight: 'bold' },
            ] };
          });

          return {
            type: 'bubble',
            header: {
              type: 'box', layout: 'vertical', backgroundColor: '#4A90D9', paddingAll: '16px', spacing: 'xs',
              contents: [
                { type: 'text', text: ownerName || '通知', size: 'xs', color: '#FFFFFFB0', wrap: true },
                { type: 'text', text: trim_(msg.getSubject() || '（無主旨）', 60),
                  weight: 'bold', size: 'lg', color: '#FFFFFF', wrap: true },
              ],
            },
            body: {
              type: 'box', layout: 'vertical', paddingAll: '16px', spacing: 'md',
              contents: rows.concat([
                { type: 'separator', margin: 'md' },
                { type: 'text', size: 'xxs', color: '#AAAAAA', margin: 'md',
                  text: Utilities.formatDate(msg.getDate(), 'GMT+8', 'yyyy/MM/dd HH:mm') + ' 收件' },
              ]),
            },
          };
        }

        function trim_(text, max) {
          return text.length > max ? text.slice(0, max) + '…' : text;
        }

        /** 先送 Flex 卡片，若被 LINE 拒絕（版面格式問題）自動退回純文字，確保通知不會整個漏掉 */
        function pushToLine_(token, userId, msg, ownerName, pairs) {
          const altText = trim_((msg.getSubject() || '新信件通知') + '｜' +
            pairs.map(p => (p.label ? p.label + ' ' : '') + p.value).join('　'), 380);

          if (postToLine_(token, { to: userId, messages: [
                { type: 'flex', altText: altText, contents: buildFlexBubble_(msg, ownerName, pairs) }] })) {
            return true;
          }

          console.warn('Flex 推播失敗，改以純文字重送');
          const text = `【${msg.getSubject() || '新信件通知'}】\n` +
            pairs.map(p => (p.label ? p.label + '：' : '') + p.value).join('\n') + '\n' +
            Utilities.formatDate(msg.getDate(), 'GMT+8', 'yyyy/MM/dd HH:mm');
          return postToLine_(token, { to: userId, messages: [{ type: 'text', text: trim_(text, 4900) }] });
        }

        function postToLine_(token, payload) {
          const res = UrlFetchApp.fetch('https://api.line.me/v2/bot/message/push', {
            method: 'post',
            headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
            payload: JSON.stringify(payload),
            muteHttpExceptions: true,
          });
          if (res.getResponseCode() !== 200) console.error('LINE 推播失敗：' + res.getContentText());
          return res.getResponseCode() === 200;
        }

        function readConfig_() {
          const fileId = PropertiesService.getScriptProperties().getProperty(CONFIG_FILE_ID_PROP);
          if (!fileId) throw new Error('尚未設定 CONFIG_FILE_ID，請參考部署步驟第 3 點。');
          const config = JSON.parse(DriveApp.getFileById(fileId).getBlob().getDataAsString());
          if (!config.statusFileId) throw new Error('設定檔沒有 statusFileId，請在 ShopManager 重新上傳一次規則。');
          return config;
        }

        /** 心跳檔內容壞掉時當成空物件，不要讓它擋住這次執行的回報 */
        function readStatus_(statusFile) {
          try { return JSON.parse(statusFile.getBlob().getDataAsString()) || {}; }
          catch (e) { return {}; }
        }
        """;
}
