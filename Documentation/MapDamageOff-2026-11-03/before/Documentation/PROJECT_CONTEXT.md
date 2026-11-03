# Starframe 精简开发交接

2026-11-03 最新入口：先看 STATUS.md 顶部，再看 Documentation/CombatHud-2026-11-03/README.md。当前已有10000cost五波SaturnRing关卡（详见ArcadeMission文档），旧段落中的“关卡尚未制作”已过时。直射武器再次×2，原生模块资产为权威；HUD无背景框（双方姿态除外），UiTheme.combatInstrument独立控制关键仪表色。预算地图且keepCoarseIntactVisuals=true时，>=128格物件在加载中预热受损外壳，只改显示组织，不改格子、稀疏受击血量、断连、质量或碰撞；普通精细模型地图与地图工作室保持原路径。残骸持续存在，渲染依赖TopologyRevision同步缓存，新增破坏/拆分路径必须增加该revision。近期回归证据及参数入口见该文档。

2026-11-02 飞控纠正（覆盖下段真实喷口wrench模型）：用户实测旧模型持续滚转且参考舰不可控，根因是直接叠加偏置力矩和通道耦合。现采用独立稳定的街机六轴模型；ShipPerformance缓存每模块每通道最佳喷口/贡献，旋转±方向统一取较弱侧能力；AllocateWrench保留名字供旧调用，但不是物理wrench求解器，不能重新将forceDirection/leverArm的副作用直接积分到其他轴。平移、俯仰、偏航、滚转只影响自己的通道，同模块共享额定预算，布局/惯量仍决定能力。Controller删除补偿预分配；尾焰/音效沿用实际分配油门。486EditMode+4真实船直飞/转向/受损boost PlayMode+1精细瞄准通过，当前玩家172件Balance-Heavy只读测试。证据与源码before在Documentation/FlightStability-2026-11-02；无参数资产/玩家存档变更。

2026-11-02 最新3500战舰框架（覆盖下段旧3000基线）：八种普通Hull cost=0，装甲/功能件收费；Power2=340、Shield2=400，标准出击预算3500。新的Battleship-Heavy/Maneuver/Assault有545/314/504件、3435/3418/3487cost、四组6–8武器。ShipPerformance按存活占格计算COM/对角惯量和喷口力矩，AllocateWrench对每模块共享额定推力，FixedUpdate复用缓存；加力1.5作用加速/转向但统一限速250m/s。无对应反推/力矩就保留惯性，旧布局不再获得隐藏全向操控。ShipFlightController/ThrusterExhaust/GameAudio共用实际喷口分配，FlightChaseCamera按渲染插值姿态先更新，AimReticleGraphic后更新读RenderForward；持久鼠标虚拟摇杆操作保持。装配原信息框增加模块/机动页，按Assembly变更和tuning/database Revision缓存，显示六轴/刹停/转速/转弯半径，中英本地化已接。新工具与完整真实before/after位于Documentation/Battleship-2026-11-02和tools/Battleship20261102；Starframe/战舰框架菜单可打开、导入及分项恢复数值（不回退算法源码）。本轮27组固定目标模型173–320s、中位197s，不能当真人对局证明。旧Balance目录仅历史记录；正式五波关卡仍下一阶段。最终验证结果见本轮README。

2026-11-02 平衡基线：38组件全部正cost，14武器按定位重定值，普通出击3000cost，靶场不限；原生资产仍是权威数据，不用C#旧默认覆盖。Core4200HP、Cube462HP、共享耐久尺度4.2已写入资产，脱战10s后修复；Power100W/1400J、Power2=650W/10000J；Shield2730/12600、回充90/360HP/s、耗能180/720，延迟4.5/5s；普通推进免费、Shift140/s×1.5。已修即时首发双重冷却/切组绕过、无锁导弹空耗电，AI会按真实来袭反应使用flare。装配显示实时cost，试飞缺电警告可继续或回装配。参考船Balance-Standard/Maneuver/Heavy已导入蓝图库，2984/2891/2928cost、129/130/145件，用户当前工作船不变。27局固定目标瞄准噪声/开火节奏矩阵首次核心击毁157–197s、中位174s，仅模型并非真人验收。474EditMode+11AppFlowPlayMode通过，中英UI实景检查。菜单Starframe/平衡参数可开资产及分项恢复；before.json因初次嵌套导出失败从历史档案/原生读数重建，详见Balance-2026-11-02/README.md。后续15min/5波关卡尚未实施；避免运行旧部件安装器抹回cost0与旧数值。

