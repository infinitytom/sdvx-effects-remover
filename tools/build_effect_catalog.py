"""Read local IFS manifests and emit offset-only patch instructions. Never alter game data."""
from pathlib import Path
from ifstools.ifs import IFS
from ifstools.handlers.image_file import ImageFile
import gzip
import hashlib
import json
import re
import struct
import math
import argparse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-root', type=Path, required=True, help='LAZY_KFC root containing contents/data')
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'EffectCatalog.json.gz')
args = parser.parse_args()
ROOT = (args.game_root / 'contents' / 'data').resolve()
if not ROOT.is_dir():
    parser.error('--game-root must contain contents/data')
OUT = args.output
sources = {}
items = []
stats = {}
protected_outputs = set()
black_textures = []
background_fills = []

def add_background_fills(obj, package):
    afp = obj.tree.folders.get('afp')
    if not afp or 'bsi' not in afp.folders:
        return
    for name, f in afp.files.items():
        if name == 'afplist.xml':
            continue
        bsi = afp.folders['bsi'].files.get(name)
        if not bsi:
            raise ValueError(f'Missing AFP byte-swap table: {package}/{name}')
        original = f.load(convert_kbin=False)
        swaps = bsi.load(convert_kbin=False)
        # Track the original byte positions of the decoded header. The rest of the
        # movie and its BSI table remain byte-identical, including all timelines.
        mapping = list(range(64))
        pos = 0
        for off in range(0, len(swaps), 2):
            word = struct.unpack_from('<H', swaps, off)[0]
            if word == 0 or pos >= 64:
                break
            pos += (word & 0x7f) * 2
            kind, loops = word >> 13, (word >> 7) & 0x3f
            if kind == 0:
                pos += 256 * loops
                continue
            if kind not in (1, 2, 3):
                raise ValueError('Unsupported AFP byte-swap operation')
            size = 1 << kind
            for _ in range(loops + 1):
                if pos + size > len(original):
                    raise ValueError('AFP byte-swap range exceeds movie')
                if pos < 64:
                    assert pos + size <= 64
                    mapping[pos:pos+size] = reversed(mapping[pos:pos+size])
                pos += size
        header = bytes(original[i] for i in mapping)
        assert header[0] in (7, 8, 9, 10) and header[1:4] == bytes.fromhex('b2d0c1')
        assert struct.unpack_from('<I', header, 4)[0] == len(original)
        assert struct.unpack_from('<H', header, 8)[0] == 0x200
        flags = struct.unpack_from('<I', header, 12)[0]
        # Only movies that ALREADY draw a background: never add opaque fills to
        # transparent particle/subclips, which could cover foreground gameplay.
        if not flags & 1 or header[31] == 0 or header[28:31] == b'\0\0\0':
            continue
        rgb_offsets = mapping[28:31]
        assert len(set(rgb_offsets)) == 3 and all(28 <= i < 32 for i in rgb_offsets)
        patched = bytearray(original)
        for i in rgb_offsets:
            patched[i] = 0
        source = Path(f.ifs_data.file.name).resolve().relative_to(ROOT).as_posix()
        if source not in sources:
            source_path = ROOT / source
            with source_path.open('rb') as stream:
                sha = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
            sources[source] = {'Path': source, 'Sha256': sha, 'Size': source_path.stat().st_size}
        container = package.relative_to(ROOT).with_suffix('').as_posix() + '_ifs'
        background_fills.append({'Source': source, 'Offset': f.ifs_data.offset + f.start,
            'Size': len(original), 'Output': f'{container}/afp/{name}', 'RgbOffsets': rgb_offsets,
            'BeforeHash': hashlib.sha256(original).hexdigest().upper(),
            'AfterHash': hashlib.sha256(patched).hexdigest().upper()})

def labels(data):
    endian = '>' if data[:4] == b'GE2D' else '<'
    count = struct.unpack_from(endian + 'H', data, 26)[0]
    offset = struct.unpack_from(endian + 'I', data, 44)[0]
    values = []
    for i in range(count):
        pos = struct.unpack_from(endian + 'I', data, offset + 4*i)[0]
        values.append(data[pos:].split(b'\0', 1)[0].decode('ascii', errors='replace'))
    return values

