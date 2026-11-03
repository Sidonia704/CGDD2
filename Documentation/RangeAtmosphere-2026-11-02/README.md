# 靶场环境氛围与稀疏网格

后续更新：本轮要求的景深模糊已关闭，远景改用距离淡出；外景岩带与巨型行星以 `Documentation/RangeBackdrop-2026-11-02/README.md` 为当前配置。以下保留上一阶段的氛围参数和测量记录。

- 定位墙间隔 200 → 600 米，墙内细格 4 → 12 米；定位线透明度 .42，色彩降低，完全在淡出距离外的网格不提交绘制。边界提示仍有独立可见性。网格切片在切线方向跟随玩家，纹路使用世界坐标，不会随切片漂移。
- 独立后处理 `Assets/_Starframe/Settings/Rendering/RangeAtmospherePostFX.asset`：四分之一分辨率 Bloom，5级、散射 .64、阈值1.05；Gaussian远景景深450–1800米、半径 .65。近处舰船和另一路渲染的HUD/准星保持清晰。光晕强度仍由游戏画面设置控制，降低闪烁选项仍优先。
- `Assets/_Starframe/Settings/Rendering/RangeAtmosphereTuning.asset` 控制尘埃、薄雾、环境颜色、强度与淡出。PC尘埃最多160、薄雾最多12；Mobile画质尘埃最多64，关闭薄雾卡片与景深。粒子世界坐标模拟，带深度软交界，关闭碰撞/触发/拖尾/粒子灯/阴影。两张128×128单通道密度纹理约42 KB，复用两种材质，无额外摄像机。
- 背景以恒星光源方向生成宽暖色光晕、细核心光晕及冷色散射。引用的 Light 方向、颜色和强度变化时平滑响应；小行星带附近薄雾增强，上方空域减弱。默认 `skyIntensity=.09`，`skyWarm/skyCool` 调配色，`beltHeightOffset=-200`、`beltFalloff=1200` 调环境层位置。场景引用由 `RangeAtmosphere` 的 `starLight/beltAnchor/volume` 指定，无每帧全场景搜索。
- 场景中保留原来的两盏灯与阴影预算，补光 .28 → .38；原 ConstructionPostFX、首页和装配用的共享配置未覆盖。雾在场景中标记为启用以保留 Player 所需 shader 变体，运行时按配置及高度调整。

## 验证与限制

`comparison.json` 是同一靶场、相同新网格下的120帧基线/120帧增强 Editor 对比，AI临时静止；`before-atmosphere.png`、`after-atmosphere.png` 为实际画面。`light-response.json` 验证临时改变恒星方向/颜色以及摄像机升高1500米后的环境响应，光源和摄像机随后恢复。测试不写用户蓝图。

基线是原 ConstructionPostFX、关闭新尘埃/薄雾/背景光色；增强使用新增配置。最终满额探针在2560×1440、RTX 5080的Editor中临时延长测试粒子寿命、关闭寿命淡出，使160个尘埃和12层薄雾始终满额，结束后恢复正常配置。120帧/模式：主线程均值12.701 → 13.140 ms（+0.439 ms），GPU均值2.642 → 2.715 ms（+0.073 ms）；主线程P95 20.144 → 19.976 ms。相邻帧确认粒子实例ID稳定、无反复重建。该机单次Editor对比受调度影响，不能当独立Player FPS或所有硬件的上限保证。原场景几何、地图加载与碰撞预算保持；本轮未重新构建Player。

单画质档位的平台筛选曾让PC索引0与内部档位映射不一致；已统一创建与轮询映射，避免半秒一次的粒子重建。最终探针验证PC单档位的系统稳定。穿越定位墙实测1个定位切片启用、格距12、opacity=.42；停在淡出范围之外时0个网格绘制。光源方向变化约44.9度、光色变化及1500米高度变化均验证能驱动背景/薄雾，临时光源和摄像机变更已恢复。最终Console无错误/警告。

## 调整和分项恢复

网格参数在 `Assets/_Starframe/Settings/Flight/FlightTuning.asset`。后处理与氛围参数是两个独立资产；将 skyIntensity 设0可单独关闭背景颜色，dustEnabled/hazeEnabled/distanceHaze 控制各层（重进靶场应用粒子配置）。Volume切回 `ConstructionPostFX.asset` 可恢复旧后处理。旧数值记录在 `before.json`。不要用整仓回退覆盖其他功能。

原生安装工具：`tools/RangeAtmosphere20261102/Install.cs` 的 `RangeAtmosphereInstall.Install()`；资源及场景通过Unity API创建和保存。性能探针在 `Probe.cs`，光照响应探针在 `LightResponse.cs`，均只用于开发验证。
