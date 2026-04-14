import sys

# Check what's actually in a corrupt file
with open('Views/EmployeeManagement/EmployeeListPage.xaml', 'rb') as f:
    raw = f.read()

# Find a known pattern: "Text=" followed by possibly corrupt Chinese
idx = raw.find(b'<!-- ')
if idx >= 0:
    chunk = raw[idx:idx+60]
    print("Bytes around first comment:")
    print(' '.join(f'{b:02X}' for b in chunk))
    print(repr(chunk))

# Try to find non-ASCII bytes
non_ascii_positions = [(i, raw[i]) for i in range(min(2000, len(raw))) if raw[i] > 0x7F]
if non_ascii_positions:
    print(f"\nFound {len(non_ascii_positions)} non-ASCII bytes in first 2000 bytes")
    # Show a sample
    for pos, b in non_ascii_positions[:20]:
        print(f"  pos {pos}: 0x{b:02X}")
else:
    print("\nAll bytes are ASCII (0x00-0x7F) in first 2000 bytes")

# Try to check if file is pure ASCII
all_ascii = all(b <= 0x7F for b in raw)
print(f"\nFile is {'pure ASCII' if all_ascii else 'NOT pure ASCII'}, total {len(raw)} bytes")

# Check for UTF-8 BOM
if raw[:3] == b'\xef\xbb\xbf':
    print("Has UTF-8 BOM")

# Try decoding as Latin-1 and see if any chars decode to Latin-1 multibyte
try:
    latin1 = raw.decode('latin-1')
    has_high = any(ord(c) > 0x7F for c in latin1)
    print(f"As Latin-1: has_high_chars = {has_high}")
except:
    print("Cannot decode as Latin-1")

# If it's mojibake (UTF-8 read as Latin-1), try to reverse
try:
    if not all_ascii:
        # Try: interpret the file bytes as Latin-1, then re-encode as Latin-1 to get original UTF-8 bytes
        # i.e., raw bytes ARE the mojibake UTF-8 representation of Latin-1-read UTF-8
        # To reverse: raw -> decode as Latin-1 -> encode as Latin-1 -> decode as UTF-8
        reversed_attempt = raw.decode('utf-8').encode('latin-1').decode('utf-8')
        print(f"\nMojibake reversal attempt (first 200 chars):")
        print(reversed_attempt[:200])
except Exception as e:
    print(f"Reversal failed: {e}")
