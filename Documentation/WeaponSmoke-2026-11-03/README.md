# 克制的枪口烟带与导弹尾烟（2026-11-03）

历史版本：当前已增强双方烟带并加入实弹弹道烟，详见 ../WeaponSmokeDense-2026-11-03/README.md。以下参数和验证是上一版本记录。

按用户修正：实弹烟主要来自发射地点，射出后短而淡；移动中连续开火会将各次枪口烟留在世界空间，形成一条淡烟带。导弹沿真实转弯弹道留烟。两者均缓慢扩散消散。

## 当前参数

| 项目 | 默认值 |
| --- | --- |
| 导弹烟寿命 | 2.4秒 |
| 枪口烟寿命 | 2.6秒 |
| 单粒峰值透明度 | 导弹0.17，枪口0.12 |
| 导弹采样间距 | 2.4米，每次真实弹道步最多12个 |
| 总发射预算 | 每帧最多160粒 |
| 容量 | 导弹1536，枪口640，共两个ParticleSystem |
| 远处发射裁剪 | 4500米外 |
| 扩散 | 从0.75倍尺寸到2.8倍，低速漂移 |

调节入口：Scripts/Runtime/Construction/Combat/WeaponSmokePool.cs 的默认字段（运行时组件可看）；Resources/Combat/WeaponSmoke.mat 的深度软化。体素发光尾焰仍保留。新烟为非发光的灰白柔边billboard，没有粒子碰撞、灯光、逐发GameObject或新增伤害查询。

WeaponDefinition.smokeTrail 是明确的视觉开关。仅 Gun1/2/3、Railgun1/2/3、Missile1/2/3 共9份原生武器资产开启；激光和光束保持关闭。数值仍读取现有武器资产。

ProjectileWorld 在实体实弹真正发射时调用枪口烟；导弹在原有0.12秒弹射后沿实际扫掠段发射烟，命中段止于实际命中点。烟在粒子自己的世界空间中独立衰减，弹丸回收不删除已有烟。切换场景／清理战斗会统一清空。

本轮未修改弹速、伤害、射速、导弹制导、供电、武器组、胜负或输入。烟的确定性序列不消耗UnityEngine.Random，避免影响战斗随机性。

## 验证与截图

- SmokeReview.Audit 通过：原生开关、枪口驻留、弹道采样边界、激光排除、预算、战斗随机数隔离、1秒仍存在、3秒内消散、清理后复用。
- ProjectileMechanicsTests 15/15通过；WeaponVfxTimingTests 3/3通过。首次时序测试结束后过早进入Play，测试器场景清理报告了异常；停止Play后重新运行这3项通过，后续日志窗口没有新增错误。
- 原生靶场短时受控验证：使用实际挂载枪口变换及ProjectileWorld，24发实弹、6枚导弹、279帧；枪口粒子峰值24/640，导弹1536/1536。高密度情况下容量保护会限制新烟，不影响实体弹丸。
- native-firing.png：真实发射中；native-lingering.png：停止发射后仍保留的烟。临时测试目标与外部飞行控制仅存在验证会话，Play已停止。
- 新烟shader零编译消息。未做独立包GPU或全任务性能测试。

工具：tools/WeaponSmoke20261103/SmokeReview.cs，SmokeNativeReview.cs，SetSmoke.cs。后者SetEnabled(false)可以通过Unity API关闭这9份资产的新烟效果；true重新打开。Before/保留改动前源码和这9份资产文本副本作为证据，不覆盖未来编辑。

课程序列：新增游戏视觉Interaction 025，未创建Stage Reflection；TraceDocsExporter.ExportForAutomation调用与菜单相同的导出核心，刷新docs与site/data并避免交互提示框。
