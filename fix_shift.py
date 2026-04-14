import sys
sys.stdout.reconfigure(encoding='utf-8')

FFFD = '\ufffd'

filepath = 'Views/ShiftSettings/ShiftSettingPage.xaml'
with open(filepath, 'r', encoding='utf-8', errors='replace') as f:
    content = f.read()

# Fix FFFD separator -> middle dot
old = f'<Run Text=" {FFFD} "/>'
new = '<Run Text=" · "/>'
if old in content:
    content = content.replace(old, new, 1)
    print("Fixed FFFD separator")
else:
    print(f"NOT FOUND: FFFD separator")

# Fix hours unit
old2 = '<Run Text=" ??"/>'
new2 = '<Run Text=" 小時"/>'
if old2 in content:
    content = content.replace(old2, new2, 1)
    print("Fixed hours unit")
else:
    print("NOT FOUND: hours unit")
    # Try to find it
    import re
    matches = [(m.start(), content[max(0,m.start()-20):m.end()+20]) for m in re.finditer(r'小時|??|<Run Text=" .."', content)]
    for pos, ctx in matches[:5]:
        print(f"  found at {pos}: {ctx!r}")

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print("Saved.")
