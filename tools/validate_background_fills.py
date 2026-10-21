"""Independently decode original/patched AFP movies with their BSI tables; never write game data."""
import argparse
import gzip
import hashlib
import json
import struct
from pathlib import Path

from ifstools.ifs import IFS


def decode(raw, bsi):
    # Full-movie byte swapping, independent of the generator's header position map.
    data = bytearray(raw)
    cursor = 0
    for word, in struct.iter_unpack('<H', bsi):
        if word == 0:
            break
        cursor += 2 * (word & 127)
        kind, repeat = word >> 13, ((word >> 7) & 63) + 1
        if kind == 0:
            cursor += 256 * (repeat - 1)
            continue
        assert kind in (1, 2, 3)
        length = 2 ** kind
        for _ in range(repeat):
            assert cursor + length <= len(data)
            data[cursor:cursor+length] = data[cursor:cursor+length][::-1]
            cursor += length
    return bytes(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', required=True, type=Path)
    parser.add_argument('--catalog', type=Path, default=Path(__file__).resolve().parents[1] / 'EffectCatalog.json.gz')
    args = parser.parse_args()
    root = args.game_root / 'contents/data'
    catalog = json.loads(gzip.decompress(args.catalog.read_bytes()))
    rules = {f['Output']: f for f in catalog['BackgroundFills']}
    assert len(rules) == len(catalog['BackgroundFills'])
    seen = set()
    examples = []
    for package in sorted((root / 'graphics/game_bg').glob('*.ifs')):
        obj = IFS(str(package))
        try:
            afp = obj.tree.folders.get('afp')
            if not afp:
                continue
            for name, movie in afp.files.items():
                if name == 'afplist.xml':
                    continue
                raw = movie.load(convert_kbin=False)
                bsi = afp.folders['bsi'].files[name].load(convert_kbin=False)
                original = decode(raw, bsi)
                assert original[0] in (7, 8, 9, 10) and original[1:4] == bytes.fromhex('b2d0c1')
                assert struct.unpack_from('<I', original, 4)[0] == len(raw)
                flags = struct.unpack_from('<I', original, 12)[0]
                rgba = struct.unpack_from('<I', original, 28)[0]
                output = package.relative_to(root).with_suffix('').as_posix() + '_ifs/afp/' + name
                needed = bool(flags & 1 and rgba >> 24 and rgba & 0xffffff)
                assert (output in rules) == needed, f'Background coverage mismatch: {output}'
                if not needed:
                    continue
                rule = rules[output]
                assert hashlib.sha256(raw).hexdigest().upper() == rule['BeforeHash']
                with (root / rule['Source']).open('rb') as stream:
                    stream.seek(rule['Offset'])
                    assert stream.read(rule['Size']) == raw
                patched = bytearray(raw)
                for index in rule['RgbOffsets']:
                    patched[index] = 0
                assert hashlib.sha256(patched).hexdigest().upper() == rule['AfterHash']
                after = decode(patched, bsi)
                assert after[:28] == original[:28] and after[31:] == original[31:]
                assert struct.unpack_from('<I', after, 28)[0] == rgba & 0xff000000
                seen.add(output)
                if package.name == 'gmbg_0102_dome.ifs' and name in ('normal', 'clear'):
                    examples.append({'movie': output, 'before': f'{rgba:08X}', 'after': f'{rgba & 0xff000000:08X}'})
        finally:
            obj.file.close()
    assert seen == set(rules)
    print(json.dumps({'validated_fills': len(seen), 'only_rgb_changed': True,
                      'alpha_flags_timeline_and_bsi_unchanged': True, 'examples': examples}))


if __name__ == '__main__':
    main()