2026-11-02 接缝与质量修复：`RangeAsteroidBackdrop` 已改4个共享体素外表网格，4645个装饰实例，中央延长带在原地图±1754m端点外按半径留35m空隙，局部加密过渡；近80–350m隐藏，LOD7km，预算仍4800总/3000可见/180批。Shader用URP `UniversalFragmentPBR`，中性.93/光滑度.6，跟地图一致的环境光、方向补光与Lit雾，勿再用固定ambient手调模拟。`ShipSceneryCollision`补足kinematic船原生接触的有限质量冲量限制，普通/CCD/复合碰撞与Sweep共享每步预算，纯值线程安全缓存；地形存活格体积×density×缩放体积原质量算法不变，不要把大块冻结。4接触/12地图/3Sweep测试通过，实景清Console。用户已明确后续忽略CGDD插件记录导出要求。旧版远景数值以下保留为历史，以本段为准。

2026-11-02 远景更新：景深已按用户要求关闭（RangeAtmospherePostFX.modeOff + Runtime.Initialize覆盖Off），不要再打开Gaussian；UI磨砂保留。`Flight/RangeAsteroidBackdrop.cs` 用4个共享低面数网格/空间实例批次生成4285块、50km长的中间延伸带和左右分离带；仅视觉，没有HP/碰撞/每块GameObject，不改原可破坏地图。worldCamera far28000/near.15，17–25km淡出，Lod8.5km，组件backdropEnabled独立开关。`SaturnRangeSky.mat` 世界半径20,000,000m、中心(-20,300,000,-400,000,800,000)，约80度半径、最近表面320km，用归一化解析求交无大球网格。原生安装/回退/测量/截图见 `RangeBackdrop-2026-11-02/README.md`；当前Editor检查通过，未新打Player。

2026-11-02 靶场氛围：`RangeEnvironment` 600米墙距/12米细格/.42透明度，CPU跳过淡出距离外的网格。`RangeAtmosphere` 与 `RangeAtmosphereTuning.asset` 管有上限的世界尘埃、薄雾和随恒星/带区高度变化的暖冷背景光色；`RangeAtmospherePostFX.asset` 是独立的四分之一Bloom/远景Gaussian配置，不覆盖装配共用profile。PC上限160/12，单PC画质过滤映射已修复，避免循环重建。原生满额Editor对比CPU+0.439ms/GPU+0.073ms，仅该机采样；调参、分项回退及证据见 `RangeAtmosphere-2026-11-02/README.md`。新场景可复用该组件，绑定主恒星、带区anchor、Volume并在玩家/摄像机就绪后Initialize。

2026-11-02 战斗扩展：组件38类，末尾追加 `BlockShape.FlareLauncher=37`（不重排旧ID），1格/薄板模型，武器分类但不参与普通武器组。`FlareDefinition` 资产控制12枚/10秒CD/45%诱偏；R每次轮流触发一台，F仍锁定。`WeaponClearance` 双向检查旋转后 +Y 整列净空。`TargetSensorWorld` 缓存存活舰体尺寸，双方发现距离为 Clamp(3000×sqrt(radius/10),1000,6000)，出界迟滞1.1倍/.5秒；F瞄准红标，离屏保持锁定。`TargetSilhouetteOutline` 用缓存实例掩码显示穿透场景的红色外轮廓。导弹 `MissileGuidance` 有转向加速度/10秒跟踪/视锥失锁，失败后不重新寻回原舰；每枚导弹对每次干扰弹齐射只掷一次概率。`ThreatAudioDriver` 只区分发现、敌方真实锁定、已来袭三类警报；声音资源和天空材质独立。场景/贴图/模型均经原生API导入，参数与工具见 `CombatUpdate20261102/README.md`，459项EditMode通过。

