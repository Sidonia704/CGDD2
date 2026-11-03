# 岩带接缝与碰撞质量修复

背景岩石采用四个共享体素外表网格；中央带增加地图两端的过渡密度，物体半径加35米安全间距替代旧的650米空档。装饰实例仍无独立物体、碰撞或HP。材质采用URP PBR、.93中性灰、.6光滑度，使用场景主光、方向补光、SH/环境反射及标准Lit雾；不再单独提亮背景阴影。实例共4645，近距离隐藏80–350米，LOD切换7公里，远端17–25公里淡出。

碰撞根因是kinematic飞船的原生旋转/重叠接触绕过了已有的有限质量Sweep。`ShipSceneryCollision`以船质量和接触速度限制冲量，多碰撞体和普通/CCD回调共用每步预算，Sweep已处理的目标不重复施力。该预算是保守近似，并非完整双动态刚体模型；既有飞船旋转阻挡规则未扩展。地图密度和质量公式不变，大块保持dynamic无重力，碎片仍可推、可碰撞、可摧毁。完全毁掉体块后不再向kinematic空刚体赋速度。

验证：`ShipSceneryCollisionTests` 4/4、`VoxelMapTests` 12/12、`ShipCollisionSweepTests` 3/3。实际本地物理场景覆盖大/小质量差、旋转复合接触、Sweep去重和缩放碎片破坏。原生靶场飞行及接缝视角已检查，运行Console为0错误/0警告，shader无编译信息。未重新打包Player。

`performance.json`为2560×1440、RTX5080的固定Editor视角、每模式120帧开关对比：2301实例/73批/344216三角形；GPU均值2.471→2.487ms，主线程13.103→13.026ms属于调度波动，不能宣称加速或代表全部设备。此测量隔离岩带开关，不代表碰撞压力测试。

外观与分布：`Assets/_Starframe/Scripts/Runtime/Construction/Flight/RangeAsteroidBackdrop.cs`、同名环境shader。原生资产重建入口：`tools/RangeBackdrop20261102/InstallBackdrop.cs` → `Starframe.EditorTools.RangeBackdropInstall.Apply`。质量密度入口仍是各`VoxelAssetDefinition.massPerCell`（kg/m³），不靠提高全局密度隐藏碰撞问题。测试结束已退出Play并回到Hangar。
