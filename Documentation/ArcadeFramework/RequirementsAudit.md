# 本轮需求与设计文档核对

核对日期：2026-10-18。历史依据为完整阅读的 `DesignReference.txt`（原设计 PDF 文本，2026-09-25，20 页）。**本轮用户要求及此前已确认的项目行为优先于旧 PDF。** 最新回归：212 项 EditMode、21 项 PlayMode 全部通过；Windows 独立版本的界面流程验证见 `PlayerValidation.json`。实际关卡内容保留接入点。

## 当前范围与验证入口

| 当前要求 / 对应 PDF | 负责模块 | 验证入口与状态 |
|---|---|---|
| 首页上部展示当前/保存船并进入编辑，下部两个关卡入口（§2–3） | `GameAppBootstrap`、`HangarShipPreview`、`ShipWorkspace` | 已通过 Home/Editor 往返、当前蓝图保留与中英文预览 |
| 普通游戏设置、中英文切换、英文文案完善 | 设置界面、`L10n`、`LocalizedUiText`、`LocalizedUiFont` | 设置分类、语言与字体切换、暂存/应用/恢复及独立版本显示确认已验证 |
| 选关/难度后重新确认蓝图、成本、核心与结构；可返回编辑（§3、5） | `MissionDefinition`、`MissionCatalog`、`DeploymentValidator`、`MissionSession` | 领域与真实界面往返均通过；快照隔离；真实关卡内容由后续设计接入 |
| 每件武器按稳定部件 ID 分组；组绑定键盘或鼠标；可单独或同时开火 | `WeaponGroups`、`ShipWeapons`、`WeaponGroupInputRouter`、`WeaponGroupScreen` | 默认启用全部；1–8 切换；停火/耗电/蓄力/预瞄过滤与真实键盘/HUD 同步已验证 |
| 为未来武器预留 Hold / Press / ChargeRelease / Toggle、需要锁定才可开火的配置（§6.2 的扩展） | `WeaponDefinition`、`WeaponActivationState`、`ShipWeapons` | `WeaponActivationTests`、`WeaponGroupRuntimeTests`；只做通用配置/执行接口，不创作新武器内容 |
| 靶场可选静止、不射击飞行、静止射击、完整对手；保留蓝图名单与各自数量 | `RangePracticeSettings`、`RangeTargetRoster`、`RangeCombatWorld` | `RangeTargetRosterTests`、`RangePracticeModeTests`；模式与蓝图名单分别持久化，不改蓝图文件 |
| 对手高速机动、有效提前量攻击、遵守残船性能/电力/地图边界（§10–13） | `RangeBotPilot`、`RangeBotWeaponInput`；共用飞控、武器、供电与碰撞 | 单元及真实靶场实战已通过；多船测量见 `Temp/ArcadeFrameworkScaleReport.json`，进一步算法优化按用户要求暂缓 |
| 三维、多格部件、唯一核心、面连接、设计与战斗数据隔离（§5、7、12） | `BlueprintRepository`、`ShipAssembly`、`BlockConnectivity`、`FlightShipRuntime` | `BlueprintRepositoryTests`、`GridConnectivityTests`、`CombatMechanicsTests`、`RangeLifecycleTests` |
| 部件血量、脱战回血、邻接支持重建、质量/功能随存在状态变化（§7、9） | `FlightShipRuntime`、`ShipPerformance` | `CombatMechanicsTests`；1000 件桥断连/重建已有机制用例，实场帧耗另测 |
| 先合并有效需求再同比供电；容量收缩截断，重建电池不免费补电（§8） | `ShipPowerBus`、`RangeCombatWorld.Execute`、共享武器/护盾系统 | `CombatMechanicsTests`、`PowerBoostTests`、`ShieldMechanicsTests` |
| 六向移动、转向/翻滚、当前最高速度 250；不重新平衡推进/质量（§6、9） | `ShipFlightController`、`FlightTuning`、`ShipPerformance` | `PowerBoostTests`、`FlightAimStabilityTests`；保留当前普通飞行与耗电加力的既有区别 |
| 锁定身份与实际命中分开；核心物理速度提前量；射程/自身遮挡/实际弹道（§6.2） | `TargetLockSystem`、`CoreVelocityEstimator`、`InterceptSolver`、`ShipWeapons`、`ProjectileWorld` | `CoreVelocityEstimatorTests`、`CoreLeadBallisticsTests`、`AimClearanceTests`、`ProjectileMechanicsTests`、`MultiTargetRangeTests` |
| 后方视角、部件状态、雷达/锁定、护盾与电量、命中反馈；非整船总血条（§2、6、10） | `FlightChaseCamera`、`RangeHUD`、`ShipStatusGraphic`、`SphereRadarGraphic`、`TargetAimGraphic` | 机制测试与 1280×720 独立版本、2560×1440 编辑器界面预览已核对 |
| 物理阻挡但无撞击伤害；敌我同边界约束（§6.3） | 共享飞控扫掠碰撞与边界制动、AI 预判转入界内 | `RangeBotPilotTests`；实场角落/对冲/残骸避让待验证；不按旧文档额外添加定时传送 |
| 1000 部件和多船积压可测，不新增隐藏数量上限（§13–14） | 下述测试矩阵；`FrameworkScaleProbe` | P1–P3 实测已完成（1000/3500/11000 部件，0/20/80 靶机），未删健康残骸或加隐性上限；80 靶机压力下有开销峰值，后续性能优化暂缓 |

