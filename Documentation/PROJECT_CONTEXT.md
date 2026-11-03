# Starframe 当前交接（2026-11-03）

后续对话先读本文件，再读 STATUS.md 顶部。按任务定位代码，不必重读全部历史报告。这里描述当前版本，旧 Documentation 子目录是历史证据，不是待执行计划。

## 当前范围与最新决定

- Unity 6000.3.25f1 / URP；工程 D:/software/unity/CGDD26107，主要内容 Assets/_Starframe/。
- 已有完整 Demo 流程：首页展示当前船 → 装配／独立蓝图库／武器组／设置／靶场／出击确认 → 五波关卡 → 胜利或失败结算 → 返回。首页只显示一个关卡选项。
- 最新决定：**关闭游戏地图破坏，不执行第二轮平衡校准。** 地图仍有碰撞、质量和无重力漂浮；舰船部件、护盾、死亡、幸存残骸仍按原战斗规则工作。不要把地图关闭破坏扩展成全游戏无敌，也不要擅自恢复长时间校准任务。
- 第一轮平衡已完成：标准出击上限30000cost，t1–t7正式接入，五波共53敌人。约15分钟只是设计目标，未完成真人时长校准；第二轮已取消。
- 用户允许忽略课程CGDD/AGENTS记录、导出与确认流程。正常修改、限定范围验证即可；不要自动写反思或发布网站。

## 场景和玩家流程

以下相对路径均以 Assets/_Starframe/ 为根：

| 入口 | 文件 | 职责 |
|---|---|---|
| 首页 | Scenes/Frontend/Hangar.unity | 当前工作船展示、单关卡、蓝图库、设置、靶场入口 |
| 装配 | Scenes/Construction/ShipEditor.unity | 建造、部件参数、当前船机动／电力汇总、试飞 |
| 靶场 | Scenes/Testing/FlightRange.unity | 蓝图靶机数量／模式设置、自由飞行战斗、退出返回 |
| 正式关卡 | Scenes/Gameplay/SaturnRing.unity | 五波有限敌人、分批增援、计分和胜负 |

- App/GameAppBootstrap、GameAppScreens及各Screen负责前端；MissionSession保存出击时的深拷贝船，MissionResults负责结算返回。
- 蓝图管理独立于装配；靶机蓝图／数量／静止／飞行／射击／完整AI模式统一在靶场设置，不要塞回蓝图库。
- 设置已有中英文、画面／音量／输入及UI偏好；Localization/GameUI.csv为文案入口，英文排版要与中文一起检查。
- 控制：W/S前后，A/D左右，Space/Ctrl上下，Q/E滚转，Shift加力，鼠标偏移持续转向；F锁定／解除，R每次释放一台干扰弹，V切换弹道武器齐射／交替，数字1–8开关武器组，再按该组绑定开火。Esc暂停／退出。

## 数据、蓝图与扩展

- Settings/Modules/ModuleDatabase.asset引用ModuleDefinition，是模型、材质、图标、成本、质量、血量、占格、电力、护盾及武器引用的权威。WeaponDefinition区分Projectile／GuidedMissile／Beam。
- 普通八种Hull免费，装甲及功能件收费；以原生资产当前值为准，不拿C#默认值或历史安装器覆盖。
- BlockShape序号兼容旧存档，新增只能追加。常用旧ID：Cube0/Wedge1/Thruster1=2/Thruster2=3/Slab4/Pyramid5/Core6/Laser7/Power8/Shield9；Thruster6=36，FlareLauncher=37。
- BlueprintData v2保存稳定部件id、shape、position/rotation、核心和前向、武器组及绑定。ShipWorkspace保存工作中的船，BlueprintRepository保存具名蓝图。
- 用户存档在 Application.persistentDataPath（本机 C:/Users/34808/AppData/LocalLow/DefaultCompany/CGDD26107）：Blueprints/、Workspace/current-ship.json、Settings/preferences.json。测试必须隔离存储，不覆盖用户工作船。
- 用户最终敌舰源文件为蓝图库 t1.json–t7.json；正式快照在 Data/Enemies/needle/viper/wasp/bastion/harrier/halberd/citadel.json，同名asset引用它们。库内再编辑不会自动改变已安装敌舰，需要明确重新导入。citadel.alternateBlueprint已指向同一份t7，避免第五波使用旧舰。
- 添加组件：配置ModuleDefinition并加入数据库；导入模型／材质／两态图标；确认占格、底面、旋转、面连接、枪口／喷口方向及分组，再做少量针对性验证。提供新武器交付方式时必须有执行器，不能只加枚举。