def add_package(path, group, accept):
    obj = IFS(str(path))
    try:
        geos = obj.tree.folders.get('geo')
        if not geos:
            return
        for f in geos.files.values():
            original = f.load(convert_kbin=False)
            if original[:4] not in (b'GE2D', b'D2EG'):
                raise ValueError(f'Unsupported geometry {path}/{f.name}')
            endian = '>' if original[:4] == b'GE2D' else '<'
            if len(original) < 52 or struct.unpack_from(endian+'I', original, 12)[0] != len(original):
                raise ValueError('Invalid geometry size')
            commands = struct.unpack_from(endian+'H', original, 28)[0]
            offset = struct.unpack_from(endian+'I', original, 48)[0]
            if offset + commands*16 > len(original):
                raise ValueError('Invalid render parameters')
            texture_names = labels(original)
            container = path.relative_to(ROOT).with_suffix('').as_posix() + '_ifs'
            output = f'{container}/geo/{f.name}'
            animation = f.name.rsplit('_shape', 1)[0]
            if path.name == 'game_lane.ifs' or (
                path.name == 'game_effect.ifs' and (animation == 'ef_btn_all' or animation.startswith(('x_ef_long', 'x_ef_Slong', 'ef_cursor_', 'vol_cursor_')))
                and not (texture_names and all(t in {'ef_crifont', 'ef_crifonts'} for t in texture_names))
            ) or (path.name == 'game_effect_hg.ifs' and animation in {'ef_BT_long', 'ef_FX_long', 'x_BT_long', 'x_FX_long'}):
                protected_outputs.add(output)
            if not commands or not accept(f.name, texture_names):
                continue
            source = Path(f.ifs_data.file.name).resolve().relative_to(ROOT).as_posix()
            if source not in sources:
                source_path = ROOT / source
                sources[source] = {'Path': source, 'Sha256': hashlib.file_digest(source_path.open('rb'), 'sha256').hexdigest().upper(), 'Size': source_path.stat().st_size}
            container = path.relative_to(ROOT).with_suffix('').as_posix() + '_ifs'
            assert '/' not in f.name and '\\' not in f.name
            output = f'{container}/geo/{f.name}'
            # Native afpu_render_draw_geo requires the original render count (often exactly one).
            # Keep all structure and draw records; collapse only the 2D positions into a point.
            vertex_count = struct.unpack_from(endian+'H', original, 20)[0]
            vertex_offset = struct.unpack_from(endian+'I', original, 32)[0]
            if not vertex_count or vertex_offset < 52 or vertex_offset + vertex_count*8 > len(original):
                raise ValueError('Invalid vertex stream')
            first_point = original[vertex_offset:vertex_offset+8]
            if not all(math.isfinite(v) for v in struct.unpack(endian+'ff', first_point)):
                raise ValueError('Invalid first vertex')
            patched = bytearray(original)
            patched[vertex_offset:vertex_offset+vertex_count*8] = first_point * vertex_count
            items.append({'Group': group, 'Source': source, 'Offset': f.ifs_data.offset + f.start,
                          'Size': len(original), 'Output': output,
                          'BeforeHash': hashlib.sha256(original).hexdigest().upper(),
                          'AfterHash': hashlib.sha256(patched).hexdigest().upper()})
            stats[group] = stats.get(group, 0) + 1
    finally:
        obj.file.close()

for package in sorted((ROOT/'graphics/game_bg').glob('*.ifs')):
    add_package(package, 'background', lambda name, textures: True)
    obj = IFS(str(package))
    try:
        add_background_fills(obj, package)
        tex = obj.tree.folders.get('tex')
        if tex:
            for f in tex.files.values():
                if not isinstance(f, ImageFile):
                    continue
                source = Path(f.ifs_data.file.name).resolve().relative_to(ROOT).as_posix()
                if source not in sources:
                    source_path = ROOT / source
                    with source_path.open('rb') as stream:
                        sha = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
                    sources[source] = {'Path': source, 'Sha256': sha, 'Size': source_path.stat().st_size}
                width, height = f.img_size
                assert 0 < width <= 8192 and 0 < height <= 8192 and width * height <= 16777216
                container = package.relative_to(ROOT).with_suffix('').as_posix() + '_ifs'
                black_textures.append({'Source': source, 'Output': f'{container}/tex/{f.name}', 'Width': width, 'Height': height})
    finally:
        obj.file.close()

