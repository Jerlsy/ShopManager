"""
全面修復所有 XAML 檔案中的中文亂碼。
處理兩種損壞類型:
  ? (U+003F)   = 被損壞的 CJK 字元或全形標點
  \ufffd       = 被損壞的其他 Unicode 字元 (·, —, ×, ║ 等)
"""
import sys
sys.stdout = open(sys.stdout.fileno(), 'w', encoding='utf-8', closefd=False)

FFFD = '\ufffd'  # U+FFFD replacement character


def apply_fixes(filepath, fixes):
    content = open(filepath, 'r', encoding='utf-8', errors='replace').read()
    original = content
    applied, skipped = [], []

    for old, new in fixes:
        if old in content:
            content = content.replace(old, new, 1)
            applied.append(repr(old[:60]))
        else:
            skipped.append(repr(old[:60]))

    if content != original:
        open(filepath, 'w', encoding='utf-8').write(content)
        print(f'✓ {filepath}: {len(applied)} 項已修復')
    else:
        print(f'- {filepath}: 無變更')

    if skipped:
        print(f'  未找到 ({len(skipped)} 項):')
        for s in skipped:
            print(f'    {s}')


# ─────────────────────────────────────────────
# ShiftSettingPage.xaml
# ─────────────────────────────────────────────
shift_fixes = [
    # 中間點分隔符 (· = U+00B7)
    (f'<Run Text=" {FFFD} "/>', '<Run Text=" · "/>'),
    # 小時單位
    ('<Run Text=" ??"/>', '<Run Text=" 小時"/>'),
    # 空白提示文字
    ('Text="?????????????,????????"',
     'Text="請點選左側班別，或點選新增班別即可在此編輯"'),
    # 表單標題轉換器
    ("ConverterParameter='????|????'", "ConverterParameter='新增班別|編輯班別'"),
    # 班別名稱 Hint
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="班別名稱"'),
    # 上班時間標籤 (第 1 個 Text="????")
    ('Text="????"', 'Text="上班時間"'),
    # 下班時間標籤 (第 2 個 Text="????")
    ('Text="????"', 'Text="下班時間"'),
    # 工作時數 Run
    ('<Run Text="????:"/>', '<Run Text="工作時數:"/>'),
    # 選擇顏色標題 (3 個 ? 對應 選擇顏色, 腳本原作者已確認)
    ('Text="???"', 'Text="選擇顏色"'),
    # 啟用此班別
    ('Text="?????"', 'Text="啟用此班別"'),
    # 儲存按鈕
    ('Text="??"', 'Text="儲存"'),
    # 取消按鈕
    ('Content="??"', 'Content="取消"'),
]
apply_fixes('Views/ShiftSettings/ShiftSettingPage.xaml', shift_fixes)