## 船体和战斗规则

- ShipAssembly／ShipEditorController／ShipEditorUI分别负责部件视图、输入和装配UI。BlockPlacement处理附着面的姿态及绕面90度旋转；R旋转、X删除、MMB旋转视角、Shift+MMB平移、滚轮缩放。
- 所有功能件使用完整整数占格，视觉可略小或超出。Laser占3格，Thruster1/2本体1/2格并保留喷口后2/3格；连接只算本体。斜面不可作为任意贴合底面，边／点相接不算面连接。
- BlockConnectivity检查真实面接触及底面支撑；WeaponClearance反向检查新块是否挡住旧武器、枪口／干扰弹上方净空。不能拿伤害弹径当模型放置净空。
- FlightShipRuntime负责部件生命、拓扑、修复、核心死亡及性能重算；ShipPerformance缓存质量、惯量和喷口贡献。飞控采用**独立平衡街机六轴**，布局和推进器朝向仍决定性能，禁止重新积分偏置副作用力矩导致持续滚转。
- RangeCombatWorld组织真实角色／AI／供电；ShipWeapons控制分组、射击、冷却和能量；ProjectileWorld／CylindricalHitQuery负责连续弹道、爆炸和命中。
- 仅血量归零的部件消失；断连幸存部分和核心死亡后的剩余船体交给DebrisWorld／DebrisCluster，保持可碰撞、可摧毁、漂浮。不要恢复“断连整片删除”。
- 结构维修按每个部件最近实际船体伤害/脱落时间独立等待6秒；护盾完全吸收命中不再重置结构维修，其他部位受伤也不清空断件进度。重建按邻接支持逐层进行，完成时用略收缩的整数格检查外部实体占位，忽略自身/触发器，使用实际装配局部坐标和刚体姿态；有残骸占位则等待让开，保留进度。射弹半径未缩小；同一发弹丸已有命中集合防止重建重复受伤。
- ShipPowerBus统一供能，ShipShieldSystem维护独立护盾。导弹撞上仍有电荷的护盾时，即使破盾也挡住当枚，后续导弹再判；直射破盾溢伤仍保留。护盾恢复受自身命中及实际船体伤害计时控制。
- TargetLockSystem按实际显示操纵光标／船头准星锁定已发现目标；F按键按InputSystem更新去重，出入页面和快速点按均有回归。CoreVelocityEstimator／InterceptSolver基于核心运动计算与弹丸一致的提前点。弹丸不继承射手速度；不要缩小世界RenderTexture导致瞄准坐标错位。
- 导弹有有限转向、加速度、视锥和追踪时间，失锁后不重锁原目标，可撞地形；AI按来袭时间和真实发射器位置提前释放，遵守上方净空、每台10秒CD及每次齐射概率0.5。

## 当前数值与波次

