from pathlib import Path
import json, shutil, hashlib, wave
import numpy as np
from scipy.io import wavfile

root=Path(__file__).resolve().parents[1]
dest=root/'Assets/_Starframe/Audio/116'
doc=root/'Documentation/Audio116-Revision3'
backup=doc/'Before';backup.mkdir(parents=True,exist_ok=True)
rate=44100
for p in [dest/'Gatling.wav',*dest.glob('Rail*.wav'),root/'Assets/_Starframe/Resources/Audio/GameAudioTuning.asset',* (root/'Assets/_Starframe/Settings/Modules').glob('Sword*Weapon.asset')]:
    if not (backup/p.name).exists():shutil.copy2(p,backup/p.name)
def read(p):
    r,x=wavfile.read(p);assert r==rate
    x=x.astype(float)/32768
    return x.mean(axis=1) if x.ndim>1 else x
def write(name,x):
    wavfile.write(dest/(name+'.wav'),rate,np.round(np.clip(x,-.98,.98)*32767).astype(np.int16))
raw=read(root/'Documentation/Audio116-2026-11-06/原始分段/06_机枪.wav')
raw*=.501/max(abs(raw)) # one shared gain preserves timbre and relative phase levels
start=raw[int(.18*rate):int(.45*rate)].copy()
body=raw[int(.45*rate):int(1.45*rate)].copy()
end=raw[int(1.45*rate):int(1.92*rate)].copy()
n=int(.035*rate);t=np.linspace(0,1,n,endpoint=False)
# Periodic seam: crossfade end into beginning, then continue at beginning+n.
loop=np.r_[body[n:-n],body[-n:]*(1-t)+body[:n]*t]
# Intro ends at the same source region as loop begins. Blend only the join.
start[-n:]=start[-n:]*(1-t)+body[:n]*t
start[:int(.003*rate)]*=np.linspace(0,1,int(.003*rate))
end[:int(.004*rate)]*=np.linspace(0,1,int(.004*rate))
end[-int(.12*rate):]*=np.linspace(1,0,int(.12*rate))
write('GatlingStart',start);write('Gatling',loop);write('GatlingEnd',end)
rows=[]
for name in ['RailLarge','RailSmallA','RailSmallB']:
    x=read(backup/(name+'.wav'));peak=int(np.argmax(abs(x)));target=round(.08*rate)
    x=x[peak-target:] if peak>=target else np.r_[np.zeros(target-peak),x]
    x[:int(.001*rate)]*=np.linspace(0,1,int(.001*rate))
    write(name,x)
    rows.append(dict(name=name,peakSeconds=float(np.argmax(abs(x))/rate),duration=len(x)/rate))
windows=[np.sqrt(np.mean(z*z)) for z in np.array_split(loop,len(loop)//441)]
report=dict(rail=rows,gatling=dict(startSeconds=len(start)/rate,loopSeconds=len(loop)/rate,endSeconds=len(end)/rate,
    minimum10msRms=float(min(windows)),median10msRms=float(np.median(windows)),seamDelta=float(abs(loop[-1]-loop[0]))))
(doc/'processing.json').write_text(json.dumps(report,indent=2),encoding='utf8')
folder=root/'音效替换';p=folder/'音效映射.json';m=json.loads(p.read_text(encoding='utf8'))
for name,file,binding in [('GatlingStart','20_机枪启动.wav','cues.Gatling.startClip'),('GatlingEnd','21_机枪停火.wav','cues.Gatling.endClip')]:
    if not any(e['sourceAsset'].endswith('/'+name+'.wav') for e in m['entries']):
        m['entries'].append(dict(file='116新素材/'+file,binding=binding,purpose=file,sourceAsset='Assets/_Starframe/Audio/116/'+name+'.wav'))
for e in m['entries']:
    src=root/e['sourceAsset']
    if not src.exists():continue
    shutil.copy2(src,folder/e['file'])
    with wave.open(str(src),'rb') as w:e['durationSeconds']=w.getnframes()/w.getframerate();e['channels']=w.getnchannels()
    e['sourceSha256']=hashlib.sha256(src.read_bytes()).hexdigest()
    if src.name=='Gatling.wav':e['notes']='仅持续射击阶段，稳定无淡出循环；启动/结束使用独立文件。'
m['version']=4;m['parameters']='Documentation/Audio116-Revision3/installed.json'
p.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,indent=2))