# ─────────────────────────────────────────────
# SalarySettingPage.xaml
# ─────────────────────────────────────────────
salary_fixes = [
    # 頁首區域 comment
    ('<!-- ????? + ????? -->', '<!-- 頁首標題列 + 操作按鈕欄 -->'),
    # 薪資設定 標題
    ('Text="????"', 'Text="薪資設定"'),
    # 勞基法設定按鈕
    ('Text="??????"', 'Text="勞基法設定"'),
    # 新增薪資按鈕
    ('Text="????"', 'Text="新增薪資"'),
    # 主欄 comment
    ('<!-- ???:???? + ????                   -->', '<!-- 主要欄:薪資清單 + 編輯表單                   -->'),
    # 薪資列表 comment
    ('<!-- ???? -->', '<!-- 薪資列表 -->'),
    # 薪資編輯欄 comment (Grid.Column=1)
    ('<!-- ????? -->', '<!-- 薪資編輯欄 -->'),
    # 表單標題轉換器
    ("ConverterParameter='??????|??????'", "ConverterParameter='新增薪資方案|編輯薪資方案'"),
    # 薪資名稱 Hint
    ('md:HintAssist.Hint="???? *(?:????)"', 'md:HintAssist.Hint="薪資名稱 *(?:正職底薪)"'),
    # 備註 Hint
    ('md:HintAssist.Hint="??(??)"', 'md:HintAssist.Hint="備註(選填)"'),
    # 薪資類型 comment
    ('<!-- ???? -->', '<!-- 薪資類型 -->'),
    # 薪資類型標籤
    ('Text="???? *"', 'Text="薪資類型 *"'),
    # 薪資類型 ComboBox Hint
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="薪資類型"'),
    # 時薪組 comment
    ('<!-- ??? -->', '<!-- 時薪組 -->'),
    # 時薪 Hint
    ('md:HintAssist.Hint="??(?)"', 'md:HintAssist.Hint="時薪(元)"'),
    # 月薪組 comment
    ('<!-- ??? -->', '<!-- 月薪組 -->'),
    # 月薪 Hint
    ('md:HintAssist.Hint="??(?)"', 'md:HintAssist.Hint="月薪(元)"'),
    # 合約組 comment
    ('<!-- ??? -->', '<!-- 合約組 -->'),
    # 合約金額 Hint
    ('md:HintAssist.Hint="????(?)"', 'md:HintAssist.Hint="合約金額(元)"'),
    # 結算週期 Hint
    ('md:HintAssist.Hint="????(?:??????)"', 'md:HintAssist.Hint="結算週期(?:每月結算一次)"'),
    # 加班設定 Expander comment
    ('<!-- ???? Expander -->', '<!-- 加班設定 Expander -->'),
    # 加班設定 Header
    ('Expander Header="??????(???????)"', 'Expander Header="加班計算設定(不填則依勞基法)"'),
    # 加班費倍率1 (每薪資) × via FFFD
    (f'md:HintAssist.Hint="??????1({FFFD})"', 'md:HintAssist.Hint="加班費倍率1(×)"'),
    # 加班費倍率2 (每薪資) × via FFFD
    (f'md:HintAssist.Hint="??????2({FFFD})"', 'md:HintAssist.Hint="加班費倍率2(×)"'),
    # 假日加班倍率 (每薪資) × via FFFD
    (f'md:HintAssist.Hint="??????({FFFD})"', 'md:HintAssist.Hint="假日加班倍率(×)"'),
    # 每日最長工時 Hint
    ('md:HintAssist.Hint="????????(?)"', 'md:HintAssist.Hint="每日最長工時(小時)"'),
    # 每週最長工時 Hint
    ('md:HintAssist.Hint="????????(?)"', 'md:HintAssist.Hint="每週最長工時(小時)"'),
    # 儲存按鈕
    ('Text="??"', 'Text="儲存"'),
    # 取消按鈕
    ('Content="??"', 'Content="取消"'),
    # 空白提示 comment
    ('<!-- ????? -->', '<!-- 空白提示 -->'),
    # 空白提示文字
    ('Text="??????????,?????????"', 'Text="選取左側薪資方案，或建立新方案。"'),
    # 右側欄 comment
    ('<!-- ????:??????                        -->', '<!-- 右側欄:勞基法設定                        -->'),
    # 提示 InfoBar comment
    ('<!-- ?? InfoBar -->', '<!-- 提示 InfoBar -->'),
    # 以下數值說明
    ('Text="??????????????,????????????,???????????????"',
     'Text="以下數值僅供參考，實際薪資計算以勞動部公告數值為準。"'),
    # 時薪設定 Expander (1st)
    ('Expander Header="?????" IsExpanded="True"', 'Expander Header="時薪設定" IsExpanded="True"'),
    # 最低時薪 Hint
    ('md:HintAssist.Hint="????(?)"', 'md:HintAssist.Hint="最低時薪(元)"'),
    # 每日最長工時 Hint (時薪)
    ('md:HintAssist.Hint="????????(?)"', 'md:HintAssist.Hint="每日最長工時(小時)"'),
    # 每週最長工時 Hint (時薪)
    ('md:HintAssist.Hint="????????(?)"', 'md:HintAssist.Hint="每週最長工時(小時)"'),
    # 加班費倍率1 (時薪, 第2小時起) × via FFFD
    (f'md:HintAssist.Hint="??????1(?2??{FFFD})"', 'md:HintAssist.Hint="加班費倍率1(第2小時起×)"'),
    # 加班費倍率2 (時薪, 第3小時後) × via FFFD
    (f'md:HintAssist.Hint="??????2(?3???{FFFD})"', 'md:HintAssist.Hint="加班費倍率2(第3小時後×)"'),
    # 月薪設定 Expander (2nd) — comment
    ('<!-- ??? -->', '<!-- 月薪組 -->'),
    # 月薪設定 Expander (2nd)
    ('Expander Header="?????" IsExpanded="True"', 'Expander Header="月薪設定" IsExpanded="True"'),
    # 最低月薪 Hint
    ('md:HintAssist.Hint="????(?)"', 'md:HintAssist.Hint="最低月薪(元)"'),
    # 每日最長工時 Hint (月薪)
    ('md:HintAssist.Hint="????????(?)"', 'md:HintAssist.Hint="每日最長工時(小時)"'),
    # 每週最長工時 Hint (月薪)
    ('md:HintAssist.Hint="????????(?)"', 'md:HintAssist.Hint="每週最長工時(小時)"'),
    # 加班費倍率1 (月薪) × via FFFD
    (f'md:HintAssist.Hint="??????1({FFFD})"', 'md:HintAssist.Hint="加班費倍率1(×)"'),
    # 加班費倍率2 (月薪) × via FFFD
    (f'md:HintAssist.Hint="??????2({FFFD})"', 'md:HintAssist.Hint="加班費倍率2(×)"'),
    # 加班費 & 假日費 Expander
    ('<!-- ???? -->', '<!-- 加班假日費 -->'),
    ('Expander Header="???? &amp; ????" IsExpanded="True"',
     'Expander Header="加班費 &amp; 假日費" IsExpanded="True"'),
    # 假日加班費率 Hint × via FFFD
    (f'md:HintAssist.Hint="??????({FFFD})"', 'md:HintAssist.Hint="假日加班費率(×)"'),
    # 每月最長加班
    ('md:HintAssist.Hint="??????(?)"', 'md:HintAssist.Hint="每月最長加班(小時)"'),
    # 儲存勞基法設定 comment
    ('<!-- ?? -->', '<!-- 儲存 -->'),
    # 儲存勞基法設定按鈕
    ('Text="??????"', 'Text="儲存勞基法設定"'),
    # 關閉勞基法設定按鈕
    ('Content="??????"', 'Content="關閉勞基法設定"'),
    # 頁首標題 comment (edit panel)
    ('<!-- ????? + ????? -->', '<!-- 頁首標題 + 操作按鈕 -->'),
    # 薪資編輯欄 comment (duplicate, just in case)
    ('<!-- ????? -->', '<!-- 薪資編輯欄 -->'),
    # 空白提示 comment (duplicate)
    ('<!-- ????? -->', '<!-- 空白提示 -->'),
]
apply_fixes('Views/SalarySettings/SalarySettingPage.xaml', salary_fixes)