2026-11-02 临时靶场接入：`Scenes/Testing/FlightRange.unity` 内 `Temporary Map Sandbox` 引用3500米地图，根物体 Y=-1100，地景 Y≈-1520..-1102，出生点 `(0,-600,-1100)` 朝下25度。活动时靶场半径2000米；禁用这个根物体后，Bootstrap 自动使用原来的出生点和边界，原 FlightTuning 资产未修改。此靶场开启 `keepCoarseIntactVisuals`，使用3500碰撞预算/120米精碰撞距离，取消飞近时同步生成精模；用户已确认飞行卡顿解决，受损重建仍可能停顿。命中反馈新增 `ProjectileWorld.HitFeedbackConfirmed(owner, scenery)`，地图弱白、舰船/护盾红色，原 `HitConfirmed` 兼容事件保留。

加载入口统一为 `RangeLoadingOverlay.LoadScene`；`FlightRangeBootstrap` 协程等待 `PrepareMap` 后才建立玩家/HUD/AI，最后 `Complete` 关闭遮罩、恢复输入。读条依据异步场景读取和真实物件构建数，支持中英文；地图构建失败显示返回装配按钮，不无限等待。预算地图冷载只创建包围盒，精确碰撞 Bounds 缓存至近处才实例化，`ColliderCount` 是逻辑精碰撞盒数，`MaterializedColliderCount` 是已创建精碰撞组件数。14项延迟碰撞专项通过，实际靶场2854组件而非247723；一次缓存就绪的 Editor 再次进入13.952秒，非首次 Player 指标，输入恢复/生成延迟/进度截图均核对。证据 `RangeSandbox-2026-11-02/`。

更新：2026-11-01。后续优先读本文件和 `STATUS.md`，按任务定位代码；无需重读整段历史、所有测试报告或美术迭代记录。新3500米参考地图的实物、入口与核查见 `ReferenceMap-2026-11-01/README.md`；此前计划保留在 `MapAudit-2026-11-01/`。

## 工作边界

- Unity 6000.3.25f1 / URP 17.3，工程 `D:/software/unity/CGDD26107`。核心内容均在 `Assets/_Starframe/`。
- 当前地图范围：已从原始 Blender 场景重建独立3500米参考布局、体块库和沙盒场景。首次图形 Player 实测加载46.27秒、平均约1.7秒/帧，不合格；已按用户授权加入视觉优先的碰撞预算，优化 Player 探针实测加载25.936秒、概览14.608ms/帧、近站9.223ms/帧。此为1440×900批处理显式渲染探针，GPU时间不可用，不能宣称普通玩家FPS或全部物理/美术验收完成。37件组件扩展及地形优化保留；历史AI移动目标失败保留，不调整AI或飞控。法向正常，不重做外表面法向。
- 地图编辑长按与性能收尾已完成，详见 `MapEditing-2026-10-24/README.md`。单击立即操作，长按等待 .65s、间隔 .18s，可在笔刷面板调节。
- 美术基准：硬边倒角体素、塑料高光与接缝暗部，白/奶白/淡灰低对比；发光组件和体素粒子。UI 平淡磨砂玻璃，无蓝色外框，无大面积上下渐变。不要无需求重做这些决定。
- 使用本地 Unity API/CLI操作场景、材质、Prefab、导入设置及其他序列化资产；源码可正常编辑。不要手改 YAML 或 .meta，不覆盖用户蓝图/地图。
- 工程现有顶层Git（最后历史提交2026-10-22），也有site仓库。大量后续合法扩展尚未提交；不要用全量回滚抹掉它们，不自动提交/发布。若所有者校验阻止只读Git，可用命令级 `-c safe.directory=D:/software/unity/CGDD26107`，不改全局配置。

## 玩家流程与入口

