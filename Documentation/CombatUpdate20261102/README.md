# Combat Update 2026-11-02

## 功能

- `F` 对准红色目标标记并锁定；传感器接触、锁定状态与敌方锁定告警会反馈到 HUD。
- `R` 释放干扰弹：每次只轮流触发一个可用发射器；每模块独立冷却。齐射火星同时构成导弹诱饵机会，改变来袭导弹转向概率。
- 武器视觉增加短促白色炮口前缘、导弹点火渐入和光束接触余辉；保留原武器参数与轨道炮瞬发行为。
- 新增敌方接触、敌方锁定、导弹来袭和干扰弹释放音频提示；训练场采用独立土星环境天空材质。
- 船体编辑器显示干扰弹齐射数、冷却、诱饵概率与向上净空要求。

## 参数与资源入口

- 干扰弹模块及诱饵参数：`Assets/_Starframe/Settings/Modules/FlareLauncherCountermeasure.asset`；模块索引与建造分类入口：`Assets/_Starframe/Settings/Modules/FlareLauncher.asset`、`ModuleDatabase.asset`。齐射数、冷却、散布角、速度、寿命、诱饵概率和半径均在对应 `FlareDefinition` 资产中。
- 战斗/传感器共用调参：`Assets/_Starframe/Settings/Flight/BattleTuning.asset`。
- 各导弹武器参数：`Assets/_Starframe/Settings/Modules/Missile1Weapon.asset`、`Missile2Weapon.asset`、`Missile3Weapon.asset`。
- 音频事件及混音/播放调参：`Assets/_Starframe/Resources/Audio/GameAudioTuning.asset`。
- 训练场天空材质：`Assets/_Starframe/Art/Materials/Environment/SaturnRangeSky.mat`。
- Flare 源模型和原始图标保留在 `Assets/_Starframe/assetsForCodex/flare.fbx`、`flare.png`、`flares.png`；运行时模型/图标是派生资源，不回写源文件。

## Native authoring helpers

这些入口供外部 `run_script` 调用；本说明不要求或记录 Editor 执行结果。

- `tools/CombatUpdate20261102/FlareInstall.cs`：`Starframe.EditorTools.FlareAssetInstall.Install()` 创建/更新 Flare 原生资源；`Verify()` 检查引用、单格占用与网格边界。
- `tools/CombatUpdate20261102/AudioInstall.cs`：`Starframe.Editor.AudioThreatInstall.Install()` 安装威胁提示音及对应调参条目。
- `tools/CombatUpdate20261102/SaturnInstall.cs`：`Starframe.EditorTools.SaturnEnvironmentInstall.Install()` 安装训练场天空资源。
- `tools/CombatUpdate20261102/CombatSmokeProbe.cs`：在已初始化的 Play Mode `RangeCombatWorld` 中调用 `Starframe.EditorTools.CombatSmokeProbe.Read()` 读取世界、传感器、干扰弹与 outline 状态并可请求截图；`Exercise()` 会尝试选择一个已检测目标并截图，使用前应确保当前场景状态可接受该临时锁定操作。

## 单项回退

美术与声音可以分别调整：将天空材质的 Planet brightness 设为 0 可单独关闭行星、保留星空；四个新增音频 cue 各自有音量。干扰弹、签名发现和导弹制导参数分属对应资产。源码回退需同时处理调用方与资源引用，不能只删除一个脚本；已有蓝图可能使用 `FlareLauncher=37`，不要删除或重排这个持久化 ID。保留 `assetsForCodex` 源文件，不用整仓 `git reset` 覆盖此前合法改动。

## 测试

全部 EditMode 459/459 通过，无跳过；汇总见 `editmode-results.json`。实际场景死亡、残骸、重生和返回装配流程通过 1/1，见 `play-lifecycle-results.json`。快速重入读条专项通过 1/1，同时覆盖原始输入暂停/未暂停两种状态；多靶机独立重生、离屏锁定/战损状态、护盾共用电力和相对翻滚回归通过 3/3，见 `play-multitarget-results.json` 与 `play-loading-reentry-results.json`。

Native Play 检查确认两台发射器连续三次调用的结果为 true/true/false，活跃火星数量为 12/24，冷却10秒；F等效选择真实目标、75块目标只提交7组轮廓绘制，实体遮挡后的红色外轮廓、离屏保锁/红箭头以及来袭导弹状态查询均核对。临时测试船和遮挡物只在内存中创建，未写入用户蓝图。`Probe/` 保存实际画面。音频资源绑定已核对，最终听感需以实际扬声器试听为准。此轮未重新构建独立 Player，也不将这些检查当作新的性能基准。

快速连续加载还修复了旧读条关闭期间被新加载复用的问题：视图和输入恢复所有权随新读条转移。原地图构建/碰撞预算不变。土星纹理来源和许可见 `tools/CombatUpdate20261102/TexturesSource/CREDITS.md`。