# ─────────────────────────────────────────────
# EmployeeListPage.xaml
# ─────────────────────────────────────────────
emp_fixes = [
    # 標題列 comment
    ('<!-- ??? -->', '<!-- 標題列 -->'),
    # 員工資料管理 標題
    ('Text="??????"', 'Text="員工資料管理"'),
    # 新增員工按鈕
    ('Text="????"', 'Text="新增員工"'),
    # 主區域 comment
    ('<!-- ??? -->', '<!-- 主區域 -->'),
    # 卡片清單 comment (帶空白對齊)
    ('<!-- ????                                     -->', '<!-- 卡片清單                                     -->'),
    # 空狀態 comment
    ('<!-- ??? -->', '<!-- 空狀態 -->'),
    # 空狀態文字
    ('Text="??????,???????????????"',
     'Text="尚無員工資料，點選右上角「新增員工」開始建立"'),
    # 員工卡片 comment
    ('<!-- ???? -->', '<!-- 員工卡片 -->'),
    # 頭像 + 姓名 comment
    ('<!-- ?? + ?? -->', '<!-- 頭像 + 姓名 -->'),
    # 已離職標記 comment
    ('<!-- ????? -->', '<!-- 已離職標記 -->'),
    # 已離職文字
    ('Text="???"', 'Text="已離職"'),
    # 資訊列 comment
    ('<!-- ??? -->', '<!-- 資訊列 -->'),
    # 電話 FallbackValue (em dash — was corrupted to ASCII ?)
    ("FallbackValue='?'", "FallbackValue='—'"),
    # 未設定班別
    ("FallbackValue='?????'", "FallbackValue='未設定班別'"),
    # 未設定薪資
    ("FallbackValue='?????'", "FallbackValue='未設定薪資'"),
    # 到職日 StringFormat
    ('StringFormat=??:{0:yyyy/MM/dd}', 'StringFormat=到職：{0:yyyy/MM/dd}'),
    # 操作按鈕 comment (卡片內)
    ('<!-- ???? -->', '<!-- 操作按鈕 -->'),
    # 編輯按鈕
    ('Text="??"', 'Text="編輯"'),
    # 編輯側欄 comment
    ('<!-- ????                                     -->', '<!-- 編輯側欄                                     -->'),
    # 標題 comment
    ('<!-- ?? -->', '<!-- 標題 -->'),
    # 新增員工|編輯員工資料
    ("ConverterParameter='????|??????'", "ConverterParameter='新增員工|編輯員工資料'"),
    # ║ 1. 基本資料 ║ comment
    (f'<!-- {FFFD} 1. ????                  {FFFD} -->', '<!-- ║ 1. 基本資料                  ║ -->'),
    # 基本資料 section header
    ('Text="????"', 'Text="基本資料"'),
    # 姓名 Hint
    ('md:HintAssist.Hint="?? *"', 'md:HintAssist.Hint="姓名 *"'),
    # 身分證字號 Hint
    ('md:HintAssist.Hint="?????"', 'md:HintAssist.Hint="身分證字號"'),
    # 聯絡地址 Hint (1st occurrence)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="聯絡地址"'),
    # 聯繫電話 Hint (2nd occurrence)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="聯繫電話"'),
    # 通訊軟體 comment
    ('<!-- ???? -->', '<!-- 通訊軟體 -->'),
    # 通訊軟體標籤
    ('Text="????(???)"', 'Text="通訊軟體（限一組）"'),
    # 類型 Hint (ComboBox for messenger type)
    ('md:HintAssist.Hint="??"', 'md:HintAssist.Hint="類型"'),
    # 帳號/ID Hint
    ('md:HintAssist.Hint="?? / ID"', 'md:HintAssist.Hint="帳號 / ID"'),
    # 自訂聯絡方式 comment
    ('<!-- ?????? -->', '<!-- 自訂聯絡方式 -->'),
    # 自訂聯絡方式標籤
    ('Text="??????"', 'Text="自訂聯絡方式"'),
    # 新增按鈕 (自訂聯絡)
    ('Text="??"', 'Text="新增"'),
    # 名稱（自訂） Hint
    ('md:HintAssist.Hint="??(??)"', 'md:HintAssist.Hint="名稱（自訂）"'),
    # 值 Hint
    ('md:HintAssist.Hint="?"', 'md:HintAssist.Hint="值"'),
    # ║ 2. 排班設定 ║ comment
    (f'<!-- {FFFD} 2. ????                  {FFFD} -->', '<!-- ║ 2. 排班設定                  ║ -->'),
    # 排班設定 section header
    ('Text="????"', 'Text="排班設定"'),
    # 預設班別 Hint
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="預設班別"'),
    # 薪資類型 Hint
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="薪資類型"'),
    # InfoBar 預設值說明
    ('Text="?????????????,????????????"',
     'Text="預設班別與薪資僅作為預設值，排班與計薪時可個別調整。"'),
    # 排班規則 Expander comment
    ('<!-- ???? Expander -->', '<!-- 排班規則 Expander -->'),
    # 排班規則 Expander Header
    ('Header="????(??)"', 'Header="排班規則（選填）"'),
    # 固定休假 comment
    ('<!-- ???? -->', '<!-- 固定休假 -->'),
    # 固定休假日標籤
    ('Text="?????"', 'Text="固定休假日"'),
    # 排除班別 comment
    ('<!-- ???? -->', '<!-- 排除班別 -->'),
    # 排除班別標籤
    ('Text="????"', 'Text="排除班別"'),
    # 排班時不排入以下班別
    ('Text="??????????"', 'Text="排班時不排入以下班別"'),
    # 不與共事 comment
    ('<!-- ???? -->', '<!-- 不與共事 -->'),
    # 不與共事標籤
    ('Text="????"', 'Text="不與共事"'),
    # 同一班次中避免
    ('Text="????????????????"', 'Text="同一班次中避免與以下員工同時排班"'),
    # 目前無其他在職員工提示
    ('Text="(?????????)"', 'Text="（目前無其他在職員工）"'),
    # 預設獎金 comment
    ('<!-- ???? -->', '<!-- 預設獎金 -->'),
    # 預設獎金標籤
    ('Text="????(??)"', 'Text="預設獎金（選填）"'),
    # 新增按鈕 (獎金)
    ('Text="??"', 'Text="新增"'),
    # 獎金名稱 Hint
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="獎金名稱"'),
    # 金額(元) Hint
    ('md:HintAssist.Hint="??(?)"', 'md:HintAssist.Hint="金額（元）"'),
    # ║ 3. 任職資料 ║ comment
    (f'<!-- {FFFD} 3. ????                  {FFFD} -->', '<!-- ║ 3. 任職資料                  ║ -->'),
    # 任職資料 section header
    ('Text="????"', 'Text="任職資料"'),
    # 到職日標籤
    ('Text="??? *"', 'Text="到職日 *"'),
    # 離職日標籤
    ('Text="???(?? = ??)"', 'Text="離職日（留空 = 在職）"'),
    # 排班衝突警告 comment
    ('<!-- ?????? -->', '<!-- 排班衝突警告 -->'),
    # 衝突提示文字
    ('Text="???????????,????????????:"',
     'Text="離職日之後仍有排班資料，請重新編輯以下月份的班表："'),
    # 操作按鈕 comment (底部)
    ('<!-- ???? -->', '<!-- 操作按鈕 -->'),
    # 儲存按鈕
    ('Text="??"', 'Text="儲存"'),
    # 取消按鈕
    ('Content="??"', 'Content="取消"'),
    # 強制儲存 ToolTip
    ('ToolTip="??????,???????"', 'ToolTip="忽略排班衝突，強制儲存離職日"'),
    # 強制儲存按鈕文字
    ('Text="????"', 'Text="強制儲存"'),
]
apply_fixes('Views/EmployeeManagement/EmployeeListPage.xaml', emp_fixes)


