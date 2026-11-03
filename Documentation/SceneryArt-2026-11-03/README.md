# 地景材质分层（2026-11-03）

目标：降低白色体素碎点，让岩石、人工设施和玩家舰船有清晰主次。

- 1279 岩石资产：404 暖灰、383 石灰、418 冷灰、74 灰白冰质。按稳定资产名分组，整块配色；无逐格随机色。
- 1066 设施资产：浅暖灰主舱体、深灰桁架、较暗的散落设备／碎片。两条已命名桁架单独处理；匿名 station/ref 结构保留主壳色，未猜测其工程用途。
- 外围陨石使用同一共享 shader，哑光且加强远距离对比收敛；原有实例化、位置、LOD 和近远淡出保留。
- ScenerySurface.shader：低频局部坐标色块（随物体移动）、降低面间对比、高光随远近整体收敛、环境光色距离衰减；设施无岩石斑块。
- VoxelHullAppearance 允许显式 surfaces 覆盖借用的舰体材质。未设置 surfaces 的资产继续用原舰体材质。地图粗细网格使用相同材质槽。
- 地图物理、体素内容、血量、不可破坏开关、飞控、敌舰及玩家蓝图无改动。本轮不增加粒子、动态灯和 UI 装饰。

调节入口：Assets/_Starframe/Art/SceneryArt/*.mat。岩石 smoothness 为 .21/.24/.22/.31，设施 .42/.29/.35，外围 .20。颜色是材质基色，实机受灯光与色彩空间影响。

工具：tools/SceneryArt20261103/Install.cs。Start 为首次安装，已有原始 manifest 时拒绝覆盖；Audit 检查原始格数、几何签名、LOD签名及共享材质槽。Refine 为旧安装版本的设备碎片配色补充。

回退：停止 Play 后通过 run_script 调用 SceneryArtInstall.Restore，恢复 before-materials.json 中每个地图资产原本的 baseColor、surfaceColors、surfaces，及外围原材质。显式覆盖入口和新增 shader 可保留（恢复后不会被地图使用）。如需连代码也恢复，原始副本在 Before/VoxelHullAppearance.cs.txt，先确认后续没有新的代码编辑。

证据：before.png 为本轮原生靶场基准；after.png 为同入口更新后；接近与近距离另见图片。验证结果记录于 validation.txt。截图仅证明所示视角，没有完成独立包性能测量。

实际验证：2345资产／18760槽位通过，2854运行时地图刚体、帧推进和加载完成已确认。before/after 是原生屏幕（含后处理和HUD）；approach/close 是临时独立相机诊断图（无HUD，后处理观感与原生屏幕不同）。临时相机已销毁，验证Play已停止。新shader零编译消息，最终Console ground truth为0 error / 0 warning；捕获历史保留工具超时与旧字体警告。未做独立包性能对比。

回退限制：首次临时工具的JsonUtility写出空对象，已改为可靠JSON序列化并加入2345数量门槛。地图颜色回退清单依据字段默认值、原始导入约定、修改前抽样头和基准截图重建，不能当作逐资产完整修改前快照；详见baseline-provenance.txt。几何校验是对当前资产与原已烘焙签名的一致性检查。源码和外围材质副本为修改前精确副本。

课程文档：已通过CGDD/Export Trace Docs刷新。菜单在完成提示框上等待，工具超时；电脑控制通道不可用，可能需人工关闭提示框。仅视觉修改，未追加游戏Interaction或Stage Reflection。
