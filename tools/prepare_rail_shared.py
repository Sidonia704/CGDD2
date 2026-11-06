from pathlib import Path
import subprocess, json, shutil, hashlib
import numpy as np
from scipy.io import wavfile
root=Path(__file__).resolve().parents[1];doc=root/'Documentation/WeaponRefinement4';doc.mkdir(parents=True,exist_ok=True)
backup=doc/'Before';backup.mkdir(exist_ok=True)
paths=[* (root/'Assets/_Starframe/Audio/116').glob('Rail*.wav'),root/'Assets/_Starframe/Resources/Audio/GameAudioTuning.asset',* (root/'Assets/_Starframe/Settings/Modules').glob('Railgun*Weapon.asset'),* (root/'Assets/_Starframe/Settings/Modules').glob('Gun*Weapon.asset')]
for p in paths:
    if not (backup/p.name).exists():shutil.copy2(p,backup/p.name)
src=root/'Assets/_Starframe/assetsForCodex/116/电磁炮.mp3'
x=np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-i',str(src),'-ar','44100','-ac','1','-f','f32le','pipe:1']),dtype='<f4')
# Full decoded timeline, including startup and tail; constant level only, no trim/fades/time stretching.
gain=.501187/max(abs(x));pcm=np.round(x*gain*32767).astype(np.int16)
dest=root/'Assets/_Starframe/Audio/116/RailShared.wav';wavfile.write(dest,44100,pcm)
report=dict(source=str(src.relative_to(root)),samples=len(pcm),duration=len(pcm)/44100,peakSeconds=int(np.argmax(np.abs(pcm.astype(float))))/44100,gain=float(gain),trimmedSamples=0)
(doc/'audio.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
folder=root/'音效替换';mapping=folder/'音效映射.json';m=json.loads(mapping.read_text(encoding='utf8'))
retired={'RailLarge.wav','RailSmallA.wav','RailSmallB.wav'}
for e in m['entries']:
    if Path(e['sourceAsset']).name in retired:
        old=folder/e['file']
        if old.exists():shutil.copy2(old,backup/old.name);old.unlink()
m['entries']=[e for e in m['entries'] if Path(e['sourceAsset']).name not in retired and Path(e['sourceAsset']).name!='RailShared.wav']
file='116新素材/03_电磁炮_大小共用.wav';shutil.copy2(dest,folder/file)
m['entries'].append(dict(file=file,binding='cues.RailLarge.clip + cues.RailSmall.clip',purpose='大小电磁炮共用完整录音',sourceAsset=str(dest.relative_to(root)),durationSeconds=len(pcm)/44100,channels=1,sourceSha256=hashlib.sha256(dest.read_bytes()).hexdigest(),notes='完整音频，不裁剪；波峰约202.494毫秒，与前摇同步。'))
m['version']=5;m['parameters']='Documentation/WeaponRefinement4/installed.json';mapping.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False))
