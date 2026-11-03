# 组件库扩充 · 2026-10-25

本轮保留原有 10 种组件，新增 26 种，共 36 种。组件来自 `Assets/_Starframe/assetsForCodex/`；原 FBX、PNG、现有蓝图和地图不覆盖。组件的生产参数见 [Catalog.md](Catalog.md)，材质与模型总览见 [CatalogVisuals.png](CatalogVisuals.png)。

## 使用

- 从首页进入装配，右侧按「船体、推进、核心、电源、武器、护盾」分类滚动选择。原组件仍可用；新基础船体也已进入「体块地图工作室」的形状笔刷。
- 武器与装甲通过真实底面安装；装甲属于护盾分类，但只提供高耐久，不生成能量护盾。
- 半高块顶面、斜面上的空体、武器顶部不能隔空托起下一块。半高块和推进器仍可通过存在的平直侧面连接。整数占格、枪口净空、推进器排气预留均保留。
- 机枪使用橙黄实弹，多枪管轮流开火；激光速射炮使用蓝色弹束；电磁狙击炮使用高速、可穿透的橙黄色弹道。
- 选择目标后，导弹需要持续锁定同一存活目标 2 秒。HUD 显示锁定进度、已装填弹数及剩余装填时间；开火需要有效锁定，发射后自动耗电装填。更换目标或目标重生会重新计时；失去目标的在途导弹继续沿当前方向飞行。双联架实际发射两枚，分配同一轮的伤害预算。
- 光剑的三角结构为常驻件；蓝色剑刃为另外的开火特效。两档射程都是 40 米，接触第一个实体或护盾时停止；更大型号具有更高伤害、耗电与质量。
- 原有武器组、开火绑定、电力分配、护盾吸收、残骸和部件恢复仍参与这些装备的运行。

## 尺寸约定

尺寸按导入后的局部 X×Y×Z 与整数占格配置，模型使用等比缩放。武器朝 +Z 开火，推进器向 +Y 排气、提供 -Y 推力。文字尺寸与源模型的轴顺序不同处以模型朝向匹配，机枪 1 为长 3、宽 1、高 1；推进器 3/4/5 分别为局部 1×2×2、1×2×4、2×3×2。细节和全部质量、耐久、装填、功率见 Catalog。

地图的 Block7/Block8 使用同一原模型按格切分，保存各格的模型偏移和旋转；受击、删除、涂色及断裂仍按格处理，完整多格外形连续。

## 实现入口

- `Settings/Modules/ModuleDatabase.asset`：完整组件库。
- `Scripts/Editor/ComponentLibraryInstaller.cs`：可重复生成扩展资产；不改原十种定义。
- `BlockModules / BlockConnectivity / BlockPlacement`：真实接触面、占格和安装基准。
- `ShipWeapons / ProjectileWorld / SpecialWeaponVisualPool / CombatVfxPool`：开火、追踪、束武器及特效。
- `Scripts/Editor/Maps/`：地图笔刷与多格预览。

`CGDD/Export Trace Docs` 刷新课程证据；本轮不发布网站，不生成 Stage Reflection。

## 最终验收

- EditMode 285/285；相关 PlayMode 19/19 通过。历史AI移动目标失败未重跑。
- 完整组件面板：[ComponentPanel.png](ComponentPanel.png)。
- 真实模型与渲染池的静态特效预览：[CombatVisuals.png](CombatVisuals.png)。这张图是摆位预览；功能验收以测试结果为据。
- 实弹minimumVisualPixels=2.8，按视深调整显示厚度，最高为基础显示宽度10倍；不会改变伤害弹径。
- 78份原FBX/PNG与原10个组件定义校验均未变化。
