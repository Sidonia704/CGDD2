# 蓝色模拟空间视觉试样

最新要求：明确的蓝底，不使用像素滤镜。

首页、装配、靶场和正式关卡已改用Simulation Sky程序化天空；不读取星空HDRI和行星照片。行星为橙色单色自发光，保留少量明暗色阶及轮廓亮度。原有材质细节和原分辨率显示保持，像素试样代码、shader和设置资产已移除。

可调材质：Assets/_Starframe/Art/SimulationTrial/SimulationBay.mat（菜单/装配），SimulationSpace.mat（靶场/关卡）。Background控制蓝底；PlanetColor控制橙色及发光值。SimulationAtmosphere.asset为独立的轻雾配置，关闭烟尘粒子，不改原配置。

实际截图：assembly-blue.png、flight-blue.png。仅作第一版方向确认；未进行进一步强风格化。

撤回：在停止Play且场景已保存时，通过Unity run_script运行tools/SimulationLook20261103/Install.cs入口InstallSimulationLook.Restore。它使用同目录Before中的四份原生场景备份恢复原HDRI/光照/环境引用。原天空素材保留。Main可重新安装本版蓝底，已去除像素设置。

验证：原生四场景保存、Shader零编译错误；实际装配/关卡渲染截图；3D相机分辨率2560与屏幕2560一致；没有像素处理材质。视觉预览后Console零错误、零警告。未修改战斗规则或平衡数据。

2026-11-03：按试玩反馈，战斗天空Background从(0.16,0.34,0.65)降为(0.128,0.272,0.52)，线性强度降低20%；菜单/装配蓝底与橙色行星保持原参数。安装器同步更新。

2026-11-03：橙色行星发光颜色强度降低25%，PlanetColor改为(0.9,0.36,0.04875)，保留橙色色相与轮廓效果。
