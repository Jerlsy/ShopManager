import sys
import glob

# Force UTF-8 output
sys.stdout.reconfigure(encoding='utf-8')

# Check the exact bytes around FFFD in each file
files_to_check = {
    'Views/ShiftSettings/ShiftSettingPage.xaml': [6304],
    'Views/Schedule/SchedulePage.xaml': [11563, 15451, 22431],
    'Views/SalarySettings/SalarySettingPage.xaml': [10673, 11044, 11791],
    'Views/EmployeeManagement/EmployeeListPage.xaml': [7096, 13650, 13677],
}

for filepath, positions in files_to_check.items():
    print(f"\n=== {filepath} ===")
    with open(filepath, 'rb') as f:
        raw = f.read()

    content = raw.decode('utf-8', errors='replace')

    for char_pos in positions:
        ctx_start = max(0, char_pos - 30)
        ctx_end = min(len(content), char_pos + 30)
        ctx = content[ctx_start:ctx_end]
        ctx_safe = ctx.encode('ascii', errors='replace').decode('ascii')

        # Find byte offset
        prefix_bytes = content[:char_pos].encode('utf-8', errors='replace')
        byte_offset = len(prefix_bytes)
        surrounding_bytes = raw[byte_offset-2:byte_offset+6]
        print(f"  Char pos {char_pos}, bytes ~{byte_offset}: {' '.join(f'{b:02X}' for b in surrounding_bytes)}")
        print(f"  Context: {ctx_safe}")
        # Show unicode codepoints
        for i, c in enumerate(ctx):
            if ord(c) > 127:
                print(f"    Non-ASCII at pos {ctx_start+i}: U+{ord(c):04X} ({c.encode('unicode_escape').decode()})")
