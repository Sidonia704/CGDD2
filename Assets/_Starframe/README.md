# Starframe 工程目录

入口场景：Scenes/Testing/SpaceTestRange.unity。打开后查看 Game 窗口，按 Play 运行。

- Art/Models、Textures、Materials、Shaders、VFX、Animations：美术资源。
- Audio/Music、SFX、Mixers：音乐、音效与混音。
- Prefabs/Ships、Parts、Weapons、Targets、Environment、VFX、UI：可复用对象。
- Scenes/Testing、Gameplay：测试靶场和后续关卡。
- Scripts/Runtime：Core、Data、Ships、Construction、Combat、Energy、Movement、AI、Recovery、Presentation、Testing。
- Scripts/Editor：仅编辑器工具。
- Data/Parts、Weapons、Blueprints、Levels、TestFixtures：定义、蓝图和实验配置。
- Settings/Rendering、Input：项目专用渲染与输入配置。
- Tests/EditMode、PlayMode：后续有实际逻辑后添加对应验证。
- Documentation：需求范围和验收记录。

目录名称采用英文以方便代码与资源路径引用。当前空目录是职责划分，不代表已实现相应模块。
现有模板 Assets/Scenes、Settings、TutorialInfo 保留原位，新增资源集中放在 _Starframe。
具体规则和本轮交付边界见 Documentation/TestRangeRequirements.md。