- 入口 `Scenes/Frontend/Hangar.unity`：展示当前舰船 → 装配/武器组/设置/靶场/关卡出击确认。
- `ShipEditor` 为装配页面，`FlightRange` 为开放式性能/战斗靶场。场景名常量在 `Flight/FlightSession.cs`。
- `Scripts/Runtime/Construction/App/` 管理首页、设置、中英文、工作区、武器组、出击与结果页。`GameAppBootstrap` 为首页入口。
- 两个关卡有选择、预算与舰船再次确认、返回修改和结果接口；实际地图、波次和自动胜负尚未接入。不要把 API smoke 验证当完成关卡。
- 后续 `MissionDefinition.sceneName` 配置已加入构建的场景；从 `MissionSession.Current` 获取深拷贝快照。关卡自行判胜负，再调用 `MissionResults.FinishAndReturn`。

## 数据与持久化

- `BlueprintData` v2 保存每个部件稳定 id、shape、position、rotation、核心/正方向和武器组。旧字段用于迁移，不代替完整姿态。
- `ShipWorkspace` 独立保存工作中的船；具名蓝图由 `BlueprintRepository` 管理。目录均在 `Application.persistentDataPath`：`Blueprints/`、`Workspace/current-ship.json`、`Settings/preferences.json`。战斗和预览不回写存档。
- `Settings/Modules/ModuleDatabase.asset` 索引 `ModuleDefinition`，是部件模型、材质、图标、质量/血量、推力、占格、电力、护盾及武器引用的权威数据源。尊重现有资产值，不拿 C# 默认值覆盖平衡。
- `Settings/Flight/FlightTuning.asset` 调飞控、镜头、推进特效；`BattleTuning.asset` 调恢复、锁定、重生、AI和战斗特效；武器参数在 `Settings/Modules/LaserRapidCannon.asset`。
- `Resources/UI/WorkbenchTheme.asset` 管视觉及分项回退开关；中英文文案 `Localization/GameUI.csv`。原框架接入说明：`ArcadeFramework/使用与接入.md`。

## 船体装配和扩展部件

- `Construction/ShipAssembly` 管部件视图、放置/删除；`ShipEditorController` 管输入，`ShipEditorUI` 管装配 UI；`BlockPlacement` 管格点姿态、附着面和绕该面的 90° 旋转。
- `BlockModules` 管占用/喷口保留格，`BlockConnectivity` 管面连接，`Combat/WeaponClearance` 管枪口方向的物理净空。不可用弹丸伤害直径当放置净空；放新块也要反查旧武器通道。
- 占格永远是完整整数单元；视觉可略小或略伸出。Laser 占 3 格；Thruster1/2 模型占 1/2 格，后方另保留 2/3 格，连接只算本体。斜面不能作为任意贴合底面，边/点接触不算连接。
- `BlockShape` 序号保持兼容：Cube0、Wedge1、Thruster1=2、Thruster2=3、Slab4、Pyramid5、Core6、Laser7、Power8、Shield9。新增类型追加，不重排旧值。
- 添加部件：新增/配置 ModuleDefinition 并加入数据库；准备导入模型/材质/两态图标；核对本地占格、底面、旋转后占格、连接、枪口/喷口方向；搜索 shape 分支补齐图标/分类/兜底几何等，再验证旧蓝图兼容。
- 武器还需 WeaponDefinition：射速/伤害/速度/弹径/穿深/二层倍率/射程/耗电/特效/触发方式。现有 Projectile、GuidedMissile、Beam 执行器；扩展攻击形式需要实际执行逻辑。干扰弹走独立 countermeasure 定义与控制器，不伪装为普通枪。

## 飞行、战斗、残骸

