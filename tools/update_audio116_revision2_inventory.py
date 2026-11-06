from pathlib import Path
import json,hashlib,shutil,wave
root=Path(__file__).resolve().parents[1];folder=root/'音效替换';mapping=folder/'音效映射.json'
m=json.loads(mapping.read_text(encoding='utf8'))
new=[('SystemUIA','17_系统提示_A.wav','cues.RotatePart.clip'),('SystemUIB','18_系统提示_B.wav','cues.InvalidPlacement.clip + cues.CorePowerOn.clip')]
new += [('Flare'+str(i).zfill(2),'19_干扰弹单次_'+str(i)+'.wav','cues.FlareDeploy.variants['+str(i-1)+']') for i in range(2,7)]
for key,file,binding in new:
    path='116新素材/'+file
    if not any(e['file'].replace('\\','/')==path for e in m['entries']):
        m['entries'].append(dict(file=path,binding=binding,purpose=file,sourceAsset='Assets/_Starframe/Audio/116/'+key+'.wav'))
for e in m['entries']:
    src=root/e['sourceAsset'];dest=folder/e['file']
    if src.exists():
        shutil.copy2(src,dest)
        with wave.open(str(src),'rb') as w:e['durationSeconds']=w.getnframes()/w.getframerate();e['channels']=w.getnchannels()
        e['sourceSha256']=hashlib.sha256(src.read_bytes()).hexdigest()
    if 'LaserRapid.wav' in e['sourceAsset']:e['loop']=False
    if 'FlareDeploy.wav' in e['sourceAsset']:e['binding']='cues.FlareDeploy.variants[0]';e['notes']='每次发射只播放一个起伏，六个片段轮换。'
m['version']=3;m['parameters']='Documentation/Audio116-Revision2/installed.json'
mapping.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
p=folder/'音效清单与替换说明.md';text=p.read_text(encoding='utf8')
text=text.replace('激光速射炮、直射激光和机枪为随开火启停的循环音','直射激光和机枪为随开火启停的循环音；激光速射炮改为逐发播放新短音')
text=text.replace('仅删除旧07、08、09、11；','旧07、08、09、11素材保持删除；07旋转、08无效放置、09核心启动现用新增的两种UI音效补充；')
text += '\n## 最新调整\n\n机枪满功率持续开火12秒过热，散热至零需6秒；音效按实际持续开火状态续播。电磁炮去除前置静音并缩短尾音。提示音削弱高频与尖锐中高频。导弹报警仅取最后一个短音，约109毫秒，原0.9秒至0.12秒的接近节奏保持。干扰弹原素材拆为6个约135–143毫秒单位，每次发射播放1个，轮换使用。1162.mp3的前3段已替换激光速射、直射激光、导弹发射；后2段补充系统UI。\n'
text += '\n新增文件：\n'+''.join('- '+file+' → '+binding+'\n' for _,file,binding in new)
p.write_text(text,encoding='utf8');print('Updated',len(m['entries']),'editable mappings.')
