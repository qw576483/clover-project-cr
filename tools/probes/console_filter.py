"""读 `unity command console --format ndjson` 的落盘结果，按关键字过滤打印。

用途：CLI 的输出是 ndjson（可能多行 + banner），命令行里内联 python -c 会被 PowerShell
的引号层吃掉（实测多次），所以固定落成这个文件，用 `python cons.py <dump> [关键字...]` 调用。
关键字为空 ⇒ 打印尾部所有 WARN/ERROR 与全部条目。
"""
import io
import json
import sys

path = sys.argv[1]
keys = sys.argv[2:]

txt = io.open(path, encoding='utf-8-sig').read()
entries = []
for line in txt.splitlines():
    line = line.strip()
    if not line.startswith('{"type":"result"'):
        continue
    d = json.loads(line)
    if d.get('errors'):
        print('CLI-ERR:', json.dumps(d['errors'], ensure_ascii=False)[:300])
    r = (d.get('data') or {}).get('result')
    if isinstance(r, str):
        try:
            r = json.loads(r)
        except Exception:
            pass
    if isinstance(r, dict) and 'entries' in r:
        entries = r['entries']
        gt = r.get('groundTruth')
    else:
        # run_script 的返回
        inner = r.get('result') if isinstance(r, dict) else r
        print('RESULT:', inner)

if not entries:
    sys.exit(0)

print('--- console entries: %d ---' % len(entries))
for e in entries:
    msg = (e.get('message') or '').split('\n')[0]
    lvl = e.get('level', '?').upper()
    if keys:
        if not any(k.lower() in msg.lower() for k in keys):
            continue
    else:
        if lvl not in ('WARN', 'ERROR', 'FATAL'):
            continue
    print(lvl.ljust(5), msg[:190])