- `Flight/FlightShipRuntime` 管部件存活、拓扑、恢复、核心死亡及性能重算；`ShipPerformance` 汇总质量、各轴推进、转向等。变动后按修订/脏标记更新，避免每帧全船重算。
- `ShipFlightController` 管第三人称飞控；`FlightChaseCamera` 管跟随。W/S前后、A/D左右、Space/Ctrl上下、Q/E滚转，鼠标相对准星的偏移是持续转向输入；松开方向自动制动，总速度有限制；Shift加力消耗能量。
- `Combat/RangeCombatWorld` 组织靶场战斗，`ShipWeapons` 使用分组输入/冷却/能量生成弹丸；`ProjectileWorld` 与 `CylindricalHitQuery` 做连续弹道/穿透。特效由 LaserVisualPool / CombatVfxPool / ThrusterExhaust 管，特效长尾不可伸入枪口后方。
- `TargetLockSystem` 对已发现红标按虚拟光标选择；锁定范围来自目标存活尺寸，视野不决定保锁，离屏红箭头指向目标。`CoreVelocityEstimator` 对核心物理位置取样，`InterceptSolver` 预测与弹丸一致的提前量。不同启用弹速分别显示提前点。
- 仅 hp 归零部件直接消失；断连幸存部分转交 `DebrisWorld/DebrisCluster`，保留剩余血量/碰撞并漂浮。核心死亡时其余幸存块也必须先转为残骸，再处理死亡/重生。不要恢复“断连全部删掉”的旧逻辑。
- `ShipPowerBus` 是统一电力通道；`ShipShieldSystem` 管护盾容量、受击延迟、恢复和耗能。实际数值来自 ModuleDefinition。船体恢复与地图格子伤害重置是两个不同系统。
- `WeaponGroups/` 按部件 id 分配组和开火绑定；进入战斗全部启用，数字 1–8 切换启用，再按绑定开火。禁用组不射击、不耗能、不参与提前点。未来特殊武器保留 Hold/Press/ChargeRelease/Toggle 通道。
- HUD 在 RangeHUD、ShipStatusGraphic、SphereRadarGraphic、TargetAimGraphic、Presentation/ReticleHitFeedback；UI 不应再承担物理/伤害计算。
- 船体大规模渲染已有 `ShipInstanceRenderer`、池化及缓存，勿直接改成每块各自 Update/Instantiate 的设计。

## 地图开发工具与漂浮资产

