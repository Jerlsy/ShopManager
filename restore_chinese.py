"""
Restore corrupted Chinese text in XAML files.
Each ? = one CJK character that was replaced.
Each U+FFFD = a non-CJK Unicode char (·, —, ┌, ┐, ×, etc.) that was replaced.
"""
import sys
sys.stdout.reconfigure(encoding='utf-8')

FFFD = '\ufffd'

fixes = {
    'Views/ShiftSettings/ShiftSettingPage.xaml': [
        # page title
        ('Text="????"',
         'Text="班別設定"',
         {'after': 'FontSize="30"'}),
        # subtitle description (15+15)
        ('Text="???????????????,???????????????"',
         'Text="為各班別設定打卡時間與識別顏色，排班時可直接套用並快速新增排班"', {}),
        # new shift button
        ('Text="????"',
         'Text="新增班別"',
         {'after': 'Kind="Plus"'}),
        # list panel title
        ('Text="????"',
         'Text="班別列表"',
         {'after': 'FontSize="20"'}),
        # disabled badge
        (f'Text="??"',
         'Text="停用"',
         {'after': 'AppInfoBrush'}),
        # shift hours unit (inside Run text)
        ('<Run Text=" ??"/>',
         '<Run Text=" 小時"/>',
         {}),
        # hours label
        ('<Run Text="????:"/>',
         '<Run Text="工作時數:"/>',
         {}),
        # color section label
        ('Text="???"',
         'Text="選擇顏色"',
         {'after': 'ColorPalette'}),
        # enable toggle label
        ('Text="?????"',
         'Text="啟用此班別"',
         {'after': 'ToggleButton'}),
        # form title converter
        ("ConverterParameter='????|????'",
         "ConverterParameter='新增班別|編輯班別'",
         {}),
        # alias hint
        ('md:HintAssist.Hint="????"',
         'md:HintAssist.Hint="班別名稱"',
         {'after': 'EditAlias'}),
        # start time label
        ('Text="????"',
         'Text="上班時間"',
         {'after': 'EditStartTimeText'}),
        # end time label
        ('Text="????"',
         'Text="下班時間"',
         {'after': 'EditEndTimeText'}),
        # save button
        ('Text="??"',
         'Text="儲存"',
         {'after': 'SaveCommand'}),
        # cancel button
        ('Content="??"',
         'Content="取消"',
         {'after': 'CancelCommand'}),
        # FFFD: middle dot separator between time and hours
        (f'<Run Text=" {FFFD} "/>',
         '<Run Text=" · "/>',
         {}),
        # hint for placeholder state
        ('Text="?????????????,????????"',
         'Text="請點選左側班別，或點選新增班別即可在此編輯"',
         {}),
    ],
    'Views/ShopSelection/ShopSelectionWindow.xaml': [
        ('Text="????"',
         'Text="選擇店鋪"',
         {'after': 'FontSize="28"'}),
        ('Text="?????????,??????????????"',
         'Text="請選擇要進入的店鋪，或建立一個全新的店鋪。"',
         {}),
        ('md:HintAssist.Hint="????"',
         'md:HintAssist.Hint="選擇店鋪"',
         {'after': 'SelectedShop'}),
        ('md:HintAssist.Hint="????????"',
         'md:HintAssist.Hint="輸入新店鋪名稱"',
         {}),
        ('Text="???????,????????????,??????"',
         'Text="選擇現有店鋪進入管理系統，或在下方輸入名稱建立新店鋪。"',
         {}),
        ('Content="????"',
         'Content="進入系統"',
         {}),
    ],
    'Views/SalarySettings/SalarySettingPage.xaml': [
        # page title
        ('Text="????"',
         'Text="薪資設定"',
         {'after': 'FontSize="30"'}),
        # labor law button
        ('Text="??????"',
         'Text="勞基法設定"',
         {'after': 'OpenLaborLawCommand'}),
        # new salary button
        ('Text="????"',
         'Text="新增薪資"',
         {'after': 'StartNewCommand'}),
        # comment: left/right panels
        ('<!-- ???:???? + ????                   -->',
         '<!-- 主欄:薪資清單 + 編輯表單                   -->',
         {}),
        # comment: salary list
        ('<!-- ???? -->',
         '<!-- 薪資列表 -->',
         {'after': 'ListBox Grid.Column="0"'}),
        # form title converter
        ("ConverterParameter='??????|??????'",
         "ConverterParameter='新增薪資方案|編輯薪資方案'",
         {}),
        # alias hint
        ('md:HintAssist.Hint="???? *(?:????)"',
         'md:HintAssist.Hint="薪資名稱 *(?:正職底薪)"',
         {}),
        # description hint
        ('md:HintAssist.Hint="??(??)"',
         'md:HintAssist.Hint="備註(選填)"',
         {}),
        # salary type section
        ('<!-- ???? -->',
         '<!-- 薪資類型 -->',
         {'after': 'SalaryTypes'}),
        ('Text="???? *"',
         'Text="薪資類型 *"',
         {}),
        ('md:HintAssist.Hint="????"',
         'md:HintAssist.Hint="薪資類型"',
         {'after': 'EditType'}),
        # hourly section
        ('<!-- ??? -->',
         '<!-- 時薪組 -->',
         {'after': 'Hourly'}),
        ('md:HintAssist.Hint="???(?)"',
         'md:HintAssist.Hint="時薪(元)"',
         {'after': 'EditHourlyRate'}),
        # monthly section
        ('<!-- ??? -->',
         '<!-- 月薪組 -->',
         {'after': 'Monthly'}),
        ('md:HintAssist.Hint="???(?)"',
         'md:HintAssist.Hint="月薪(元)"',
         {'after': 'EditMonthlyBase'}),
        # contract section
        ('<!-- ??? -->',
         '<!-- 合約組 -->',
         {'after': 'Contract'}),
        ('md:HintAssist.Hint="????(?)"',
         'md:HintAssist.Hint="合約金額(元)"',
         {'after': 'EditContractAmount'}),
        ('md:HintAssist.Hint="????(?:??????)"',
         'md:HintAssist.Hint="結算週期(?:每月結算一次)"',
         {}),
        # overtime expander
        ('<!-- ???? Expander -->',
         '<!-- 加班設定 Expander -->',
         {}),
        ('Expander Header="??????(???????)"',
         'Expander Header="加班計算設定(不填則依勞基法)"',
         {}),
        # save/cancel
        ('Text="??"',
         'Text="儲存"',
         {'after': 'SaveCommand'}),
        ('Content="??"',
         'Content="取消"',
         {'after': 'CancelCommand'}),
        # empty state
        ('Text="??????????,?????????"',
         'Text="選取左側薪資方案，或建立新方案。"',
         {}),
        # comment: labor law section
        ('<!-- ????:?????? -->',
         '<!-- 右側欄:勞基法設定 -->',
         {}),
        # info bar
        ('Text="??????????????,????????????,???????????????"',
         'Text="以下數值僅供參考，實際薪資計算以勞動部公告數值為準。"',
         {}),
        # hourly expander
        ('Expander Header="?????" IsExpanded="True"',
         'Expander Header="時薪設定" IsExpanded="True"',
         {'after': 'HourlyMinimumWage'}),
        # hourly hints
        ('md:HintAssist.Hint="????(?)"',
         'md:HintAssist.Hint="最低時薪(元)"',
         {'after': 'HourlyMinimumWage'}),
        ('md:HintAssist.Hint="????????(?)"',
         'md:HintAssist.Hint="每日最長工時(小時)"',
         {'after': 'HourlyDailyMaxHours'}),
        ('md:HintAssist.Hint="????????(?)"',
         'md:HintAssist.Hint="每週最長工時(小時)"',
         {'after': 'HourlyWeeklyMaxHours'}),
        # monthly expander
        ('Expander Header="?????" IsExpanded="True"',
         'Expander Header="月薪設定" IsExpanded="True"',
         {'after': 'MonthlyMinimumWage'}),
        # monthly hints
        ('md:HintAssist.Hint="????(?)"',
         'md:HintAssist.Hint="最低月薪(元)"',
         {'after': 'MonthlyMinimumWage'}),
        ('md:HintAssist.Hint="????????(?)"',
         'md:HintAssist.Hint="每日最長工時(小時)"',
         {'after': 'MonthlyDailyMaxHours'}),
        ('md:HintAssist.Hint="????????(?)"',
         'md:HintAssist.Hint="每週最長工時(小時)"',
         {'after': 'MonthlyWeeklyMaxHours'}),
        # overtime & holiday expander
        ('Expander Header="???? &amp; ????" IsExpanded="True"',
         'Expander Header="加班費 &amp; 假日費" IsExpanded="True"',
         {}),
        ('md:HintAssist.Hint="??????(?)"',
         'md:HintAssist.Hint="假日加班費率(倍)"',
         {'after': 'HolidayOTRate'}),
        ('md:HintAssist.Hint="??????(?)"',
         'md:HintAssist.Hint="每月最長加班(小時)"',
         {'after': 'MaxMonthlyOTHours'}),
        # save labor law
        ('Text="??????"',
         'Text="儲存勞基法設定"',
         {}),
        ('Content="??????"',
         'Content="關閉勞基法設定"',
         {}),
        # comment: header + buttons
        ('<!-- ????? + ????? -->',
         '<!-- 頁首標題 + 操作按鈕 -->',
         {}),
        # comment: edit panel
        ('<!-- ????? -->',
         '<!-- 薪資編輯欄 -->',
         {}),
        # comment: empty state
        ('<!-- ????? -->',
         '<!-- 空白提示 -->',
         {'after': 'IsEditing'}),
        # FFFD in overtime rate hints
        (f'md:HintAssist.Hint="??????1({FFFD})"',
         'md:HintAssist.Hint="加班費倍率1(×)"',
         {}),
        (f'md:HintAssist.Hint="??????2({FFFD})"',
         'md:HintAssist.Hint="加班費倍率2(×)"',
         {}),
        (f'md:HintAssist.Hint="??????({FFFD})"',
         'md:HintAssist.Hint="假日加班倍率(×)"',
         {}),
        # per-salary OT hints (inside the salary form expander)
        ('md:HintAssist.Hint="??????1(▲)"',
         'md:HintAssist.Hint="加班費倍率1(×)"',
         {}),
        ('md:HintAssist.Hint="??????2(▲)"',
         'md:HintAssist.Hint="加班費倍率2(×)"',
         {}),
        ('md:HintAssist.Hint="??????(▲)"',
         'md:HintAssist.Hint="假日加班倍率(×)"',
         {}),
        # daily/weekly max hours in salary form
        ('md:HintAssist.Hint="????????(?)"',
         'md:HintAssist.Hint="每日最長工時(小時)"',
         {'after': 'EditDailyMaxHours'}),
        ('md:HintAssist.Hint="????????(?)"',
         'md:HintAssist.Hint="每週最長工時(小時)"',
         {'after': 'EditWeeklyMaxHours'}),
        # labor law OT hints
        ('md:HintAssist.Hint="??????1(?2??▲)"',
         'md:HintAssist.Hint="加班費倍率1(第2小時起×)"',
         {}),
        ('md:HintAssist.Hint="??????2(?3???▲)"',
         'md:HintAssist.Hint="加班費倍率2(第3小時後×)"',
         {}),
        ('md:HintAssist.Hint="??????1(▲)"',
         'md:HintAssist.Hint="加班費倍率1(×)"',
         {}),
        ('md:HintAssist.Hint="??????2(▲)"',
         'md:HintAssist.Hint="加班費倍率2(×)"',
         {}),
    ],
}

def apply_fixes(filepath, fix_list):
    with open(filepath, 'r', encoding='utf-8', errors='replace') as f:
        content = f.read()

    original = content
    applied = []
    skipped = []

    for item in fix_list:
        if len(item) == 3:
            old, new, ctx = item
        else:
            old, new = item
            ctx = {}

        if old in content:
            content = content.replace(old, new, 1)
            applied.append(old[:50])
        else:
            skipped.append(old[:50])

    if content != original:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"\n✓ {filepath}: {len(applied)} fixes applied")
    else:
        print(f"\n- {filepath}: no changes")

    if skipped:
        print(f"  SKIPPED ({len(skipped)}): ")
        for s in skipped:
            print(f"    {s!r}")

    return content != original


# Apply all fixes
for filepath, fix_list in fixes.items():
    apply_fixes(filepath, fix_list)

print("\nDone.")
