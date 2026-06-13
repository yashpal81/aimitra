import pathlib
import re

root = pathlib.Path(r"i:\proj\aimitra\aimitra\src")
files = list(root.rglob('*.cs'))
text = {}
for f in files:
    try:
        text[f] = f.read_text(encoding='utf-8')
    except Exception:
        text[f] = f.read_text(encoding='utf-8', errors='ignore')

symbols = {}
for path, content in text.items():
    for m in re.finditer(r'^(?:\s*(?:public|internal|private|protected|protected internal|private protected)\s+)?(?:static\s+)?(?:sealed\s+)?(?:abstract\s+)?(?:partial\s+)?(?:class|interface|struct|enum)\s+(\w+)', content, re.MULTILINE):
        name = m.group(1)
        symbols.setdefault(name, {'kind': 'type', 'decls': [], 'refs': 0})['decls'].append((path, m.group(0).strip()))

    for m in re.finditer(r'^(?:\s*(?:public|internal|private|protected|protected internal|private protected)\s+)?(?:static\s+)?(?:async\s+)?(?:override\s+)?(?:sealed\s+)?(?:virtual\s+)?(?:partial\s+)?[\w<>,\[\]\.\s]+\s+(\w+)\s*\(', content, re.MULTILINE):
        name = m.group(1)
        symbols.setdefault(name, {'kind': 'method', 'decls': [], 'refs': 0})['decls'].append((path, m.group(0).strip()))

for name, info in symbols.items():
    pattern = re.compile(r'\b' + re.escape(name) + r'\b')
    for content in text.values():
        info['refs'] += len(pattern.findall(content))
    info['refs'] -= len(info['decls'])

unused = [(name, info) for name, info in symbols.items() if info['refs'] == 0 and len(info['decls']) > 0]
unused.sort(key=lambda x: (x[1]['kind'], x[0]))

print('Total symbols scanned:', len(symbols))
print('Potential unused symbols:', len(unused))
for name, info in unused[:200]:
    print(f"{info['kind']} {name} refs=0 decls={len(info['decls'])}")
    for path, decl in info['decls'][:2]:
        print('  ', path, decl)
