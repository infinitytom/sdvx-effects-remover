"""Validate direct override paths against the packed IFS file names, not inferred names."""
import argparse
import gzip
import json
import struct
from pathlib import Path

from ifstools.ifs import IFS

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-root', required=True, type=Path)
parser.add_argument('--catalog', type=Path, default=Path(__file__).resolve().parents[1] / 'EffectCatalog.json.gz')
args = parser.parse_args()
root = args.game_root / 'contents/data'
catalog = json.loads(gzip.decompress(args.catalog.read_bytes()))
packages = {}
count = 0
try:
    for rule in catalog['Items'] + catalog['BackgroundFills']:
        container, subpath = rule['Output'].split('_ifs/', 1)
        package = container + '.ifs'
        if package not in packages:
            packages[package] = IFS(str(root / package))
        obj = packages[package]
        folder, name = subpath.split('/', 1)
        file = obj.tree.folders[folder].files[name]
        # _packed_name is the actual unescaped AVS filename; packed_name is the
        # XML-escaped name and must not be used (e.g. leading digit -> underscore).
        expected = container + '_ifs/' + folder + '/' + file._packed_name
        assert rule['ArchiveOutput'] == expected, (rule['ArchiveOutput'], expected)
        assert len(file._packed_name) == 32 and all(c in '0123456789abcdef' for c in file._packed_name)
        assert rule['Offset'] == file.ifs_data.offset + file.start
        assert rule['Size'] == file.size
        if rule.get('Group') == 'frame':
            data = file.load(convert_kbin=False)
            endian = '>' if data[:4] == b'GE2D' else '<'
            labels = struct.unpack_from(endian + 'H', data, 26)[0]
            table = struct.unpack_from(endian + 'I', data, 44)[0]
            for index in range(labels):
                start = struct.unpack_from(endian + 'I', data, table + index * 4)[0]
                label = data[start:].split(b'\0', 1)[0].decode('ascii')
                assert not label.startswith(('hantei_', 'beam_', 'font_', 'num_', 'gauge_', 'ef_BT_long', 'ef_FX_long', 'vol_cursor'))
        count += 1
finally:
    for obj in packages.values():
        obj.file.close()
print(json.dumps({'validated_native_paths': count, 'containers': len(packages), 'xml_escaped_paths_used': False}))
