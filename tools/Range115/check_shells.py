import pathlib,re,json
import numpy as np
from scipy import ndimage
root=pathlib.Path(__file__).resolve().parents[2]
paths=list((root/'Assets/_Starframe/Maps/UniformVoxels/Samples/Assets').glob('*.asset'))
paths+=list((root/'Assets/_Starframe/Maps/UniformVoxels/Backdrop/Assets').glob('*.asset'))
rows=[]
for path in paths:
    if not any(x in path.stem for x in ('Rock_','field_','scatter_','Backdrop_')): continue
    text=path.read_text(encoding='utf-8-sig')
    p=np.array(re.findall(r'^  - position: \{x: (-?\d+), y: (-?\d+), z: (-?\d+)\}',text,re.M),dtype=np.int32)
    if not len(p): continue
    lo=p.min(0)-2; shape=tuple(p.max(0)-lo+3)
    if np.prod(shape)>50_000_000: continue
    occupied=np.zeros(shape,dtype=bool);occupied[tuple((p-lo).T)]=True
    labels,n=ndimage.label(~occupied)
    center=tuple(np.clip(-lo,0,np.array(shape)-1))
    sealed=bool(occupied[center] or labels[center]!=labels[0,0,0])
    _,components=ndimage.label(occupied)
    rows.append(dict(name=path.stem,cells=len(p),sealed=sealed,components=components,emptyRegions=n))
print(json.dumps(rows,indent=2))
(root/'tools/Range115/shell-audit.json').write_text(json.dumps(rows,indent=2))