## 已确认的覆盖关系

| 旧 PDF 表述 | 当前应保留的规则 / 证据 |
|---|---|
| §7、15 不保留独立物理断块 | **覆盖**：所有断连且仍有 HP 的部件保留为可碰撞、可被射击毁坏、可漂移的残骸；核心死亡时其余幸存部件也转移；只有 HP 到 0 才消失。不得按时间、距离、重生或优化理由清除健康残骸。`DebrisWorld` / `DebrisCluster`；`DebrisMechanicsTests`、`RangeLifecycleTests`。 |
| §6.1 鼠标目标朝向稳定后停止转向 | **覆盖**：保持耦合虚拟摇杆，鼠标相对准星偏移是持续转向输入，舰体旋转不消耗或自动居中该偏移。`ShipFlightController`；`FlightAimStabilityTests`、`PowerBoostTests`。 |
| §8.3 护盾具体参数待定 | **已具体化**：组件 300 容量、8 半径，受击后 2 秒延迟，以 50/s 回复，满速回复需求 100/s；普通单格实体碰撞不随小模型缩小；护盾表面低透明度，重叠时衰减，不产生妨碍船体/己方出射的物理球壳。`ShipShieldSystem`、`ShieldSurfaceVisibility`、`BlockGeometry`、生产配置；`ShieldMechanicsTests`、`VisualPowerAssetTests`、`AssemblyPreviewUiTests`。 |
| §6、8 普通移动/制动统一随缺电变弱 | **保留现有已测试行为**：普通飞行/制动与耗电加力分离；加力、武器、护盾回复参加共享分配，不能在本轮借旧文档重新改动力规则。`PowerBoostTests.ZeroEnergyKeepsBaseFlightAndBrakingWhileBoostScalesOnlyWithItsAllocation`。 |
| §4、14 两关完整波次、胜负结算及约 15 分钟内容 | **当前范围排除**：本轮完成首页入口、关卡/难度选择、出击确认及后续关卡接入点；实际关卡、波次、敌船内容和结算玩法实现可留空接口，不据旧 PDF 额外创作。 |

## 本轮明确不新增

实际关卡场景/布局、波次与敌船内容设计、新配件或新武器资产、配件平衡和预算数值再平衡；也不新增掉落、局内成长、经济、联网、排行榜、主动修复、导弹或复杂武器拼装。五小时是本轮质量与测试预算，不是未经测量的 FPS、硬件承诺或必须增加玩法的依据。

旧 PDF 中完整炮塔追踪、喷口偏转档位等尚未由当前内容配置实现的细节，不因本轮具备通用武器/机动接口就宣称已经交付，也不据此扩大本轮组件内容范围。

## AI 二次检查记录