- 调整菜单 **Starframe → 平衡参数 → 伤害倍率**；Settings/Combat/CombatDamageTuning.asset仅两个值：nonMissileDamageMultiplier=0.60、missileDamageMultiplier=0.25。
- 最终伤害=WeaponDefinition.damage×分类倍率×攻击方系数；BattleTuning.enemyWeaponDamageMultiplier=0.50，玩家Team0另乘playerDirectDamageMultiplier=1.10或playerMissileDamageMultiplier=0.85，敌方实际伤害不受这两个新字段影响。光束／穿透／爆炸不重复乘。导弹连装分摊齐射伤害；结构爆炸总预算1.5倍，原始武器伤害未抹掉。装配面板读取相同分类倍率。
- 最新易生存调整：BattleTuning.botDirectAimErrorMultiplier=5、botDirectVelocitySampleSeconds=0.35，只作用敌方非导弹；按带误差的瞄准点决定开火，避免暗中等真实核心对准才射击。玩家护盾playerShieldRecoveryMultiplier=2、playerShieldDelayMultiplier=0.5：供电足够时受击后3秒开始恢复、空盾约2秒充满；不加额外额定耗电。敌方护盾及导弹瞄准不应用这些修正。本次用户要求不运行测试，仅编译。
- Settings/Flight/FlightTuning.asset调飞控／镜头；BattleTuning.asset调AI／恢复／锁定／重生；Settings/Game/ice.asset为正式出击预算；SaturnRingWaves.asset为波次。
- t1–t7当前部件数111/172/75/301/293/344/419，cost2031/3410/3110/9421/7969/18461/16156。定位依次为机炮轻机、导弹支援、轨道炮轻机、综合舰、激光舰、重轨道炮＋近光束舰、厚重机炮旗舰。
- 波次：①t1×5+t3×2；②t1×4+t2×2+t3×3+t4×1；③t1×4+t2×2+t3×3+t4×2+t5×1；④t1×3+t2×2+t3×2+t4×1+t5×2+t6×1；⑤t1×3+t2×2+t3×3+t4×1+t5×1+t6×2+t7×1。
- 总53=40小型/12大型/1旗舰；同时最多10敌人、3大型、1旗舰、3直接进攻机会。五阶段连续投放，不要求清场或进入准备菜单；前阶段队列已投放、至少35秒后，存活≤3或阶段达135秒时开始下一阶段。旧阶段存活继续计数，最后全部投放并清场才胜利。玩家全程飞行，HUD左侧仅阶段编号如3/5，无倒计时/敌人数/读条/框。
- 正式关卡minimumArenaHalfExtent=2200，中央战区比例.62；Bootstrap在创建边界/飞控前应用同一有效尺寸。敌机居中出生、越界软回场，地图资产不搬动。出生高度避开底部大结构；靶场规则独立。
- V（GameAction.WeaponCadence，可重绑定）切换弹道武器齐射/交替，保留组1–8开关；HUD显示当前模式。自动武器错开相位保持单炮持续DPS，点按/蓄力弹丸延后实际出弹。全船导弹按实际弹体约6枚/秒轮询发射，保留锁定、供电、净空、装填限制，无掉帧补射洪峰。ShipWeapons.MissileLaunchesPerSecond为节奏入口；电磁炮三型号shotsPerSecond=1.50/1.25/1.00（本轮+25%）。

## 美术与声音交接

- 视觉方向：硬边倒角体素、塑料高光、接缝暗部，白／奶白／淡灰的小灰度差；发光组件和发光体素粒子。不要大幅跳色或改回全黑白。
- 当前背景是深蓝模拟空间、低亮度单色橙色巨行星；已不用旧星空HDRI，**没有像素滤镜，世界景深模糊关闭**。远景靠淡出与LOD。旧HDRI材质还在，但并非当前场景基准。
- 当前天空：Art/SimulationTrial/SimulationBay.mat（装配）／SimulationSpace.mat（飞行），Art/Shaders/Environment/SimulationSky.shader；原环境、陨石带及其他材质在Art/Materials/Environment。场景灯光、Volume和反射一起决定塑料观感，不要只调基色补光。
- 部件材质在Art/Materials/Ships/及Expansion/，实际引用从ModuleDatabase追；核心白发光、电源浅蓝发光、护盾蓝发光，能量护盾保持低强度多层可见。新模型在Art/Models/Modules，图标在Art/UI/Construction/Icons。
- UI：现有uGUI脚本构建，复用Presentation/UiTheme、MenuSurface、UiStyle及Flight/GlassUiPalette；Resources/UI/WorkbenchTheme.asset是共享主题。均匀磨砂玻璃、无蓝色描边、无大面积上下渐变。
- MissionHUD仅左侧阶段编号如3/5，结算仍显示胜败文字；交叠投放阶段以首艘实际入场更新编号。
- 战斗HUD只有双方姿态监视器保留背景；速度／能量／护盾使用图形仪表。当前关键仪表色combatInstrument为黄绿色，文字和舰体示意中性，敌方／受损为红色；地图命中为弱白色。HUD涉及RangeHUD、FlightInstrumentGraphic、ShipStatusGraphic、SphereRadarGraphic、TargetAimGraphic、ReticleHitFeedback。改颜色先调主题，避免每个脚本硬编码。
- 激光／推进器是流动发光体素：LaserVisualPool／SpecialWeaponVisualPool／CombatVfxPool／ThrusterExhaust。可改视觉尺寸、亮度和寿命，不能改变占格或让长尾伸到枪口后方。
- 音频：Resources/Audio/GameAudioTuning.asset、Audio/Clips/、App/GameAudio及ThreatAudioDriver。当前导弹来袭为2kHz/60ms干脆pip，距离或预计命中时间驱动0.90→0.12s节拍，独占总16音源中的1个，避免叠响；敌方锁定提示独立。土星关卡已按FlightRangeBootstrap识别并延迟绑定战斗声音。
- 如只改美术，保留蓝图／武器数值／飞控／AI／胜负，按页面或材质组留下可独立回退的修改。

