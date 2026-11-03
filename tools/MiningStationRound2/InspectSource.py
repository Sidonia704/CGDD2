"""Diagnostic source geometry projections; never creates Unity assets."""
import json, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[2]
r=json.loads((ROOT/'Temp/MiningRound2/Sources.json').read_text(encoding='utf-8'))
m=r['meshes'][0]
v=np.array([[p['x'],p['y'],p['z']] for p in m['vertices']])
parts={p['id']:p for p in m['parts']}
def frame(vertices,triangles,title,w=620,h=490,principal=False):
    vertices=vertices.copy();vertices-= (vertices.max(0)+vertices.min(0))/2
    if principal:
        _,axes=np.linalg.eigh(np.cov(vertices.T));vertices=vertices@axes[:,::-1]
    direction=np.array([.65,.7,1.]);direction/=np.linalg.norm(direction)
    right=np.cross([0,1,0],direction);right/=np.linalg.norm(right);up=np.cross(direction,right)
    projected=np.stack((vertices@right,-vertices@up),axis=1)
    span=np.ptp(projected,axis=0);scale=min((w-48)/max(span[0],.1),(h-70)/max(span[1],.1))
    projected=projected*scale+[w/2,h/2+15]
    image=Image.new('RGB',(w,h),(19,26,35));d=ImageDraw.Draw(image)
    tris=np.array(triangles,dtype=int).reshape(-1,3)
    z=vertices@direction
    for t in tris[np.argsort(z[tris].mean(1))]:
        a,b,c=vertices[t];normal=np.cross(b-a,c-a);ln=np.linalg.norm(normal)
        shade=.35+.65*abs(float(normal@np.array([.5,.8,.3]))/max(ln,1e-9))
        shade=min(shade,1);col=tuple(int(x*shade) for x in (188,204,218))
        d.polygon([tuple(p) for p in projected[t]],fill=col)
    d.text((16,12),title,fill=(240,240,240))
    return image
selections=[('PLATE / island 179',179),('PLATE / island 184',184),('PLATE / island 185',185),('SYSTEM / island 111',111),('FRAME / island 178',178),('COLUMN / island 224',224),('DECK / island 227',227),('SLAB / island 485',485),('BEAM / island 583',583)]
atlas=Image.new('RGB',(1860,1470))
for i,(name,pid) in enumerate(selections):
    if pid is None:vv=v;tt=m['triangles']
    else:
        p=parts[pid];idx=np.array(p['indices']).reshape(-1,3);unique=np.unique(idx);vv=v[unique];remap={int(k):j for j,k in enumerate(unique)};tt=[remap[int(k)] for k in idx.flat]
        name+=f" / triangles {p['triangleCount']}"
    atlas.paste(frame(vv,tt,name),(i%3*620,i//3*490))
atlas.save(ROOT/'Temp/MiningRound2/SourceDetailsAtlas.png')
# Compact native-space inventory of broad/long islands, avoiding smallest decorative parts.
candidates=[]
for p in m['parts']:
    size=sorted(p['size'].values(),reverse=True)
    if size[0]>35:candidates.append({k:p[k] for k in ['id','triangleCount','center','size']})
(ROOT/'Temp/MiningRound2/Candidates.json').write_text(json.dumps(candidates,indent=2))
print('SourceDetailsAtlas.png; broad/long islands:',len(candidates))