- 菜单 `Starframe/开发工具/体块地图工作室`。独立开发工具，不挂进游戏首页。`Scripts/Editor/Maps/SpaceMapStudioWindow` 的 partial 分别管理 UITK界面、视窗、编辑操作和示例资源。
- `Maps/Assets/*.asset` 是可复用 VoxelAssetDefinition；`Maps/Layouts/*.asset` 是 SpaceMapDefinition，记录实例姿态/缩放/漂移/出生点。场景/Prefab 用 Unity原生 API 导出。`Maps/Scenes/StudioPhysicsPreview.unity` 是独立沙盒。
- 新参考地图：`Maps/ReferenceMiningStation/Layouts/ReferenceMiningStation3500.asset` 是独立布局，`Maps/ReferenceMiningStation/Scenes/ReferenceMiningStation3500.unity` 是可打开、进入 Play 的独立沙盒；布局或资产 `.asset` 在 Project 视图双击可直接进体块地图工作室。新库有 **2345 个可编辑资产**、**2854 个布局实例**、**3123469 个实例加权占格记录**，另有7个未摆放的源石块原型供库内复用。`Maps/Layouts/MiningStationAsteroidField.asset` 及 MiningStationRound3 的旧 v3 草案仍保留，不能与新图混为同一验收对象。源 `assetsForCodex/scenemodel` 内的 `cggd.blend` 与四个 FBX 未改动，哈希见 `ReferenceMap-2026-11-01/source-qa-original-hashes.json`。
- 新图以 Blender 对象世界矩阵标定最长轴3500米，站体、封闭陨石、平面卡片替换物和两组残骸分别按源位置摆放。细设施/桁架用固定1米网格，大实体按物件固定2/4/5米网格；全库 **2102 件实体填充**、**243 件按原设计空心机械壳**。Cube、Wedge、Slab、Pyramid、Block5/6/7/8 八种形状均真实使用，所有2345件原生资产初始六邻接连通。总账与源覆盖见 `ReferenceMap-2026-11-01/README.md`。
- 既有地图资产使用 Cube/Wedge/Slab/Pyramid；新参考地图还实际使用 Block5/6/7/8。所有形状复用船体数据库的精确模型和材质。资产 baseColor 与 surfaceColors 只生成地图侧着色副本，不改源材质。形状、旋转、颜色随保存和断裂保留。
- `Runtime/Construction/Maps/VoxelStructureRuntime`：完整资产是一个无重力动态刚体，合并视觉网格+合并复合 BoxCollider。所有基础形状碰撞/伤害/地图连通性按完整一格。受损切断后，按面连接分成独立动态刚体，不按世界区块组织。
- 地图只稀疏记录最近受击格的临时血量；默认停止受击2秒后直接忘记损伤视作满血，不逐格回血。只有被摧毁格永久缺失。`VoxelBodyHitbox` 与现有弹道相接，`ShipCollisionSweep` 可推地图刚体。
- `VoxelTemplateCache` 显式引用计数，最后使用者才释放；预览关闭先 DisposeResources 再 Cleanup。模型有 .98 等实际接缝，不可仅因邻格占用就删内部面；VoxelHullAppearance 按真实覆盖判断，保留接缝内表面。法向已确认正常。
- 大预览（有数据库且≥512格）使用数据态 runtime + 编辑器 `VoxelPreviewInstances` 原生 CombineMeshes；重复完整资产共享网格，破坏副本独立。静止相机复用预览纹理。没有每格碰撞组件。运行时物理路径不变。
- 新地图资产：工作室现支持八种基础形状 Cube/Wedge/Slab/Pyramid/Block5/6/7/8。Block7占1×1×2、Block8占2×2×2，以modelOffset分片保留每个物理格，不能按一格存。颜色槽只改地图外观，不给地图加船体组件。运行时保留稀疏临时受击HP、共享模板和单资产内部4³格损伤更新；那不是固定世界区块。完整资产一个无重力刚体，断裂后物理碎块均保留碰撞/可破坏性。
- 参考沙盒采用分帧构建与懒加载精细视觉；站体按源几何接触导入125个装配关系（112个 FixedJoint、13个仅临时碰撞过滤）。布局原生核对通过全部2854个实例姿态、锚点、八种形状和占格数；这些是序列化/Editor核查，Player 探针另见 `ReferenceMap-2026-11-01/player-optimized.json`。`world-native.json`、`assembly-native.json`、`tests/` 记录原生与测试结果。
- **已授权的碰撞/视觉预算取舍**：新沙盒 `SpaceMapRuntime.useBudgetedPhysics=true`；远处每个活物件启用一个跟随刚体移动的外包围盒代理，相机距物件包围盒300米内才按最近物件逐件切换到精细碰撞盒（切换时关闭代理），所有已启用地图碰撞器预算8000。每个活物件在预算中至少占一格，不能用总生成盒数当当前启用数。`BudgetedScenery` 专层关闭地图物件之间的自碰撞，仍可与船和弹丸交互；远处船体撞击只得到包围盒近似，弹丸沿格遍历仍查真实存活占格，不把空心包围盒当命中。`maximumFineVisualCells=8000` 让超大未受损物件保留烘焙外表 Cube 视觉，避免即时生成复杂细面；实例继续无重力动态漂浮、断裂与稀疏伤害。调参字段和 `@tune` 在 `Scripts/Runtime/Construction/Maps/SpaceMapRuntime.cs`，该沙盒的值在 `Maps/ReferenceMiningStation/Scenes/ReferenceMiningStation3500.unity` 里的 `SpaceMapRuntime` 组件。近站首次精细视觉生成仍可能出现卡顿，外表边界拟合不等于最终美术；全面 Play 破坏/碰撞和视觉验收仍需继续。
- 新图有独立沙盒加载入口；正式关卡/游戏首页接入、敌方波次与胜负仍需后续实现，不能宣称现有地图已进入靶场或关卡。

## 测试与证据的最小流程