## 地图与性能约束

- 开发工具菜单 Starframe → 开发工具 → 体块地图工作室；Scripts/Editor/Maps/SpaceMapStudioWindow各partial负责UI／预览／编辑。不要挂进玩家首页。
- Maps/ReferenceMiningStation/Layouts/ReferenceMiningStation3500.asset是当前3500米地图：2345可编辑资产、2854实例、约312万实例加权格。大型岩体固定4–5米格，设施细部1米；八种基础Hull都已使用，模型／材质与舰船一致，VoxelAssetDefinition.baseColor／surfaceColors只调整地图侧着色。
- 地景在沙盒下方Y约−1520..−1102，上方是开放战斗空间。外围陨石条带是共享实例化纯视觉背景，无逐块血量或碰撞，不要与可碰撞地图混淆。
- **当前关闭破坏**：SpaceMapRuntime.allowDestruction默认false；BuildOne给所有游戏结构设置damageEnabled=false。VoxelStructureRuntime.ApplyDamage立即返回0，不建立临时HP、不删格、不拆分；PrewarmDamageVisuals跳过。地图仍是有质量的无重力动态刚体，碰撞／推动不受影响。
- 弹丸击中不可破坏地图在首表面停止，光束／导弹仍有命中特效和弱白反馈。导弹爆炸跳过地图逐格伤害扫描。舰船残骸保持原可破坏规则。
- 开发工具单资产破坏预览仍可用；孤立VoxelStructureRuntime默认damageEnabled=true，已有稀疏HP／断连实现留存供开发。只有明确重新开启地图破坏时才设置allowDestruction=true并重建地图，不能仅因旧测试或旧记录恢复。
- 保留分帧加载、延迟精碰撞、共享模板、实例渲染和预算层。地图完整资产一个刚体，不能改成每格GameObject／Update。大型物件根据真实质量限制撞击位移。
- 冷精碰撞每帧最多24盒、软预算1.5ms，完整代理在切换准备完成前保留。不要重新引入飞近时整块同步重建。关闭破坏后不要预热不会使用的受损外壳。
- VoxelTemplateCache引用计数防止共享网格被提前销毁；接缝内表面和法向已确认正常，不要全量翻转法向。船／残骸实例缓存依赖TopologyRevision，所有拓扑变化必须更新它。

## 后续验证的最小范围

1. 先读入口和实际资产；源码可正常改，场景／Prefab／材质／导入设置通过Unity API修改，不手改YAML/.meta，不覆盖存档。不全量回滚／提交／发布。
2. Unity CLI路径 C:/Users/34808/AppData/Local/Unity/bin/unity.exe；本轮已检查beta15为最新。Editor调用带 --caller plugin --skill unity-cli --project-path D:/software/unity/CGDD26107。当前Pipeline原生命令eval/run_script/recompile/run_tests；源码工具在tools/，不必放Assets触发重编译。
3. 只跑改动相关的少量测试。Play要检查帧推进和加载完成；尊重用户自己的Play会话，自己开启的验证结束后停止并恢复后台／auto-tick。未来美术对话不应默认跑全套战斗测试或15分钟关卡。
4. 最近大范围证据：Documentation/FinalBalanceRound1-2026-11-03/README.md。第一轮54/54运行测试，561项编辑器用例经局部复查覆盖通过；实际53敌舰的加速五波／结算／重进／死亡／8533件残骸验证通过。加速强制击毁仅证明流程，不是真人难度结论。
5. 三艘t4、600m静止精确瞄准和真实供电基准：首次有效命中后t4/t6/t7约2/3/3秒核心毁坏；不能视作真人击杀时长。独立包实测和15分钟真人校准没有完成，用户已取消本轮继续校准。
6. 本次地图关闭破坏的专项证据：Documentation/MapDamageOff-2026-11-03/。历史美术依据按需看SimulationLook、CombatHud、UIIteration报告；不要执行历史安装器恢复旧船、HDRI、cost或伤害。

## 最新地景美术分层（2026-11-03）

