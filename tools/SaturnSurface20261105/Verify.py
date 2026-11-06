"""Focused offline acceptance of recovered surfaces and split ownership."""
import json
from pathlib import Path
import zipfile
import numpy as np
from scipy.spatial import cKDTree

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT/'Temp/SaturnSurface20261105'
manifest = json.loads((OUTPUT/'manifest-0000.json').read_text())
z = zipfile.ZipFile(ROOT/'Documentation/ReferenceMap-2026-11-01/interchange-final.zip')
catalog = json.loads(z.read('Temp/ReferenceMap20261101/geometry-full-scene-final.json'))
assets = {a['assetKey']: a for a in catalog['assets']}
result = {'assets': len(manifest['assets']), 'triangles': manifest['triangles'], 'failures': list(manifest['failures']), 'splitAssets': []}
for record in manifest['assets']:
    mesh = json.loads(Path(record['path']).read_text())
    v = np.asarray(mesh['vertices']).reshape(-1, 3)
    t = np.asarray(mesh['triangles']).reshape(-1, 3)
    if not np.isfinite(v).all() or t.min() < 0 or t.max() >= len(v):
        result['failures'].append({'assetKey': record['assetKey'], 'error': 'invalid vertex/index data'})
    if not record['occupiedCellClipping']:
        continue
    asset = assets[record['assetKey']]
    cells = np.asarray(json.loads(z.read(asset['cellRowsPath']))['cells'])[:, :3]
    tree = cKDTree(cells * asset['cellSize'])
    tri = v[t]
    # Deterministic barycentric points include corners, edges, and interiors.
    points = np.concatenate([tri[:,0]*a + tri[:,1]*b + tri[:,2]*(1-a-b)
                             for a in (0, .25, .5, .75, 1) for b in (0, .25, .5, .75, 1) if a+b <= 1])
    distance = tree.query(points, p=np.inf)[0]
    excess = np.maximum(distance-asset['cellSize']*.5, 0)
    worst = float(excess.max(initial=0))
    entry = {'assetKey': record['assetKey'], 'samplePoints': len(points), 'maximumOccupiedBoxExcessMetres': worst, 'triangles': len(t)}
    result['splitAssets'].append(entry)
    if worst > .001:
        result['failures'].append({'assetKey': record['assetKey'], 'error': 'surface crosses physical split', 'excess': worst})
result['allChecksPassed'] = len(manifest['assets']) == len(assets) and not result['failures']
(OUTPUT/'verification.json').write_text(json.dumps(result, indent=2))
print(json.dumps(result, indent=2))
raise SystemExit(0 if result['allChecksPassed'] else 1)
