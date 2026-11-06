from pathlib import Path
import json, subprocess, shutil
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter,sosfilt
ROOT=Path(__file__).resolve().parents[1]; DOC=ROOT/'Documentation/Audio116-Revision2'; DEST=ROOT/'Assets/_Starframe/Audio/116'
EDIT=ROOT/'音效替换/116新素材'; BEFORE=DOC/'Before'; RATE=44100
BEFORE.mkdir(parents=True,exist_ok=True)
for p in list(DEST.glob('*.wav'))+list((ROOT/'Assets/_Starframe/Audio/Clips').glob('*.wav')):
    if p.name=='Music.wav':continue
    target=BEFORE/p.parent.name/p.name;target.parent.mkdir(parents=True,exist_ok=True)
    if not target.exists():shutil.copy2(p,target)
for p in [ROOT/'Assets/_Starframe/Resources/Audio/GameAudioTuning.asset',* (ROOT/'Assets/_Starframe/Settings/Modules').glob('Gun*Weapon.asset')]:
    target=BEFORE/p.name
    if not target.exists():shutil.copy2(p,target)
def load(path):
    sr,x=wavfile.read(path);assert sr==RATE
    if x.ndim>1:x=x.astype(np.float64).mean(axis=1)
    return x.astype(np.float64)/32768
def decode(path):
    return np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ac','1','-ar',str(RATE),'-f','f32le','pipe:1']),dtype='<f4').astype(float)
def trim(x,threshold=.02):
    active=np.flatnonzero(abs(x)>max(abs(x).max()*threshold,1e-5))
    return x[max(0,active[0]-44):min(len(x),active[-1]+220)] if len(active) else x
def edges(x,attack=.001,release=.015):
    x=x.copy();a=min(int(attack*RATE),len(x)//3);b=min(int(release*RATE),len(x)//3)
    if a:x[:a]*=np.linspace(0,1,a)
    if b:x[-b:]*=np.linspace(1,0,b)
    return x
def soften(x):
    # Remove brittle high harmonics; no post-filter normalization that would undo attenuation.
    filtered=subprocess.check_output(['ffmpeg','-v','error','-f','f32le','-ar',str(RATE),'-ac','1','-i','pipe:0','-af',
        'equalizer=f=2800:t=q:w=0.8:g=-5,lowpass=f=4800','-f','f32le','pipe:1'],input=x.astype('<f4').tobytes())
    return np.frombuffer(filtered,dtype='<f4').astype(float)*.85
def level(x,db=-6):
    return x*min(10**(18/20),10**(db/20)/max(abs(x).max(),1e-8))
def loop(x):
    n=min(int(.035*RATE),len(x)//4);u=np.linspace(0,1,n,endpoint=False)
    return np.r_[x[n:-n],x[-n:]*(1-u)+x[:n]*u]
rows=[]
def write(key,x,reason,editable=None):
    x=np.clip(x,-.98,.98);path=DEST/(key+'.wav');wavfile.write(path,RATE,np.round(x*32767).astype(np.int16))
    if editable:shutil.copy2(path,EDIT/editable)
    rows.append(dict(key=key,duration=len(x)/RATE,peak=float(abs(x).max()),reason=reason))
new=decode(ROOT/'Assets/_Starframe/assetsForCodex/116/1162.mp3')
for key,a,b,kind,name in [
    ('LaserRapid',.055,.355,'shot','01_激光速射炮.wav'),
    ('DirectLaser',.724,2.665,'loop','02_直射激光器.wav'),
    ('MissileShot',3.285,4.23,'shot','07_导弹发射.wav'),
    ('SystemUIA',4.644,4.83,'ui','17_系统提示_A.wav'),
    ('SystemUIB',5.287,5.36,'ui','18_系统提示_B.wav')]:
    x=trim(new[int(a*RATE):int(b*RATE)]);x-=x.mean()
    x=level(loop(x) if kind=='loop' else edges(x),-7 if kind!='ui' else -9)
    if kind=='ui':x=soften(x)
    write(key,x,dict(source='1162.mp3',start=a,end=b,mode=kind),name)
old=ROOT/'Documentation/Audio116-2026-11-06/原始分段'
for key,file,limit,edit in [('RailLarge','03_大电磁炮.wav',.65,'03_大电磁炮.wav'),('RailSmallA','04_小电磁炮_A.wav',.28,'04_小电磁炮_A.wav'),('RailSmallB','05_小电磁炮_B.wav',.40,'05_小电磁炮_B.wav')]:
    x=trim(load(old/file),.07)[:int(limit*RATE)]
    write(key,level(edges(x,release=.055)), 'Remove pre-onset silence and shorten reverb tail',edit)
# Keep all six individual flare pulses, cycle one unit per launcher deployment.
x=load(old/'08_干扰弹发射.wav');cuts=[0,.143,.282,.424,.564,.699,.841]
for i,(a,b) in enumerate(zip(cuts,cuts[1:])):
    key='FlareDeploy' if i==0 else 'Flare'+str(i+1).zfill(2)
    y=level(edges(x[int(a*RATE):int(b*RATE)],.003,.009),-7)
    write(key,y,dict(source='116声音.mp3',start=28.05+a,end=28.05+b,unit=i+1),'08_干扰弹发射.wav' if i==0 else '19_干扰弹单次_'+str(i+1)+'.wav')
mapping=[('LockAcquired','09_锁定敌人.wav'),('EnemyDetected','10_敌方进入雷达.wav'),('TargetLost','11_敌方脱离锁定.wav'),
('EnemyLockWarning','12_被敌方锁定.wav'),('HealthShieldWarning','13_血量护盾警报.wav'),('OverheatWarning','14_过热警报.wav')]
for key,file in mapping:
    x=soften(level(edges(load(old/file)),-8));write(key,x,'Soften notification upper mids and harmonics',file)
# Last isolated tone, not the entire earlier three-pulse alarm phrase.
x=load(old/'15_导弹警报.wav')[int(.639*RATE):int(.802*RATE)]
x=np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-f','f32le','-ar',str(RATE),'-ac','1','-i','pipe:0',
    '-af','atempo=1.6','-f','f32le','pipe:1'],input=x.astype('<f4').tobytes()),dtype='<f4').astype(float)
x=soften(level(edges(x),-8));write('MissileIncoming',x,'Last short tone, pitch-preserving time compression to fit original120ms fastest cadence','15_导弹警报.wav')
# Also soften existing UI/warning cues from the pristine external backup, exactly once.
backup=Path(json.loads((ROOT/'Documentation/Audio116-2026-11-06/segments.json').read_text(encoding='utf8'))['backup'])/'Clips'
for name in ['UI_Select','Blueprint_Save','Warning_Confirm','Boundary_Warning','Respawn_Reboot']:
    path=ROOT/'Assets/_Starframe/Audio/Clips'/f'{name}.wav'
    wavfile.write(path,RATE,np.round(np.clip(soften(load(backup/f'{name}.wav')),-1,1)*32767).astype(np.int16))
(DOC/'processing.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(rows,ensure_ascii=False))
