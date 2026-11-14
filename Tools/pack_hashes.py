"""산 에셋 팩 파일의 해시 목록(항목 32 사용자 결정 3). 구입한 원본이 바뀌지 않았는지 확인한다.

  python Tools/pack_hashes.py write   # 목록을 새로 쓴다(들여온 직후, URP 변환을 했다면 변환 직후 한 번)
  python Tools/pack_hashes.py check   # 목록과 비교해 달라진 파일을 출력한다(종료 코드 0 = 같음)

- 대상: Assets/ExternalAssets/Synty 아래 모든 파일(.meta 포함 = 임포트 설정이 바뀌어도 잡힌다).
- 목록: ../Game-logs/assets/pack-hashes.csv  (경로는 팩 폴더 기준 상대 경로, 열: path,size,sha256)
- 시험 `Assets_PackFilesUnchanged`가 같은 목록을 매번 비교한다.
"""
import csv
import hashlib
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK = os.path.join(ROOT, 'Assets', 'ExternalAssets', 'Synty')
LIST = os.path.join(ROOT, '..', 'Game-logs', 'assets', 'pack-hashes.csv')


def scan():
    rows = {}
    for folder, _, files in os.walk(PACK):
        for name in files:
            full = os.path.join(folder, name)
            rel = os.path.relpath(full, PACK).replace(os.sep, '/')
            digest = hashlib.sha256()
            with open(full, 'rb') as handle:
                for block in iter(lambda: handle.read(1 << 20), b''):
                    digest.update(block)
            rows[rel] = (os.path.getsize(full), digest.hexdigest())
    return rows


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'check'
    rows = scan()
    if mode == 'write':
        os.makedirs(os.path.dirname(LIST), exist_ok=True)
        with open(LIST, 'w', newline='', encoding='utf-8') as handle:
            writer = csv.writer(handle)
            writer.writerow(['path', 'size', 'sha256'])
            for rel in sorted(rows):
                writer.writerow([rel, rows[rel][0], rows[rel][1]])
        print(f'{len(rows)} files written -> {os.path.normpath(LIST)}')
        return 0
    with open(LIST, newline='', encoding='utf-8') as handle:
        saved = {r['path']: (int(r['size']), r['sha256']) for r in csv.DictReader(handle)}
    changed = [p for p in rows if p in saved and rows[p] != saved[p]]
    added = [p for p in rows if p not in saved]
    removed = [p for p in saved if p not in rows]
    print(f'files {len(rows)} (list {len(saved)}): changed {len(changed)}, added {len(added)}, removed {len(removed)}')
    for label, items in (('changed', changed), ('added', added), ('removed', removed)):
        for p in items[:20]:
            print(' ', label, p)
    return 0 if not (changed or added or removed) else 1


if __name__ == '__main__':
    sys.exit(main())
