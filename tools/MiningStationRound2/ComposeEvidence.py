"""Compose real Unity captures and measure silhouette agreement (not a model-quality score)."""
from pathlib import Path
import json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/(sys.argv[1] if len(sys.argv)>1 else 'Documentation/MiningStation-Round2-2026-11-01/v04')
FONT=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',22)
SMALL=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',17)
def canvas(w,h):return Image.new('RGB',(w,h),(19,26,35))
def text(im,xy,s,font=FONT,color=(227,234,242)):ImageDraw.Draw(im).text(xy,s,font=font,fill=color)
def mask(im):
    a=np.asarray(im.convert('RGB'),dtype=float);bg=a[0,0]
    return np.linalg.norm(a-bg,axis=2)>20
r=json.loads((OUT/'build-report.json').read_text())
metrics=[]
for p in r['prototypes']:
    name=p['name'];sheet=canvas(1300,1476)
    text(sheet,(18,10),f"{name}  |  {p['cells']:,} editable cells  |  cell size {p['cellSize']:g}")
    text(sheet,(18,43),'SOURCE GEOMETRY',SMALL);text(sheet,(668,43),'EDITOR HULL BLOCKS',SMALL)
    for view,label in enumerate(['Oblique','Front / silhouette','Top / openings']):
        a=Image.open(OUT/f'raw/{name}-source-{view}.png').convert('RGB')
        b=Image.open(OUT/f'raw/{name}-blocks-{view}.png').convert('RGB')
        ma,mb=mask(a),mask(b);union=(ma|mb).sum();iou=float((ma&mb).sum()/max(1,union))
        metrics.append({'name':name,'view':label,'silhouetteIoU':round(iou,4),'sourcePixels':int(ma.sum()),'blockPixels':int(mb.sum())})
        y=84+view*464;sheet.paste(a,(0,y));sheet.paste(b,(650,y));text(sheet,(18,y+426),label,SMALL)
    sheet.save(OUT/f'{name}-comparison.png')
# Actual cutaway renders: only their headings/captions are composited here.
sections=canvas(1600,1260)
text(sections,(18,10),'SOLID INSPECTION / temporary cutaway views / saved assets remain complete')
slab=next(p['name'] for p in r['prototypes'] if p['sourceFile']=='mainstru.fbx' and p['sourceIslands']==[179])
for i,name in enumerate(['Station_SphericalTank','Station_CapsuleTank',slab,'Rock_Notched']):
    p=next(p for p in r['prototypes'] if p['name']==name)
    image=Image.open(OUT/f'raw/{name}-section.png').convert('RGB');image.thumbnail((760,550))
    x=i%2*800;y=50+i//2*600;sections.paste(image,(x+20,y))
    text(sections,(x+18,y+540),name,SMALL)
    text(sections,(x+18,y+565),f"Added interior cells: {p['interiorFilledCells']:,} | Enclosed air: {p['enclosedAirCells']}",SMALL)
sections.save(OUT/'solid-sections.png')
context=canvas(1500,1020)
text(context,(18,10),'LOCAL DENSITY STUDY / 56 drifting bodies / NOT the full 2500-unit map')
context.paste(Image.open(OUT/'density-context.png').convert('RGB'),(0,50))
text(context,(18,965),'Orange box = 351.64 x 202.69 x 500 station-size reference ONLY; it is not a rebuilt station.',SMALL)
context.save(OUT/'density-overview.png')
# Compact all-component gallery, same neutral shading but individually framed.
gallery=canvas(2000,720)
text(gallery,(18,10),'ROUND 2  /  Editable hull-block prototypes  /  NOT the assembled station')
for i,p in enumerate(r['prototypes']):
    image=Image.open(OUT/f"raw/{p['name']}-blocks-0.png").convert('RGB');image.thumbnail((400,277))
    x=i%5*400;y=55+i//5*330;gallery.paste(image,(x,y));text(gallery,(x+10,y+274),p['name'].replace('Station_','').replace('Debris_','Wreck: '),SMALL)
    text(gallery,(x+10,y+297),f"{p['cells']:,} cells  /  {p['cellSize']:g} units",SMALL,(159,178,196))
gallery.save(OUT/'prototype-gallery.png')
# Key comparison: 3 structural archetypes, oblique views.
compare=canvas(1300,1480)
text(compare,(18,8),'SOURCE GEOMETRY                                  EDITABLE HULL BLOCKS')
slab=next(p['name'] for p in r['prototypes'] if p['sourceFile']=='mainstru.fbx' and p['sourceIslands']==[179])
for row,name in enumerate(['Station_OpenTruss','Station_CapsuleTank',slab]):
    y=52+row*472
    for col,kind in enumerate(['source','blocks']):compare.paste(Image.open(OUT/f'raw/{name}-{kind}-0.png'),(col*650,y))
    text(compare,(18,y+432),name,SMALL)
compare.save(OUT/'station-comparison.png')
(OUT/'silhouette-metrics.json').write_text(json.dumps(metrics,indent=2),encoding='utf-8')
for p in r['prototypes']:
    vals=[m['silhouetteIoU'] for m in metrics if m['name']==p['name']]
    print(p['name'],p['cells'],p['shapeCounts'],'silhouette',vals)
