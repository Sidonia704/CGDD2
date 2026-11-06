"""Compare recovered triangle area with frozen source area (split assets grouped)."""
from collections import defaultdict
import json
from pathlib import Path
import zipfile
import numpy as np
from Prepare import ROOT, shell_ids

out = ROOT/'Temp/SaturnSurface20261105'
docs = ROOT/'Documentation/ReferenceMap-2026-11-01'
z = zipfile.ZipFile(docs/'interchange-final.zip')
catalog = json.loads(z.read('Temp/ReferenceMap20261101/geometry-full-scene-final.json'))
inventory = json.loads((docs/'source-meshes/source-meshes.json').read_text())
objects = {o['name']: o for o in inventory['objects']}
calibration = np.asarray(json.loads((docs/'source-calibration.json').read_text())['sourceWorldToCanonicalUnityMatrixRows'])
groups = defaultdict(list)
for asset in catalog['assets']:
    groups[(asset['source']['object'], tuple(shell_ids(asset['source'])))].append(asset)
cache = {}
records = []
def area(v, t):
    tri = v[t]
    return float(np.linalg.norm(np.cross(tri[:,1]-tri[:,0], tri[:,2]-tri[:,0]), axis=1).sum()*.5)
for (name, shells), assets in groups.items():
    if name not in cache:
        obj = objects[name]
        data = np.load(docs/'source-meshes'/obj['npz'])
        matrix = calibration @ np.asarray(obj['matrix_world'])
        vertices = data['vertices'] @ matrix[:3,:3].T + matrix[:3,3]
        labels = np.load(docs/'source-meshes'/(Path(obj['npz']).stem+'-components.npz'))['triangle_component']
        cache[name] = (vertices, data['triangles'].reshape(-1,3), labels)
    v,t,labels = cache[name]
    expected = area(v, t[np.isin(labels, shells)])
    actual = 0
    for asset in assets:
        mesh = json.loads((out/(asset['assetKey']+'.json')).read_text())
        actual += area(np.asarray(mesh['vertices']).reshape(-1,3), np.asarray(mesh['triangles']).reshape(-1,3))
    relative = abs(actual-expected)/max(expected,1e-10)
    records.append({'assets': [a['assetKey'] for a in assets], 'sourceArea':expected, 'recoveredArea':actual, 'relativeDifference':relative})
failures = [r for r in records if r['relativeDifference'] > .001 and abs(r['recoveredArea']-r['sourceArea']) > .0001]
report = {'assetCount':len(catalog['assets']), 'sourceGroups':len(records), 'maximumRelativeAreaDifferenceAboveOneSquareCentimetre':max(r['relativeDifference'] for r in records if r['sourceArea'] > .0001), 'subSquareCentimetreSourceGroups':sum(r['sourceArea'] <= .0001 for r in records), 'absoluteAreaToleranceSquareMetres':.0001, 'relativeAreaTolerance':.001, 'failures':failures, 'allChecksPassed':not failures, 'groups':records}
(out/'source-area-coverage.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k!='groups'},indent=2))
raise SystemExit(bool(failures))
