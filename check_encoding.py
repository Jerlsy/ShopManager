import glob

files = []
for ext in ['*.cs', '*.xaml', '*.md']:
    files.extend(glob.glob('**/' + ext, recursive=True))

for f in sorted(files):
    parts = f.replace('\\', '/').split('/')
    if any(x in parts for x in ['.vs', 'bin', 'obj']):
        continue
    try:
        data = open(f, 'rb').read()
        try:
            content = data.decode('utf-8')
            has_chinese = any(0x4e00 <= ord(c) <= 0x9fff for c in content)
            has_mojibake = '???' in content
            if has_mojibake:
                print('CORRUPT: ' + f)
            elif has_chinese:
                print('HAS_CHINESE: ' + f)
        except UnicodeDecodeError:
            print('NOT_UTF8: ' + f)
    except Exception as e:
        print('ERROR: ' + f)