- 本轮用户已批准地景分色：大部分陨石是中明度暖灰／石灰／冷灰哑光，74个冰质资产保留浅色；设施主体奶白，桁架深灰，散落设备降低亮度。此条更新前面“全白小灰度差”的旧基准。
- Art/SceneryArt/*.mat 是地图独立共享调节入口；ScenerySurface.shader 控制大色块、面间对比与远景收敛。VoxelHullAppearance 优先使用显式 surfaces，无覆盖时保留舰体材质。粗细LOD一致，不改变格子、碰撞、物理、破坏开关或舰船材质。
- 本轮未增加粒子／灯光／UI装饰。原生靶场后图见 Documentation/SceneryArt-2026-11-03/after.png；诊断相机图观感与原生后处理不同。校验2345资产／18760槽位通过；未做独立包性能测量。
- tools/SceneryArt20261103/Install.cs 提供 Audit/Restore；地图原色回退清单是重建基线，非逐资产完整快照，详情及限制见该目录README与baseline-provenance.txt。不要把检查到的空清单当成功。

## 2026-11-03 连续战斗收尾

- 11个当前启用的尖滑提示音已替换为短促机械声；干扰弹Flare_Deploy为低沉三连弹射，GameAudioTuning gain=.42。用户引擎/激光/方块声、2kHz来袭警报保留。脱离锁定仍为锁定音低音调版本。
- RangeAtmosphereTuning.starDirection=(.62,.52,.58)，实际方向光与SimulationSky恒星光盘共用方向，旧近处Star Direction Reference仅在运行时禁用渲染；恒星与橙行星约126度分离，天空背景仍深蓝，无景深模糊。
- 本轮备份/安装器/测试证据见Documentation/SessionNov03和Documentation/ContinuousMission-2026-11-03；不运行历史数值安装器覆盖新值。

- 本轮验证：初始Edit573/573，最终维修坐标专项13/13；Play55项经53项初次通过+2项复查通过覆盖（不是单次全绿报告）。实际土星关卡471帧、1/5、敌机入场、2200边界、自由飞行及恒星方向一致通过；截图/报告在SessionNov03。GameInput对键鼠路径优先使用当前设备，避免热插拔/多键盘时V读取旧设备。验证Play已停止。

- 最新音频回退：SessionNov03的11音效替换已撤回，原cue参数恢复。仅干扰弹保留该轮取消锁定机械音（独立Flare_Deploy.wav、pitch.75/gain.4）；导弹发射沿用旧音效单独降调10%，直射声和来袭警报不变。ProjectileWorld.EmittingMissileShot只标识发射事件供音频选择音调。详情Documentation/AudioRollback-2026-11-03。

## 最新武器烟尘（2026-11-03）

- WeaponSmokePool是纯视觉共享粒子池；Gun/Railgun在真实开火地点留下2.6s淡烟，移动连射形成世界空间烟带；Missile在0.12s原点火延迟后沿真实弹道留下2.4s细烟。激光／光束不冒烟。
- Resources/Combat/WeaponSmoke.mat与Art/Shaders/WeaponSmoke.shader控制柔边／深度软化；WeaponDefinition.smokeTrail明确开启9个实体武器资产。原有体素发光尾焰不变。
- 两个ParticleSystem容量1536/640，每帧新粒总预算160，4500m外拒绝发射；不增加灯光、粒子碰撞或战斗随机数消耗。高负载到顶时只限制新烟。
- 审查脚本、原生截图、参数和关闭工具见Documentation/WeaponSmoke-2026-11-03/README.md。本轮不改战斗参数，Play验证结束后已停止。

## 最新双方弹道烟增强（2026-11-03）

- 本条取代前文克制烟默认值：实弹沿真实扫掠路径新增明显淡白烟线，4.5秒；枪口短烟带6.5秒；导弹加宽细烟带6秒。保持烟龄扩散与世界空间驻留。
- WeaponSmokePool有6个固定PS：双方各自的导弹／实弹路径／枪口。总容量8192/8192/4096，每侧帧预算64/96/32，总帧384；双方与类型之间不能互相抢空预算。
- 当前审查用tools/WeaponSmokeDense20261103/Audit.cs；旧SmokeReview的寿命和池结构断言只适用于上一版本。原生双方交火截图与参数见Documentation/WeaponSmokeDense-2026-11-03/README.md。

- 最新残骸策略覆盖历史“永久保留”：DebrisWorld.lifetimeSeconds=90，按Step累计模拟时间，碎裂继承到期时间，不延寿；到期注销碰撞/渲染并递增拓扑版本。地图物件不受这条舰船残骸计时影响。12项专项通过。新手键位PDF在output/pdf（ZH/EN），网站guides同步。
