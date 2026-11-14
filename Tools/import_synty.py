"""산 Synty 팩(.unitypackage)을 프로젝트에 골라 넣는다(항목 32-0, 2026-11-14).

  python Tools/import_synty.py [패키지 폴더]

- 기본 패키지 폴더: ../temp-assets/Synty/unity (zip에서 꺼낸 .unitypackage 네 개).
  zip만 있으면 먼저 꺼낸다: unzip -j "*.zip" "*.unitypackage" -d unity/
- Unity의 -importPackage는 전부 넣기만 해서, 여기서 직접 풀어 필요한 것만 쓴다.
  .unitypackage는 tar.gz이고 GUID 폴더마다 pathname(경로)·asset(파일)·asset.meta가 들어 있다.
  .meta를 그대로 쓰므로 GUID가 어느 PC에서나 같다(VisualCatalog 칸이 그대로 이어진다).
- 넣지 않는 것: 데모 씬, 샘플 스크립트(Input System 필요), 패키지 도우미, 문서.
- Assets/ExternalAssets/Synty는 .gitignore에 있다(산 에셋을 저장소에 올리지 않는다). 새 PC에서는 이 스크립트를 다시 돌린다.
"""
import os
import sys
import tarfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP = (
    '/Scenes/', '/SyntyPackageHelper/', '/AnimationBaseLocomotion/Samples/', '/Documentation/',
    '.unity', '.lighting', '.pdf',
)


def place(path):
    """팩 안의 경로(Assets/Synty/...)를 우리 프로젝트의 외부 에셋 폴더(Assets/ExternalAssets/Synty/...)로 옮긴다."""
    return 'Assets/ExternalAssets/' + path[len('Assets/'):]


def wanted(path):
    if not path.startswith('Assets/Synty/'):
        return False
    probe = path + ('/' if '.' not in os.path.basename(path) else '')
    return not any(token in probe or path.endswith(token) for token in SKIP)


def import_package(package):
    entries = {}
    written = 0
    skipped = 0
    with tarfile.open(package, 'r:gz') as tar:
        for member in tar:
            if not member.isfile():
                continue
            guid, _, name = member.name.partition('/')
            guid = guid.lstrip('./')
            entry = entries.setdefault(guid, {})
            if name == 'pathname':
                entry['path'] = tar.extractfile(member).read().decode('utf-8', 'replace').splitlines()[0].strip()
            elif name in ('asset', 'asset.meta'):
                entry[name] = tar.extractfile(member).read()
            # 한 GUID의 세 파일이 다 모이면 바로 쓴다(메모리 절약). 순서는 asset → asset.meta → pathname이 보통이다.
            if 'path' in entry and 'asset.meta' in entry and not entry.get('done'):
                path = entry['path']
                if wanted(path):
                    target = os.path.join(ROOT, place(path).replace('/', os.sep))
                    os.makedirs(os.path.dirname(target), exist_ok=True)
                    if 'asset' in entry:
                        with open(target, 'wb') as handle:
                            handle.write(entry['asset'])
                    else:
                        os.makedirs(target, exist_ok=True)
                    with open(target + '.meta', 'wb') as handle:
                        handle.write(entry['asset.meta'])
                    written += 1
                else:
                    skipped += 1
                entry['done'] = True
                entry.pop('asset', None)
                entry.pop('asset.meta', None)
    # pathname이 asset보다 먼저 온 항목(드묾)을 마저 쓴다.
    late = [e for e in entries.values() if not e.get('done') and 'path' in e]
    for entry in late:
        if wanted(entry['path']) and 'asset.meta' in entry:
            target = os.path.join(ROOT, place(entry['path']).replace('/', os.sep))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            if 'asset' in entry:
                with open(target, 'wb') as handle:
                    handle.write(entry['asset'])
            with open(target + '.meta', 'wb') as handle:
                handle.write(entry['asset.meta'])
            written += 1
    print(f'{os.path.basename(package)}: {written} written, {skipped} skipped')


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, '..', 'temp-assets', 'Synty', 'unity')
    packages = sorted(f for f in os.listdir(folder) if f.endswith('.unitypackage'))
    if not packages:
        print('패키지 없음:', folder)
        return 1
    for name in packages:
        import_package(os.path.join(folder, name))
    return 0


if __name__ == '__main__':
    sys.exit(main())