# ─────────────────────────────────────────────
# SchedulePage.xaml
# ─────────────────────────────────────────────
sched_fixes = [
    # 左側：員工清單面板 comment (1st)
    ('<!-- ??:??????                    -->', '<!-- 左側：員工清單面板                    -->'),
    # 右側：行事曆主區域 comment (2nd)
    ('<!-- ??:??????                    -->', '<!-- 右側：行事曆主區域                    -->'),
    # 在職員工 標題
    ('Text="????"', 'Text="在職員工"'),
    # 點選員工提示
    ('Text="(?????????,?????????)"', 'Text="（點選員工以檢視限制，拖放到班別格子排班）"'),
    # 頂部工具列 comment
    ('<!-- -- ????? --------------- -->', '<!-- -- 頂部工具列 --------------- -->'),
    # 月份導覽 comment (1st ???? comment)
    ('<!-- ???? -->', '<!-- 月份導覽 -->'),
    # 今天按鈕
    ('Content="??"', 'Content="今天"'),
    # 視圖切換 + 功能操作區 comment
    ('<!-- ???? + ????? -->', '<!-- 視圖切換 + 功能操作區 -->'),
    # 複製上週 comment
    ('<!-- ????(????????)-->', '<!-- 複製上週(班表已建立才顯示)-->'),
    # 複製上週按鈕文字 (1st Text="????")
    ('Text="????"', 'Text="複製上週"'),
    # 批次排班 comment
    ('<!-- ????(????????)-->', '<!-- 批次排班(班表已建立才顯示)-->'),
    # 批次排班按鈕文字 (2nd Text="????")
    ('Text="????"', 'Text="批次排班"'),
    # 新增班表 comment
    ('<!-- ????(????????)-->', '<!-- 新增班表(班表未建立才顯示)-->'),
    # 新增班表按鈕文字 (3rd Text="????")
    ('Text="????"', 'Text="新增班表"'),
    # 建立班表對話框 comment
    ('<!-- -- ??????? -------------- -->', '<!-- -- 建立班表對話框 -------------- -->'),
    # 新增班表對話框標題 (4th Text="????")
    ('Text="????"', 'Text="新增班表"'),
    # 年標籤
    ('Text="?"', 'Text="年"'),
    # 月標籤
    ('Text="?"', 'Text="月"'),
    # 該月店休日標籤
    ('Text="?????(???)"', 'Text="該月店休日（可調整）"'),
    # 建立按鈕
    ('Content="??"', 'Content="建立"'),
    # 取消按鈕 (create dialog)
    ('Content="??"', 'Content="取消"'),
    # 排班編輯面板 comment
    ('<!-- -- ?????? -------------- -->', '<!-- -- 排班編輯面板 -------------- -->'),
    # 編輯排班 Run + 中間點 (· via FFFD)
    (f'<Run Text="???? {FFFD} "/>', '<Run Text="編輯排班 · "/>'),
    # 班別標籤 (edit entry)
    ('Text="??"', 'Text="班別"'),
    # 備註（選填）標籤
    ('Text="??(??)"', 'Text="備註（選填）"'),
    # 選擇班別 Hint (1st)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="選擇班別"'),
    # 輸入備註 Hint
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="輸入備註"'),
    # 儲存按鈕 (edit entry)
    ('Text="??"', 'Text="儲存"'),
    # 取消按鈕 (edit entry)
    ('Content="??"', 'Content="取消"'),
    # 快速新增班次面板 comment
    ('<!-- -- ???????? ----------- -->', '<!-- -- 快速新增班次面板 ----------- -->'),
    # 標題 comment (quick add)
    ('<!-- ?? -->', '<!-- 標題 -->'),
    # 快速新增排班 Run + 中間點
    (f'<Run Text="?????? {FFFD} "/>', '<Run Text="快速新增排班 · "/>'),
    # 員工標籤 comment (quick add)
    ('<!-- ?? -->', '<!-- 員工 -->'),
    # 員工標籤文字
    ('Text="??"', 'Text="員工"'),
    # 選擇員工 Hint (quick add)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="選擇員工"'),
    # 班別標籤 comment (quick add)
    ('<!-- ?? -->', '<!-- 班別 -->'),
    # 班別標籤文字 (quick add)
    ('Text="??"', 'Text="班別"'),
    # 選擇班別 Hint (quick add)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="選擇班別"'),
    # 儲存按鈕 (quick add)
    ('Text="??"', 'Text="儲存"'),
    # 取消按鈕 (quick add)
    ('Content="??"', 'Content="取消"'),
    # 批次排班面板 comment
    ('<!-- -- ?????? -------------- -->', '<!-- -- 批次排班面板 -------------- -->'),
    # 批次排班標題 comment
    ('<!-- ?? -->', '<!-- 批次排班 -->'),
    # 批次排班標題文字 (6 chars)
    ('Text="??????"', 'Text="批次排班設定"'),
    # 員工 + 班別 comment
    ('<!-- ?? + ?? -->', '<!-- 員工 + 班別 -->'),
    # 員工 * 標籤
    ('Text="?? *"', 'Text="員工 *"'),
    # 選擇員工 Hint (batch)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="選擇員工"'),
    # 班別 * 標籤
    ('Text="?? *"', 'Text="班別 *"'),
    # 選擇班別 Hint (batch)
    ('md:HintAssist.Hint="????"', 'md:HintAssist.Hint="選擇班別"'),
    # 日期範圍 comment
    ('<!-- ???? -->', '<!-- 日期範圍 -->'),
    # 開始日期 * 標籤
    ('Text="???? *"', 'Text="開始日期 *"'),
    # 開始 Hint
    ('md:HintAssist.Hint="??"', 'md:HintAssist.Hint="開始"'),
    # → 分隔符 (between start/end date, FFFD)
    (f'Text="{FFFD}"', 'Text="→"'),
    # 結束日期 * 標籤
    ('Text="???? *"', 'Text="結束日期 *"'),
    # 結束 Hint
    ('md:HintAssist.Hint="??"', 'md:HintAssist.Hint="結束"'),
    # 套用星期 comment
    ('<!-- ???? -->', '<!-- 套用星期 -->'),
    # 套用星期標籤 (5th Text="????")
    ('Text="????"', 'Text="套用星期"'),
    # 確認批次排班按鈕
    ('Text="??????"', 'Text="確認批次排班"'),
    # 取消按鈕 (batch)
    ('Content="??"', 'Content="取消"'),
    # 空白班表提示 comment
    ('<!-- -- ?????? -------------- -->', '<!-- -- 空白班表提示 -------------- -->'),
    # 此月份尚未建立班表
    ('Text="?????????"', 'Text="此月份尚未建立班表"'),
    # 請點擊新增班表
    ('Text="?????????????"', 'Text="請點擊「新增班表」開始建立"'),
    # 排班視圖欄 comment (section wrapper)
    ('<!-- ?????                         -->', '<!-- 月視圖區域                       -->'),
    # 月視圖 comment
    ('<!-- ??? -->', '<!-- 月視圖 -->'),
    # 星期標題列 comment
    ('<!-- ????? -->', '<!-- 星期標題列 -->'),
    # 日曆格子 comment
    ('<!-- ???? -->', '<!-- 日曆格子 -->'),
    # 日期與星期 comment (inside calendar cell)
    ('<!-- ????? -->', '<!-- 日期與星期 -->'),
    # 休 indicator (1 char)
    ('Text="?"', 'Text="休"'),
    # 班別色塊 comment
    (f'<!-- ???? (??1+2: ??????? chip {FFFD}) -->', '<!-- 班別色塊 (待辦1+2: 員工名稱顯示的 chip ×) -->'),
    # Border 操作 comment
    ('<!-- ?? Border:????? -->', '<!-- 點擊 Border:班次操作項 -->'),
    # chips comment
    ('<!-- ?? chips(????/??)-->', '<!-- 員工 chips(已排員工/停用)-->'),
    # ContextMenu items (1st set, month view)
    ('MenuItem Header="?????"', 'MenuItem Header="編輯此班次"'),
    ('MenuItem Header="????????"', 'MenuItem Header="複製到下一週排班"'),
    ('MenuItem Header="?????"', 'MenuItem Header="刪除此班次"'),
    # 周/日視圖 — 時間軸 comment
    (f'<!-- ?/??? {FFFD} ??? -->', '<!-- 周/日視圖 — 時間軸 -->'),
    # 周視圖日期標題列 comment
    ('<!-- ????????? -->', '<!-- 周視圖的日期標題列 -->'),
    # 時間軸列 comment
    ('<!-- ???? -->', '<!-- 時間軸列 -->'),
    # ContextMenu items (2nd set, week/day view) — replace remaining occurrences
    ('MenuItem Header="?????"', 'MenuItem Header="編輯此班次"'),
    ('MenuItem Header="????????"', 'MenuItem Header="複製到下一週排班"'),
    ('MenuItem Header="?????"', 'MenuItem Header="刪除此班次"'),
]
apply_fixes('Views/Schedule/SchedulePage.xaml', sched_fixes)

print('\n完成。請執行 check_encoding.py 確認修復結果。')
