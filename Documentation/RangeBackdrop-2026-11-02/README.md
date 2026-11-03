# Range 背景岩带

## 功能与范围

`FlightRange` 原生场景组件 `RangeAsteroidBackdrop` 使用固定 seed 和 `Graphics.RenderMeshInstanced` 绘制装饰岩带；不创建刚体、碰撞体或 HP，不参与命中、破坏和地图预算。相机、详细/远景网格和岩石材质都已序列化在场景组件上，无需运行时再调用 `Configure`。四个 Rock 网格包含 per-face 顶点色，shader 将其作为表面 tint。

场景单位为米。中央带沿 Z=-25,000…25,000 m 延伸、X 在 ±1,450 m 内，|Z|<2,400 m 留出地图区域。左右带分别位于 X=−3,200…−5,900 m 与 X=3,200…5,900 m，沿全 Z 长度延伸，给地图两侧留空。岩带高度为 Y=−1,330±185 m。远端岩石直径 45–310 m，较近区域 24–150 m，三个轴再分别乘随机形状系数。

## 预算、细节与距离

生成按 2,000 m 的 Z 段分组，每段中央最多请求 48 个、两侧各最多请求 64 个；中央地图空档会跳过样本。硬上限为 4,800 个生成体、每帧 3,000 个可见体及 180 个提交批次。实例按包围盒做距离与视锥筛选，再从近到远提交；每批共享 LOD，距批次包围盒小于 8,500 m 用详细网格，其余用远景网格。源网格须以原点为中心、适配单位半径；包围范围按最长缩放轴的 1.15 倍生成。

近距离渐隐区间为 1,100–2,100 m，远端为 17,000–25,000 m。shader 保持不透明和深度写入，用屏幕门限裁剪做淡出。`FlightRange` 世界相机 near/far 为 0.15/28,000 m，far clip 能覆盖淡出终点。Range profile 的景深模式已通过原生API设为 `Off`，环境初始化也覆盖为 `Off`，不会被画质切换重新打开；450/1,800 m 等未使用的参数保留在组件序列化中。岩石采用中性灰和非发光漫反射，避免背景触发强光晕。

## 行星材质尺度

`SaturnRangeSky.mat` 使用世界坐标解析球模式：`_PlanetWorldRadius=20,000,000 m`，`_PlanetCenterWS=(-20,300,000,-400,000,800,000) m`；最近表面约319.7公里、视觉半径约79.82°。shader 在行星半径单位中求交，没有新增球体网格或摄像机，行星距离不受28公里裁剪限制。相较初版60°角半径，边缘更平缓；世界尺度使几公里飞行只有很小的行星视差。`_PlanetRadius=60°` 仅供 `_PlanetWorldRadius` 不大于1时的回退模式使用。

## 相机与独立回退

岩带的 `RenderParams.camera` 指定世界相机；`RangeHUD` 将该相机的图像显示在场景 RenderTexture。HUD 显示相机只渲染第 5 层，世界相机排除第 5 层，岩带保持在世界相机可见层，因此不会被 HUD 相机重复绘制。

单独关闭岩带可将组件 `backdropEnabled` 设为 false，或禁用/移除该组件；不会改变飞行、碰撞、地图或 HUD。行星可将材质的 `_PlanetWorldRadius` 设0恢复旧角半径模式。若后续主动恢复景深，需同时修改 Range profile 的模式与 `RangeAtmosphere.Initialize` 的 Off 覆盖；当前按本轮要求保持关闭。

## 原生验证

`performance.json` 是最终中性调色的 fixed-input Editor 开关对比；2560×1440、RTX 5080、每模式120帧。实际生成4,285块，该飞行视角提交2,112块/73批/76,800三角形，新增岩带没有碰撞体或刚体。最新GPU均值2.626→2.632ms，CPU14.148→13.785ms；CPU负差属于Editor调度波动，不宣称加入几何能提升性能。较早采样的CPU增量约0.61ms、GPU约0.07ms。只测岩带开关差额，两模式均无景深并使用同一行星；最终十倍行星世界尺度不增加绘制路径。没有重新构建Player，不能当所有视角/硬件的保证。

俯视记录在 `overview.png`：三条平行带各自分开，左右留有净空，画面计数为 2,173 rocks / 76 batches。飞行视角及 HUD 见 `flight-backdrop.png`。

![俯视岩带与净空](D:/software/unity/CGDD26107/Documentation/RangeBackdrop-2026-11-02/overview.png)

![飞行视角中的行星、岩带与 HUD](D:/software/unity/CGDD26107/Documentation/RangeBackdrop-2026-11-02/flight-backdrop.png)
