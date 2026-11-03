"""Deterministic dry mechanical cues; no swept oscillators or sustained noise beds."""
from pathlib import Path
import json, wave, shutil
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
CLIPS=ROOT/'Assets/_Starframe/Audio/Clips'
BACKUP=ROOT/'Documentation/SessionNov03/audio-environment/before'
SR=48000

def strike(duration, frequency, decay, gain=1):
    t=np.arange(round(duration*SR))/SR
    # Inharmonic, fixed resonances give a dull casing impact rather than a musical glide.
    body=(np.sin(2*np.pi*frequency*t)*np.exp(-t/decay)
          +.33*np.sin(2*np.pi*frequency*2.71*t+.2)*np.exp(-t/(decay*.36))
          +.14*np.sin(2*np.pi*frequency*6.19*t)*np.exp(-t/.0035))
    attack=np.minimum(1,t/.0008)
    release=np.minimum(1,(duration-t)/.008)
    return body*attack*release*gain

def cue(name,duration,hits):
    path=CLIPS/(name+'.wav')
    target=BACKUP/(name+'.wav')
    BACKUP.mkdir(parents=True,exist_ok=True)
    if path.exists() and not target.exists():shutil.copy2(path,target)
    y=np.zeros(round(duration*SR))
    for start,freq,decay,gain in hits:
        offset=round(start*SR)
        sound=strike(duration-start,freq,decay,gain)
        y[offset:offset+len(sound)]+=sound
    y*=.78/max(1,float(np.max(np.abs(y))))
    pcm=np.round(np.clip(y,-1,1)*32767).astype('<i2')
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(SR);w.writeframes(pcm.tobytes())
    return dict(clip=name,seconds=duration,peak=float(np.max(np.abs(y))),rms=float(np.sqrt(np.mean(y*y))),hits=len(hits))

specs=[
 ('Flare_Deploy',.24,[(0,130,.042,1),(.046,172,.027,.72),(.094,119,.035,.56)]),
 ('Enemy_Detected',.16,[(0,380,.019,.7),(.057,380,.015,.4)]),
 ('Enemy_Lock_Warning',.22,[(0,470,.025,.8),(.08,470,.025,.8)]),
 ('UI_Select',.065,[(0,470,.011,.7)]),
 ('Blueprint_Save',.15,[(0,240,.018,.7),(.05,320,.02,.45)]),
 ('Warning_Confirm',.18,[(0,220,.025,.7),(.07,220,.023,.55)]),
 ('Rotate_Servo',.09,[(0,235,.018,.7),(.022,410,.008,.18)]),
 ('Core_Power_On',.24,[(0,110,.07,.7),(.035,195,.045,.35)]),
 ('Lock_Acquired',.13,[(0,410,.025,.65),(.045,410,.019,.45)]),
 ('Boundary_Warning',.23,[(0,360,.026,.7),(.1,360,.026,.7)]),
 ('Respawn_Reboot',.27,[(0,100,.08,.7),(.085,235,.024,.4)]),
]
report=[cue(*s) for s in specs]
(BACKUP.parent/'audio-preparation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