1. 先查脚本/资产关联，使用临时内存蓝图/地图或独立沙盒，不破坏用户存档。
2. Unity CLI：`C:/Users/34808/AppData/Local/Unity/bin/unity.exe`，先检查最新版本；驱动 Editor 的调用带 `--caller plugin --skill unity-cli`，工作目录/`--project-path` 指向本工程。现在 beta14；不要因旧命令提示随意升级项目包。项目旧 Pipeline 原生命令为 `eval`、`run_script`；`script eval` 会自动回退成功但留下无该命令的 Console 记录，优先用原生命令。
3. 定位相关 EditMode 测试（`Assets/_Starframe/Tests/Construction`）或 PlayMode（`Tests/PlayMode`）。只跑任务需要的过滤范围，不默认跑全套。进 Play 不是完成验证；检查帧推进、实际画面与 Console。
4. 当前地图收尾：`Documentation/MapEditing-2026-10-24/Checks.json` 全通过，实际预览 NativePreview.png 正常。相同数据编辑重建：4096格约362→51ms，10000格约757→124ms；这是同步 CPU 重建，不是FPS保证，极大资产仍可能停顿。
5. 新参考地图核查：优化前完整 EditMode **392/392**、新增优化碰撞专项 **11/11**，均零跳过；原生世界核对通过全部2854实例及2345资产。首次图形 Player 探针46.27秒加载、约1.7秒/帧，不合格。优化探针在 i7-13700K/RTX 5080、1440×900批处理显式相机渲染下：120帧概览均值14.608ms、p95 20.751ms；120帧近站均值9.223ms、p95 13.051ms、最大182.174ms；加载25.936秒。初始启用碰撞器2854、近站8000，推动物件移动、临时伤害过期后破坏孔不回填、幸存格守恒均通过。GPU计时未返回，普通玩家FPS、首次近景卡顿及大范围破坏仍要另验。报告：`ReferenceMap-2026-11-01/player-optimized.json`、`tests/`。
6. 历史基线 EditMode250/250、PlayMode28/29。有已复现的 AI 移动目标伤害测试失败，未完成归因；失败断言已补诊断，证据留在 MapHull-2026-10-24/HANDOFF.md。用户已叫停，不能当绿灯也不要擅自继续。
7. 按项目 AGENTS.md：游戏脚本 @role/@calls/@tune 与实际一致；游戏变动追加 DEVLOG Interaction，不篡改历史，不捏造 Stage Reflection。改变挑战前询问胜负/玩家动词；纯开发工具输入/性能不属于挑战改动。
8. 运行 `CGDD/Export Trace Docs`（可用 `TraceDocsExporter.ExportForAutomation()`）刷新 docs/与site/data/。不顺带改网站或发布。

其余历史材料按需读：地图基础 `MapStudio-2026-10-24/README.md`，模型/接缝 `MapHull-2026-10-24/`，既有船体性能 `Performance-2026-10-22/`。不需要重新搜索全部历史来添加单个组件。

## 2026-10-25 组件扩充补充

- 组件库已扩充到36（新增26）；enum 0..9保持原值，10..35依次追加新Hull/Thruster/Power/Shield/Armor/Gun/Laser/Railgun/Missile/Sword。不要重排序号。
- Installer：`Scripts/Editor/ComponentLibraryInstaller.cs`，可重复生成扩展资产，原assetsForCodex与旧十种资产保留。
- 武器的WeaponDelivery为Projectile/GuidedMissile/Beam；ShipWeapons处理同目标2秒锁定、耗电装填和多炮口轮射。SpecialWeaponVisualPool处理真实导弹与蓝色40m光剑。光剑的三角实体常驻。
- 实弹和电磁minimumVisualPixels=2.8，LaserVisualPool只加粗显示，不改实体弹径/伤害。可用visualWidth、visualLength、tailLength调整显示。
- BlockModules.FacesContact基于真实平面片重叠；武器只底面且不能互接，Armor只底面。MountingCell/AlignMount将长装甲后端实际底脚对准点击面，不能把origin空面当底脚。
- 地图现有8种基础Hull；Block7/8每格存modelOffset，切片只封内部切口，不改外侧原模型法线。
- 资产与测试证据：`ComponentLibrary-2026-10-25/HANDOFF.md`。最新285/285 EditMode、19/19相关PlayMode通过；历史AI移动目标失败未重跑。

## 2026-10-25 Thruster6 与武器收尾补充

