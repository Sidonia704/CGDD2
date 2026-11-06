"""Recover the old map's source surfaces, preserving its voxel assets and poses.

Writes an offline interchange only. Split source shells are clipped to the union
of the owning asset's occupied cell boxes, never merely its enclosing bounds.
"""
import argparse
import collections
import itertools
import json
from pathlib import Path
import sys
import zipfile

import numpy as np
from scipy.spatial import cKDTree

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ReferenceMap20261101'))
from Geometry import raster_mesh, Limits


def clip(poly, axis, plane, greater):
    result = []
    a = poly[-1]
    ai = a[axis] >= plane-1e-8 if greater else a[axis] <= plane+1e-8
    for b in poly:
        bi = b[axis] >= plane-1e-8 if greater else b[axis] <= plane+1e-8
        if ai != bi:
            result.append(a + (b-a) * ((plane-a[axis])/(b[axis]-a[axis])))
        if bi:
            result.append(b)
        a, ai = b, bi
    return result


def clip_cells(vertices, triangles, cells, size):
    """Triangle/occupied-box intersection. Retains planar surfaces exactly."""
    centers = cells * size
    tree = cKDTree(centers)
    result = []
    half = size*.5
    for indices in triangles:
        tri = vertices[indices]
        pieces = []
        lo, hi = tri.min(0), tri.max(0)
        center = (lo+hi)*.5
        radius = np.linalg.norm((hi-lo)*.5)+half*np.sqrt(3)+1e-7
        candidates = tree.query_ball_point(center, radius)
        for ci in candidates:
            c = centers[ci]
            if np.any(c+half < lo-1e-7) or np.any(c-half > hi+1e-7):
                continue
            poly = list(tri)
            for axis in range(3):
                for plane, greater in ((c[axis]-half, True), (c[axis]+half, False)):
                    if poly:
                        poly = clip(poly, axis, plane, greater)
            if len(poly) >= 3:
                pieces.append(poly)
        dissolved = dissolve(pieces, tri)
        for poly in dissolved if dissolved is not None else pieces:
            for i in range(1, len(poly)-1):
                t = np.array((poly[0], poly[i], poly[i+1]))
                if np.linalg.norm(np.cross(t[1]-t[0], t[2]-t[0])) > 1e-7:
                    result.append(t)
    if not result:
        return np.empty((0, 3)), np.empty((0, 3), dtype=np.int32)
    vs = np.concatenate(result)
    # Weld shared clipping edges; discard duplicate triangles on shared boxes.
    vs, inverse = np.unique(np.round(vs, 5), axis=0, return_inverse=True)
    ts = inverse.reshape(-1, 3)
    _, unique = np.unique(np.sort(ts, axis=1), axis=0, return_index=True)
    return vs, ts[np.sort(unique)]


def dissolve(pieces, triangle):
    """Remove internal cell boundaries on one original planar triangle.

    Ear-clips simple boundary loops. Falls back to the exact clipped polygons
    if holes, ambiguous touching loops or numerical degeneracy are detected.
    """
    edges = {}
    for poly in pieces:
        pts = [tuple(np.round(p, 5)) for p in poly]
        for a, b in zip(pts, pts[1:]+pts[:1]):
            if a == b:
                continue
            if (b, a) in edges:
                del edges[b, a]
            else:
                edges[a, b] = True
    following = {}
    for a, b in edges:
        if a in following:
            return None
        following[a] = b
    loops = []
    while following:
        start = next(iter(following))
        loop, p = [], start
        while True:
            loop.append(np.asarray(p))
            if p not in following:
                return None
            p = following.pop(p)
            if p == start:
                break
            if len(loop) > len(edges):
                return None
        loops.append(loop)
    normal = np.cross(triangle[1]-triangle[0], triangle[2]-triangle[0])
    normal /= max(np.linalg.norm(normal), 1e-15)
    result = []
    for loop in loops:
        # Collinear boundary points introduced by adjacent cells are redundant.
        changed = True
        while changed and len(loop) > 3:
            changed = False
            for i in range(len(loop)):
                a, b, c = loop[i-1], loop[i], loop[(i+1) % len(loop)]
                if np.linalg.norm(np.cross(b-a, c-b)) < 2e-4 * max(np.linalg.norm(c-a), 1):
                    loop.pop(i); changed = True; break
        signed = sum(np.dot(np.cross(loop[i], loop[(i+1) % len(loop)]), normal) for i in range(len(loop)))
        if signed <= 1e-8:  # A hole requires a constrained triangulator; retain exact clipping.
            return None
        while len(loop) > 3:
            found = False
            for i in range(len(loop)):
                a, b, c = loop[i-1], loop[i], loop[(i+1) % len(loop)]
                if np.dot(np.cross(b-a, c-b), normal) <= 1e-9:
                    continue
                occupied = False
                for j, q in enumerate(loop):
                    if j in ((i-1) % len(loop), i, (i+1) % len(loop)):
                        continue
                    if all(np.dot(np.cross(v-u, q-u), normal) >= -1e-7 for u, v in ((a,b), (b,c), (c,a))):
                        occupied = True; break
                if occupied:
                    continue
                result.append([a,b,c]); loop.pop(i); found = True; break
            if not found:
                return None
        if len(loop) == 3:
            result.append(loop)
    return result


