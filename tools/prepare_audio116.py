from pathlib import Path
import json, shutil, subprocess, hashlib
import numpy as np
from scipy.io import wavfile

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Assets/_Starframe/assetsForCodex/116/116声音.mp3'
DOC=ROOT/'Documentation/Audio116-2026-11-06'
DEST=ROOT/'Assets/_Starframe/Audio/116'
EDIT=ROOT/'音效替换/116新素材'
BACKUP=Path('C:/Users/34808/.codex/visualizations/2026/11/04/01a1a7d9-fe76-7391-a1e5-cb26a9b2f175/Audio116-original-backup')
segments=[
 ('LaserRapid','激光速射炮',0,5.59,'loop'),
 ('DirectLaser','直射激光器',6.93,16.51,'loop'),
 ('RailLarge','大电磁炮',17.52,20.012,'shot'),
 ('RailSmallA','小电磁炮_A',20.609,20.92,'shot'),
 ('RailSmallB','小电磁炮_B',20.953,22.174,'shot'),
 ('Gatling','机枪',22.675,24.859,'loop'),
 ('MissileShot','导弹发射',25.37,27.64,'shot'),
 ('FlareDeploy','干扰弹发射',28.05,28.891,'shot'),
 ('LockAcquired','锁定敌人',29.023,29.516,'alarm'),
 ('EnemyDetected','敌方进入雷达',29.619,30.166,'alarm'),
 ('TargetLost','敌方脱离锁定',30.289,30.694,'alarm'),
 ('EnemyLockWarning','被敌方锁定',30.889,31.642,'alarm'),
 ('HealthShieldWarning','血量护盾警报',32.197,33.04,'alarm'),
 ('OverheatWarning','过热警报',33.10,34.054,'alarm'),
 ('MissileIncoming','导弹警报',34.251,35.053,'alarm'),
 ('Music','背景音乐',36.394,258.03,'music'),
]
# The supplied list contains 16 clips: 15 SFX (including two small-rail variants) and music.
def save(path,x):
    path.parent.mkdir(parents=True,exist_ok=True)
    wavfile.write(path,44100,np.round(np.clip(x,-1,1)*32767).astype(np.int16))
def crossfade_loop(x,n):
    n=min(n,len(x)//4)
    u=np.linspace(0,1,n,endpoint=False)[:,None]
    seam=x[-n:]*(1-u)+x[:n]*u
    return np.concatenate((x[n:-n],seam))
if BACKUP.exists():
    raise RuntimeError('Backup already exists; do not overwrite the baseline')
BACKUP.mkdir(parents=True)
shutil.copytree(ROOT/'Assets/_Starframe/Audio/Clips',BACKUP/'Clips')
shutil.copytree(ROOT/'音效替换',BACKUP/'音效替换')
shutil.copy2(ROOT/'Assets/_Starframe/Resources/Audio/GameAudioTuning.asset',BACKUP/'GameAudioTuning.asset')
raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(SOURCE),'-f','f32le','-acodec','pcm_f32le','-ar','44100','-ac','2','pipe:1'])
x=np.frombuffer(raw,dtype='<f4').reshape(-1,2).copy()
rows=[]
for index,(key,label,start,end,kind) in enumerate(segments,1):
    clip=x[round(start*44100):round(end*44100)].copy()
    save(DOC/'原始分段'/f'{index:02}_{label}.wav',clip)
    if kind!='music':clip=clip.mean(axis=1,keepdims=True)
    clip-=clip.mean(axis=0,keepdims=True)
    if kind=='loop':
        # Keep sustained body, remove original attack/decay, crossfade the loop seam.
        crop={'LaserRapid':(.55,4.75),'DirectLaser':(.50,8.80),'Gatling':(.16,1.95)}[key]
        clip=clip[round(crop[0]*44100):round(crop[1]*44100)]
        clip=crossfade_loop(clip,round(.04*44100))
    elif kind=='music':
        # Preserve the complete track; a brief equal-gain join removes loop silence.
        clip=clip[:round((249.7-start)*44100)]
        clip=crossfade_loop(clip,round(1.5*44100))
    else:
        attack=min(round(.002*44100),len(clip)//4)
        release=min(round(.012*44100),len(clip)//4)
        clip[:attack]*=np.linspace(0,1,attack)[:,None]
        clip[-release:]*=np.linspace(1,0,release)[:,None]
    target={'loop':-6,'shot':-6,'alarm':-8,'music':-5}[kind]
    gain=min(10**(18/20),10**(target/20)/max(float(abs(clip).max()),1e-8))
    clip*=gain
    path=DEST/(key+'.wav');save(path,clip)
    editable=EDIT/f'{index:02}_{label}.wav';editable.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,editable)
    rows.append(dict(key=key,label=label,sourceStart=start,sourceEnd=end,kind=kind,
        asset=str(path.relative_to(ROOT)).replace('\\','/'),editable=str(editable.relative_to(ROOT)),
        duration=len(clip)/44100,channels=clip.shape[1],peakDb=20*np.log10(max(float(abs(clip).max()),1e-9)),
        gainDb=20*np.log10(gain),sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
DOC.mkdir(parents=True,exist_ok=True)
(DOC/'segments.json').write_text(json.dumps(dict(source=str(SOURCE),backup=str(BACKUP),clips=rows),ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(dict(count=len(rows),backup=str(BACKUP),clips=[(r['key'],round(r['duration'],3)) for r in rows]),ensure_ascii=False))
