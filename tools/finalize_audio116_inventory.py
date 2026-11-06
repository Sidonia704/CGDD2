from pathlib import Path
import json
root=Path(__file__).resolve().parents[1]
doc=root/'Documentation/Audio116-2026-11-06'
segments=json.loads((doc/'segments.json').read_text(encoding='utf8'))
baseline=json.loads((Path(segments['backup'])/'音效替换/音效映射.json').read_text(encoding='utf8'))
retained={e['binding'] for e in baseline['entries']}-{'cues.RotatePart','cues.InvalidPlacement','cues.CorePowerOn','cues.LaserImpact'}
entries=[]
for e in baseline['entries']:
    if e['binding'] in retained:
        if e['binding']=='cues.Select':e['gain']*=.7
        e['notes']='旧素材已恢复；有116新版的用途使用新版，其余恢复旧播放设置。仅01音量降低30%。'
        entries.append(e)
for e in segments['clips']:
    binding='cues.'+e['key']+'.clip'
    if e['key']=='RailSmallA':binding='cues.RailSmall.clip'
    if e['key']=='RailSmallB':binding='cues.RailSmall.alternateClip'
    if e['key']=='Music':binding='workbenchMusic + flightMusic'
    entries.append(dict(file=str(Path(e['editable']).relative_to('音效替换')),purpose=e['label'],binding=binding,
        sourceAsset=e['asset'],durationSeconds=e['duration'],channels=e['channels'],sourceSha256=e['sha256'],
        loop=e['kind'] in ('loop','music'),sourceStart=e['sourceStart'],sourceEnd=e['sourceEnd']))
(root/'音效替换/音效映射.json').write_text(json.dumps(dict(version=2,entries=entries,
    replacementMode='素材已接入游戏；本文件夹仍是编辑副本，后续修改后按映射同步至Assets。',
    parameters='Documentation/Audio116-2026-11-06/installed.json',backup=segments['backup']),ensure_ascii=False,indent=2),encoding='utf8')
lines=['# 116 音效素材清单','',
'新素材共16段（15段音效＋1段音乐），已接入游戏。两段小电磁炮交替播放；激光速射炮、直射激光和机枪为随开火启停的循环音。背景音乐在菜单和飞行场景播放。',
'','仅删除旧07、08、09、11；旧01音量降低30%。其余旧素材已恢复，有116新素材的用途使用新版，其他用途恢复旧播放设置，包括引擎循环。加力启动原音量为0，保持原设置。',
'','本文件夹是编辑副本。修改时保留文件名；修改完成后告知我，以便同步游戏引用。循环文件请保留首尾衔接。',
'','血量或现有护盾低于25%时共用一次警报，最多每10秒一次；机枪进入过热时提示一次，冷却恢复后可再次触发。导弹警报使用保留的专用声源，整段播放期间不会重复堆叠。',
'','|文件|对应绑定|','|---|---|']
lines += [f"|{e['file']}|{e['binding']}|" for e in entries]
lines += ['', '外部旧素材备份：`'+segments['backup']+'`。',
    '', '原MP3、完整原始分段、切点和处理记录保存在116源文件夹及Documentation/Audio116-2026-11-06。运行时音量、冷却和音高详见该目录installed.json。']
(root/'音效替换/音效清单与替换说明.md').write_text('\n'.join(lines)+'\n',encoding='utf8')
print('Wrote',len(entries),'mappings (19 restored old files + 16 new).')