def shell_ids(source):
    return source.get('sourceShells') or source.get('sourceGroupShells') or [source['sourceShell']]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, default=ROOT/'Temp/SaturnSurface20261105')
    p.add_argument('--start', type=int, default=0)
    p.add_argument('--count', type=int, default=100000)
    args = p.parse_args()
    out = args.output
    out.mkdir(parents=True, exist_ok=True)
    docs = ROOT/'Documentation/ReferenceMap-2026-11-01'
    z = zipfile.ZipFile(docs/'interchange-final.zip')
    catalog = json.loads(z.read('Temp/ReferenceMap20261101/geometry-full-scene-final.json'))
    inventory = json.loads((docs/'source-meshes/source-meshes.json').read_text())
    source_by_name = {o['name']: o for o in inventory['objects']}
    calibration = np.asarray(json.loads((docs/'source-calibration.json').read_text())['sourceWorldToCanonicalUnityMatrixRows'])
    references = collections.Counter((a['source']['object'], shell) for a in catalog['assets'] for shell in shell_ids(a['source']))
    instances = {i['assetKey']: i for i in reversed(catalog['instances']) if i['source'].get('replacement') is None}
    source_cache = {}
    records = []
    failures = []
    for index, asset in enumerate(catalog['assets']):
        if index < args.start or index >= args.start+args.count:
            continue
        try:
            key, source, size = asset['assetKey'], asset['source'], asset['cellSize']
            name = source['object']
            if name not in source_cache:
                obj = source_by_name[name]
                data = np.load(docs/'source-meshes'/obj['npz'])
                world = data['vertices'] @ np.asarray(obj['matrix_world'])[:3, :3].T + np.asarray(obj['matrix_world'])[:3, 3]
                vs = world @ calibration[:3, :3].T + calibration[:3, 3]
                ts = data['triangles'].reshape(-1, 3)
                labels = np.load(docs/'source-meshes'/(Path(obj['npz']).stem+'-components.npz'))['triangle_component']
                source_cache[name] = (vs, ts, labels)
            vs, ts, labels = source_cache[name]
            shells = shell_ids(source)
            selected = ts[np.isin(labels, shells)]
            used, inverse = np.unique(selected, return_inverse=True)
            vertices, triangles = vs[used], inverse.reshape(-1, 3)
            if key in instances:
                inst = instances[key]
                rotation = np.asarray(inst['rotationMatrix']).reshape(3, 3)
                vertices = (vertices-np.asarray(inst['position'])) @ rotation / inst.get('scale', 1)
                anchor_method = 'frozen canonical source instance inverse'
            else:
                # Unplaced scatter prototype cells were rasterized in canonical world.
                occupied = raster_mesh(vertices/size, triangles, Limits(max_triangle_tests=60000000, max_object_cells=1000000), fill=True)
                anchor = np.rint((occupied.min(0)+occupied.max(0))*.5)
                vertices = vertices-anchor*size
                anchor_method = 'original canonical raster midpoint'
            bounds = np.asarray(asset['boundsCells'], dtype=float)*size
            bounds[0] -= size*.5
            bounds[1] += size*.5
            split = any(references[name, s] > 1 for s in shells)
            before = len(triangles)
            if split:
                rows = json.loads(z.read(asset['cellRowsPath']))['cells']
                cells = np.asarray(rows, dtype=np.int32)[:, :3]
                vertices, triangles = clip_cells(vertices, triangles, cells, size)
            if len(triangles) == 0:
                raise ValueError('empty recovered surface')
            excess = float(max(np.max(bounds[0]-vertices.min(0)), np.max(vertices.max(0)-bounds[1]), 0))
            if excess > .002:
                raise ValueError(f'source surface exceeds occupied bounds by {excess:.4f}m')
            payload = {'assetKey': key, 'vertices': vertices.round(5).reshape(-1).tolist(), 'triangles': triangles.reshape(-1).tolist()}
            path = out/(key+'.json')
            path.write_text(json.dumps(payload, separators=(',', ':')))
            records.append({'assetKey': key, 'path': str(path.resolve()), 'sourceTriangles': before, 'triangles': len(triangles), 'vertices': len(vertices), 'occupiedCellClipping': split, 'boundsExcess': excess, 'anchorMethod': anchor_method})
            if index % 50 == 0:
                print(f'{index}: {key}, {len(triangles)} triangles', flush=True)
        except Exception as error:
            failures.append({'index': index, 'assetKey': asset['assetKey'], 'error': str(error)})
            print(f'FAILED {index} {asset["assetKey"]}: {error}', flush=True)
    report = {'start': args.start, 'totalCatalog': len(catalog['assets']), 'assets': records, 'failures': failures, 'triangles': sum(a['triangles'] for a in records)}
    (out/f'manifest-{args.start:04d}.json').write_text(json.dumps(report, indent=2))
    print(json.dumps({'prepared': len(records), 'failures': failures, 'triangles': report['triangles']}), flush=True)
    return bool(failures)


if __name__ == '__main__':
    raise SystemExit(main())
