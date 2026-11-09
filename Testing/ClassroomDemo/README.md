# Classroom LAN test channel

这是独立的 Windows x64 课堂测试频道，与正式版本分开。运行包发布在 **test/prerelease 的 Release 附件**，不把二进制提交到 main。

当前版本：`test-classroom-2026-11-09-08172d1c`，构建 GUID `08172d1cab7149f58b8e4812ca4d8b55`。

## 手动下载

1. 打开 [测试预发布版本](https://github.com/Sidonia704/CGDD2/releases/tag/test-classroom-2026-11-09-08172d1c)，下载附件 `ClassroomDemo-Test-08172d1c-r2.zip`，不要选 GitHub 自动生成的 Source code。
2. 与同版本 `SHA256SUMS-v2.txt` 核对。用 Windows PowerShell 的 `Get-FileHash .\ClassroomDemo-Test-08172d1c-r2.zip -Algorithm SHA256` 查看摘要。
3. 解压到新的版本目录，完整保留 `ClassroomDemo` 文件夹。旧版本不必删除。双击 `Starframe-LAN.exe`，不需要 Unity、Codex 或账号。
4. 两台电脑用同一版本和允许互访的局域网。房主创建房间，客机填写房主局域网 IPv4 和相同端口（默认7777）；2/2后房主开始。两边按A/D平移即可演示共享位置和移动。Esc可退房再开。

## 可选更新脚本

下载并检查本目录的 `Update-Test.ps1`，按电脑现有脚本策略运行：

本目录脚本为修正版。Release附件请选择 **Update-Test-v2.ps1**，运行命令相应换名；初版 `Update-Test.ps1` 对ZIP中文文件名不兼容，会在切换版本前安全停止。初版ZIP另有一项操作卡清单不一致，请使用 r2 ZIP；两项修复均不改动游戏二进制。旧附件保留供历史复核。

```powershell
.\Update-Test.ps1
# 可选：指定一个独立安装目录
.\Update-Test.ps1 -InstallRoot .\CGDD2-Test
# 已经下载ZIP时，仍会完整检查摘要和包清单
.\Update-Test.ps1 -ArchivePath .\ClassroomDemo-Test-08172d1c-r2.zip
```

脚本只读取本仓库明确的 `classroom-test` 频道：先下载到临时目录，检查ZIP的SHA256、路径和展开数量，再检查包内每个文件及构建GUID，全部通过后写入独立版本目录并切换 `current.json`。旧版本保留，脚本不会自动启动程序，不嵌入凭证，不修改网络或执行策略。若组织策略不允许脚本，使用上面的手动下载流程。

## 范围与更新维护

最低目标只有双人在同一空间共享位置和移动。已有直射交火和损毁保留为可选演示；没有完整比赛规则或自定义蓝图。重开后部分中文屏内提示偶发缺行，外部操作卡随包提供。实体双机、持键切窗和人工手感仍由实际设备验证；本机自动输入结果不代替这些结论。

公开分发版仅排除了调试符号与编译调试文本，游戏EXE/DLL及构建GUID保持原样；包清单相应更新。原冻结构建和原ZIP保留在开发端。

测试脚本、说明和 `channel.json` 仅维护在 `codex/classroom-demo-test-channel` 分支的本目录。后续发布使用新的 `test-classroom-*` 预发布与独立附件，上传并验证成功后再更新频道；不要覆盖既有版本附件。该分支提交记录的是分发工具，不代表main源码能重现此运行包。