# Preserve the complete original button-hit and hold animations, including their light pillars.
# Only knob dust remains in the historic "hit" group so installed modes can upgrade without renaming.
add_package(ROOT/'graphics/ver06/game_effect_hg.ifs', 'hit', lambda name, tex:
            name.startswith('efhg_dust_'))
add_package(ROOT/'graphics/ver06/game_effect_hg.ifs', 'lane', lambda name, tex:
            name.startswith('lane_effect_shape') and bool(tex) and all(t in {'lane_hansya', 'lane_lamp', 'lane_metalgrow'} for t in tex))
add_package(ROOT/'graphics/ver07/game_hud.ifs', 'lane', lambda name, tex:
            name.rsplit('_shape',1)[0] in {'x_glow_blue', 'x_glow_red', 'x_glow_white', 'x_line_light_mm'})

# Keep button light pillars intact; enumerate them only for the protection audit.
add_package(ROOT/'graphics/ver06/game_lane.ifs', 'extra', lambda name, tex: False)
add_package(ROOT/'graphics/ver06/game_effect.ifs', 'extra', lambda name, tex:
            name.rsplit('_shape', 1)[0].startswith(('ef_vo_corner_', 'ef_vol_out_')))
add_package(ROOT/'graphics/ver06/game_effect_hg.ifs', 'extra', lambda name, tex:
            name.rsplit('_shape', 1)[0].startswith(('efhg_curve_', 'ef_line_hg_', 'x_curve_')))

for version in ('ver06', 'ver07'):
    add_package(ROOT/f'graphics/{version}/game_hud.ifs', 'extra', lambda name, tex:
                name.rsplit('_shape', 1)[0] in {'x_eff_chain_a', 'x_eff_chain_b', 'x_logo_in', 'x_logo_line_motion', 'x_logo_lpef', 'blpass_ef'} or
                (bool(tex) and all(t in {'blpass_ef', 'chain_beam_blue', 'chain_beam_green', 'ef_linekouka', 'gauge_efline'} for t in tex)))
    # Result/clear banners are animation-only containers, not the judgment logic or score data.
    add_package(ROOT/f'graphics/{version}/hantei.ifs', 'judgment', lambda name, tex: True)
    add_package(ROOT/f'graphics/{version}/game_hud.ifs', 'judgment', lambda name, tex:
                name.rsplit('_shape', 1)[0] in {'push_early', 'push_late'} or
                (bool(tex) and all(t.startswith(('font_early', 'font_late', 'font_chain')) for t in tex)))
add_package(ROOT/'graphics/ver06/game_effect.ifs', 'judgment', lambda name, tex:
            name.rsplit('_shape', 1)[0] in {'x_crifont', 'x_crifontS', 'x_ef_error'} or
            (bool(tex) and all(t in {'ef_crifont', 'ef_crifonts'} for t in tex)))

assert len({i['Output'] for i in items}) == len(items)
assert not protected_outputs.intersection(i['Output'] for i in items), 'A required hit/cursor resource was selected'
assert len({t['Output'] for t in black_textures}) == len(black_textures)
catalog = {'Version': 2, 'GameIdentifier': 'KFC-6aa256e6_7ad708', 'Sources': list(sources.values()), 'Items': items, 'ProtectedOutputs': sorted(protected_outputs), 'BlackTextures': black_textures, 'BackgroundFills': background_fills}
OUT.write_bytes(gzip.compress(json.dumps(catalog, separators=(',', ':')).encode('utf8'), mtime=0))
print(json.dumps({'groups': stats, 'black_textures': len(black_textures), 'background_fills': len(background_fills), 'sources': len(sources), 'catalog_bytes': OUT.stat().st_size}))
