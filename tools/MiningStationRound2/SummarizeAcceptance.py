"""Summarize actual Unity reports; never infer FPS from editor-only probes."""
from pathlib import Path
import hashlib, json, statistics
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'Documentation/MiningStation-Round2-2026-11-01'
OUT=BASE/'v04'
def load(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def stats(values):
    return {'n':len(values),'median':round(statistics.median(values),3),'min':round(min(values),3),'max':round(max(values),3)}
versions={}
for version in ('v03','v04'):
    shots=[load(OUT/'bench'/f'{version}-{i}.json') for i in range(5)]
    damage={}
    for shot in shots:
        for probe in shot['representatives']:
            name=probe['assetName'].replace('Station_FracturedDeck','Station_NotchedSlab')
            damage.setdefault(name,[]).append(probe['flushMs'])
    versions[version]={
        'allPassed':all(s['passed'] and not s['unsupported'] for s in shots),
        'damageFlushMs':{k:stats(v) for k,v in damage.items()},
        'trussCutFlushMs':stats([s['cut']['flushMs'] for s in shots]),
        'patchStepAvgMs':stats([s['patchPhysics']['stepAvgMs'] for s in shots]),
        'patchStepMaxMs':stats([s['patchPhysics']['stepMaxMs'] for s in shots]),
        'allScenesRestored':all(s['environment']['sceneRestored'] and s['environment']['globalPhysicsUntouched'] for s in shots),
    }
summary={'method':'Five alternating warm-cache editor runs per version, all five station assets + largest rock/debris damaged individually. Surface probe destroys one cell. Cut probe severs the cropped truss. Median/min/max are CPU ms, not FPS; rendering/AI/full-station/sustained-fire were not tested. Functional passed is not a frame-budget pass.','versions':versions}
(OUT/'benchmark-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
audit=[]
for entry in load(ROOT/'Temp/MiningRound2/source-hashes-before.json'):
    actual=hashlib.sha256((ROOT/entry['path']).read_bytes()).hexdigest().upper()
    audit.append({'path':entry['path'],'before':entry['sha256'],'after':actual,'unchanged':actual==entry['sha256']})
(OUT/'source-hash-audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2)); print('source hashes unchanged:',sum(x['unchanged'] for x in audit),'/',len(audit))
assert all(x['unchanged'] for x in audit)
assert all(v['allPassed'] and v['allScenesRestored'] for v in versions.values())

build=load(OUT/'build-report.json')
verify=load(OUT/'verification-report.json')
assets={a['name']:a for a in verify['assets']}
def link(label,path): return f'[{label}](<{(ROOT/path).as_posix()}>)'
def evidence(label,name): return link(label,OUT.relative_to(ROOT)/name)
lines=['# 破损采矿站 · 第二轮小样最终验收（v04）',
'',
'## 结论与范围',
'',
'**10 件可编辑小样的结构、实心性、漂浮碰撞与破坏功能通过；实时战斗性能尚不能按成品验收。** 本轮只完成代表件与局部密度研究，不是完整组件库，不是已拼好的主站或最终地图。未改动游戏运行时代码、任务、AI、飞行手感或玩家场景。',
'',
'尺寸约束为地图 2500 单位、主站源模型最长世界轴 AABB 500 单位（不是已测得的主轴/脊柱长度）。站体源整体包围盒约 351.64 × 202.69 × 500。密度图的橙色线框仅是该尺寸参考。',
'',
'## 此前为何未通过',
'',
'旧验收脚本在非 Play Mode 调用 SceneManager.CreateScene，未能建立独立物理世界，随后正确标记物理探针未执行；这不是一次碰撞失败。现已改为独立 Preview Scene，并用门禁证明默认场景守卫不动、预览场景守卫移动 0.6 单位。当前 failures 与 unsupported 均为空。',
'',
'## 在编辑器中查看',
'',
'打开 `Starframe/开发工具/体块地图工作室`，载入下列布局；不要把旧版本当成最新版：',
'',
'- '+link('组件展廊',Path(build['galleryPath']))+'：10 件独立小样。',
'- '+link('局部密度样区',Path(build['patchPath']))+'：56 个有漂移/自转参数的独立实例。',
'- '+link('站体局部拼装研究',Path(build['assemblyStudyPath']))+'：只有所选站体样本的相对摆位，不代表全站还原。',
'',
'## 最终小样',
'',
'| 可编辑资产 | 格距 | 占格数 | Block8 组 | 顶点数 | 碰撞盒 |',
'|---|---:|---:|---:|---:|---:|']
for p in build['prototypes']:
    a=assets[p['name']]
    lines.append(f"| {link(p['name'],Path(p['assetPath']))} | {p['cellSize']:g} | {p['cells']:,} | {p['largeBlockGroups']} | {a['meshVertexCount']:,} | {a['colliderCount']} |")
lines += ['',
'厚板名称从旧版 FracturedDeck 改为中性的 NotchedSlab，避免误认为薄地板；两件源残骸按实际读感命名为 FracturedChunk / AngledShard，不冒充机械设备。桁架是源岛中央约 39 单位的复用节段，不是用 39 单位冒充完整约 115 单位的源桁架。',
'',
'## 实心与大型块验收',
'',
'除两件镂空框架外，其余 8 件均使用闭合源体积填充：源焊接边界/非流形边数为 0，实心采样相对表层采样确实增加内部占格，封闭空腔数为 0。三个条件联合判断，未把“外壳漏气所以空腔数为零”当作实心。',
'',
'Block8 是现有的 2×2×2 多格船壳模块。保留全部 8 个占格及正确 modelOffset，不是只存一个锚点假装实心；10 件均通过完整足迹检查。仅对完整 Cube 组打包，边界 Slab / Wedge 不乱换。',
'',
'![四件实心切面](<'+(OUT/'solid-sections.png').as_posix()+'>)',
'',
'切面仅为临时预览，保存的资产未被切掉；未使用生成图替代 Unity 证据。所有 8 件的内部增加格数见 '+evidence('构建报告','build-report.json')+'。',
'',
'## 审美取舍',
'',
'多视图审查后保留 v03 的球罐 2.0、笼架 1.25、岩石 3/2.5/1.75 与残骸 1.0；桁架保持 1.0。v03 胶囊 2.0 过方、厚板 2.5 缺口过粗，最终折中为胶囊 1.5、厚板 2.0，而非全退回高成本 v02。最终两件复审接受；残余问题是近看胶囊端盖较钝、倒角有体素台阶、残骸仍偏矿物质感。轮廓 IoU 只作诊断，未拿单一数值替代审美判断。',
'',
'![源件与体块对比](<'+(OUT/'station-comparison.png').as_posix()+'>)',
'',
'## 密度与完整样区物理',
'',
'- 44 岩石 + 12 残骸，共 56 个刚体、77,657 占格、5,112 碰撞盒，使用 5 份共享模板网格。',
'- 三簇 19/19/18、两处留白、三个高度层、6 个游离件；最大尺度 10.0–60.2 单位。布局避让半径 330，局部采样区域 890×180×800，不是铺满整个 2500 地图。',
'- 真实加载所有 56 实例，按游戏运行时的位置、旋转、缩放、质量、漂移和角速度语义初始化，独立物理世界步进 120×1/60 秒。56 件都移动，NaN=0、越界=0、新增世界 AABB 重叠对=0。初始与结束均有 2 对 AABB 相交，这是粗包围盒近似，不能称为“精确无碰撞重叠”。',
'- 受控的匀速、静止、碰撞推动探针也通过。物理测试不含渲染、AI、持续交火或完整主站。',
'',
'![局部密度和500单位尺寸参考](<'+(OUT/'density-overview.png').as_posix()+'>)',
'',
'## 破坏与性能：功能通过不等于不卡顿',
'',
'5 轮 v03 / v04 交替、热缓存、Editor CPU 测量；不是 FPS。v04 的单格致死伤害，重建耗时如下：',
'',
'| 对象 | 中位数 ms | 最小–最大 ms |',
'|---|---:|---:|']
for name,s in versions['v04']['damageFlushMs'].items():
    lines.append(f"| {name.replace('Station_','').replace('Debris_','')} | {s['median']:.1f} | {s['min']:.1f}–{s['max']:.1f} |")
lines += ['',
'胶囊和厚板为保轮廓，v04 比粗糙的 v03 更贵；并未声称 v04 全面更快。相同几何的笼架和桁架也有明显计时波动，可能受 Editor/分配/回收影响，未做 Profiler 归因，不能归咎于某一种原因。',
'',
'桁架截面切除 104 格后，剩余 2,299 格守恒、分成 2 个连通岛 / 2 个刚体，正确产生 1 件新碎片；5 轮切断重建中位数约 136.6 ms。',
'',
'完整样区的每步 PhysX CPU：5 轮均值的中位数约 0.568 ms，各轮均值 0.509–0.634 ms；5 轮观测到的最大单步 3.31 ms。仅能说明该短时漂浮样区的物理开销，不能外推渲染帧率、带伤害的整站性能或大量碎片成本。样区约 121 ms 的总初始化发生在前序资产检查已预热模板之后，不是冷加载成绩；归档探针中的旧字段 coldInitMs 仅指每资产第一份实例，最新版已更名 firstPerAssetInitMs 并写明限制。',
'',
'**仍需解决整网格破坏重建的卡顿，暂不建议直接把这些大件大量投入持续战斗。** 后续若获准，应优先研究同一刚体内分块重建、内部不可见几何剔除及碰撞体更新，再测真实渲染与持续射击；不能把一个刚性件拆成若干无约束漂浮物来伪装成本下降。本轮未越界重写运行时架构。',
'',
'## 原始证据与保护检查',
'',
'- '+evidence('10件图库','prototype-gallery.png')+' / '+evidence('实心切面','solid-sections.png')+' / '+evidence('站体源对比','station-comparison.png')+' / '+evidence('俯视密度','density-top.png'),
'- '+evidence('功能验收报告','verification-report.json')+' / '+evidence('五轮汇总','benchmark-summary.json')+' / '+evidence('构建与实心指标','build-report.json'),
'- '+evidence('源文件哈希审计','source-hash-audit.json')+'：4 个源 FBX、各自导入 meta，以及用户原资产与 meta，10/10 SHA-256 均未改变。用户场景与全局物理设置通过恢复检查。',
'- v01、v02、v03 全部保留，仅供对照，不覆盖；当前建议入口是 v04。',
'',
'## 复现',
'',
'工具位于 '+link('构建脚本',Path('tools/MiningStationRound2/Build.cs'))+'、'+link('验收脚本',Path('tools/MiningStationRound2/Verification.cs'))+'、'+link('打包脚本',Path('tools/MiningStationRound2/Bundle.py'))+' 和 '+link('证据汇总脚本',Path('tools/MiningStationRound2/SummarizeAcceptance.py'))+'。',
'',
'现有资产已生成，构建脚本会拒绝覆盖，不应直接再次 Build。用已运行的 Unity Editor 执行打包生成的 MiningRound2.Verify 可复验，用 MiningRound2.Recapture 可重拍；不要再启动一个 Editor。Benchmark 也有目录防覆盖保护。',
]
(OUT/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