- 无反向/侧向推进的轴给零输入，继续使用共享飞控的真实制动；不向缺失引擎请求虚构加速度。转向请求反解当前虚拟摇杆响应，并使用实测角速度阻尼。
- 射击使用实际炮口与核心位置、核心速度；只有可解的射程内提前量、真实炮管对准且友船/残骸未挡住射线时才请求触发；自身遮挡仍由共享武器检查最终否决。
- 近距离脱离、重新占位、攻击与纯飞行均只发普通操作输入；加力仍受供电分配和 250 速度上限约束。边界以制动距离提前转入，生成/重生/明确重置之外不传送。静止模式从静止生成时保持位置；从飞行切换时先用共享制动减速，不凭空清零速度。
- 新武器触发接口加入后，修复 AI 缺失有效目标标记、Toggle 切换模式仍锁存、Press 不重触发、ChargeRelease 不松开的兼容问题；失去安全射击条件即取消待发动作。混合同一输入组的不同触发类型仍遵循同一组输入语义，不自动改变用户分组。
- 近角落或残骸堵住编队位置后，生成额外搜索地图内空位；未通过删残骸或减少目标数腾出位置。场地确实无法容纳指定体积/数量的极端情况仍需内容侧合理配置。
- `RangeLifecycleTests` 显式保存模式偏好、测试时设 Combat、结束恢复原偏好，避免用户的静止练习选项污染旧生命周期用例。

## 建议主任务运行的性能矩阵（不改保存蓝图）

测试场景用内存副本和独立运行实例；保存原设计 JSON 和现有蓝图文件内容摘要，结束比对。不要调用蓝图保存，不修改组件资产，不写目标名单；模式调用 `SetBotMode(mode, false)`。保留所有健康残骸，不以删残骸或限制敌船数获得结果。

| 用例 | 可复现构造 / 操作 | 要记录或断言 |
|---|---|---|
| P1 单船 1000 件 | 克隆默认 10 件试飞船，另加 990 件方块：先接 `(2,1.5,0)` 的桥，再向 `x=2..11, y=1.5..10.5, z=-1..-10` 填 989 格，稳定唯一 ID；避开原喷口/炮口；先运行蓝图校验。独立实例热身 2s，正常飞行/瞄准/射击 20s。 | 确为 1000 个部件；无重复占格/ID；p50/p95/最大帧耗、物理/渲染/GC；源 JSON/文件摘要完全相同。此布局仅为测试夹具，不是新船内容。 |
| P2 一波规模 | P1 玩家，加 6 艘 300 件与 14 艘 50 件内存夹具（同样保留原操控/炮口通道）；先 FlyOnly 20s，再 Combat 30s；所有实例启用真实飞控。 | 20 目标完整存在；3500 总部件；AI/弹丸/碰撞耗时和分配；实际加速、开火/命中、发电/储能变化；没有超速、NaN 或出界。 |
| P3 四波积压等效规模 | P2 阵容复制四组，用 `SpawnTargets` 生成 24 大 + 56 小目标，保留玩家 1000 件；不需要实现真实波次。 | 80 目标、11000 总部件；热身后持续 30s 的同类指标，尤其 AI、碰撞、弹丸、HUD 与护盾重叠耗时；不暗加同屏上限。 |
| P4 结构/残骸峰值 | P1 击断新增桥，之后按恢复规则重建；在 P2/P3 中分批摧毁 5/20 艘核心，再观察重生、漂移及残骸受击。 | 断连/重建和单帧峰值；所有幸存部件转移且 HP 保留，原船功能失效；重生避开残骸；没有健康残骸按时间消失；生成/销毁分配与内存趋势。 |
| P5 边界/追踪/模式 | 把运行实例置于三面交角附近，保持原姿态/能力；玩家跨向机动，切换四种模式并重置目标。 | 高速制动/转入、对冲与残骸避让；Stationary/FlyOnly 不再发待发或锁存射击；StationaryFire 从静止生成不平移、从运动切换按能力制动；Combat 真正伤害目标；切换/重置不改名单与设计。 |

性能门槛需要结合目标机器与实际分辨率设定；目前只报告测量，不虚构合格 FPS。P1 机制用例不能替代 P2/P3 的真实物理、渲染和积压规模测试。
