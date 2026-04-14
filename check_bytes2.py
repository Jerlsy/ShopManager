import glob

files = [
    'Views/EmployeeManagement/EmployeeListPage.xaml',
    'Views/SalarySettings/SalarySettingPage.xaml',
    'Views/Schedule/SchedulePage.xaml',
    'Views/ShiftSettings/ShiftSettingPage.xaml',
    'Views/ShopSelection/ShopSelectionWindow.xaml',
    'DEVLOG.md',
]

for filepath in files:
    with open(filepath, 'rb') as f:
        raw = f.read()

    # Count 0x3F (?) replacements and find FFFD (U+FFFD)
    q_count = raw.count(b'\x3f')
    fffd_count = raw.count(b'\xef\xbf\xbd')  # U+FFFD in UTF-8

    # Decode as UTF-8 to find FFFD positions
    content = raw.decode('utf-8', errors='replace')
    fffd_positions = [i for i, c in enumerate(content) if c == '\ufffd']

    print(f"\n{filepath}:")
    print(f"  0x3F '?' bytes: {q_count}")
    print(f"  U+FFFD bytes: {fffd_count}")
    if fffd_positions:
        # Show context around first few FFFD
        for pos in fffd_positions[:3]:
            start = max(0, pos-20)
            end = min(len(content), pos+20)
            ctx = content[start:end].replace('\ufffd', '[FFFD]')
            print(f"  FFFD at {pos}: ...{ctx!r}...")