- 组件库现为 **37** 个；`BlockShape.Thruster6=36` 追加在末尾，不重排旧值。Thruster6 单格、400 kg、80 HP、底面安装，五个喷口（+Y/±X/±Z）各 45 kN；尾焰**可视**长度为一格（1 m）；放置采用**半格判定**（绿色预览 0.5 m，相邻格不再硬占位，方块可贴近）；运行时喷口遮挡仍按完整相邻格检查，贴近方块会关闭该方向喷口；堵住一个只关该轴，共享转向额定值只计一次。
- 最终武器节奏：Laser2/3 10 发/秒；Gun1/2/3 16/20/24 发/秒、1800/2000/2200 m/s；Railgun1/2/3 **3/2.5/2 发/秒**、10/12/14 km/s。电磁炮用 `TracerStyle.Continuous`，金色光束 + 白核，尾长 200/300/420 m，0.09 s 残留，visualWidth 8/9/10，minimumVisualPixels 4.5/5/5.5；物理弹径与伤害未改。
- `ComponentLibraryInstaller.MakeWeapon` 的默认值已与最终资产一致；`Starframe/Components/Apply follow-up weapon tuning` 更新射速/弹速/电磁炮和机枪视觉，不重置枪口、材质或导弹网格。Thruster6 用 `Starframe/Components/Install five-way Thruster6` 安装。
- 面板通过 `ShipEditorUI.ShapeOrder/IconSize/CategoryFor` 自动纳入 Thruster6（Propulsion，Drive VI），本地化键为 `module.Thruster6`、`module.stat.multithrust`、`module.mount.thruster6`。
- **安装判定收尾**：`Power`/`Power2` 在 `BlockModules.Patches` 中走 `FullFace`，六个方向都按完整常规方块接触，顶面可以正常放置方块。`BlockConnectivity.HasCompleteMount` 检查武器/装甲底部每一宽度列的支撑：宽 2 格必须左右两列都有支撑，装甲只看 `MountingCell` 的真实脚面那一排；1 格宽和其他功能性方块保持原规则。武器连接改用 `MountTouches`（检查真实支撑顶面），装甲保留实际模型脚面。
- **电磁炮触发**：`WeaponDefinition.firstShotImmediate` 为真时，按住开火的第一发立即发射，随后按 `shotsPerSecond` 冷却；Railgun1/2/3 资产已设为真，`ComponentLibraryInstaller.ApplyRailPulseVisual` 也会在重建时设置。物理伤害、穿透和弹径不变。
- **连射观感收尾**：Gun1/2/3 可视头长 4/5/6 m、可视宽度 2.2/2.4/2.6、minimumVisualPixels 2.2，拖尾仍 100 m；机枪/电磁炮命中闪光、白核、碎片数量与持续时间放大。`WeaponVisualTests` 用真实 Gun1/2/3 与 `LaserVisualPool` 验证 2/4/6 炮口的左右生成点、可视体数量和拖尾头部；`ProjectileWorld.OwnPathBlocked` 忽略相邻武器，避免密集多炮互相阻塞。
- 验收：EditMode **308/308**；电磁炮专项 **3/3**；多炮口可视体专项 **3/3**；面板 PlayMode 与靶场固定射击各 **1/1**（面板新增半格绿色预览断言）。面板用例多次都通过断言，但批处理进程在结果写出后均以 0xC0000005 退出（可复现）；靶场 PlayMode 正常退出。以 XML 结果为准，不把退出码当断言失败。证据与已知限制：`Thruster6-Weapons-2026-10-25/README.md`。


## UI iteration / 2026-11-02
Use the existing uGUI builders and shared MenuSurface/GlassUiPalette/UiStyle; uniform glass, no panel gradients or colored outlines. UiModalScope isolates keyboard focus; new typography/glass/motion fields are real GameSettings preferences. AppPage.BlueprintLibrary is appended as8; never renumber old pages. BlueprintLibraryScreen owns saved designs (preview is non-mutating until explicit load), and AssemblyAppNavigation captures its draft before entering with BlueprintsReturnToEditor + EditorResumePage. Target roster/count/mode configuration belongs only to GameAppScreens.ShowRangeSetup; do not add it back to assembly or the blueprint library. Home displays one mission card; actual level remains a placeholder. New component entries use database-driven catalogues as before. Validation:486 EditMode +43 PlayMode on final code; fixtures must await staged map loading, serialize locale changes and isolate physical scenes/storage. Source baselines and guarded grouped rollback: Documentation/UIIteration-2026-11-02.
