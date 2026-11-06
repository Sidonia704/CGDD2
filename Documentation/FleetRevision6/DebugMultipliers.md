# 调试倍率与重型舰平衡

Unity Project窗口选择以下资源，在Inspector直接改数值；当前三项均为1，2表示两倍。

| 用途 | 资源 | 字段 |
| --- | --- | --- |
| 所有武器伤害（含双方、弹丸、光束、常规导弹和垂发） | Assets/_Starframe/Settings/Combat/CombatDamageTuning.asset | globalDamageMultiplier |
| 所有舰船推进器推力 | Assets/_Starframe/Settings/Flight/FlightTuning.asset | thrustMultiplier |
| 所有舰船组件有效重量 | Assets/_Starframe/Settings/Flight/FlightTuning.asset | blockMassMultiplier |

菜单入口：Starframe → 平衡参数 → 伤害倍率；Starframe → 平衡参数 → 打开飞行参数。

全局伤害倍率与原有分类/阵营倍率相乘一次。推力与重量倍率不改写各组件原始数值；有效质量、重心、惯量、舰船性能和实际控制使用缩放后的值。运行时在Inspector调整会刷新舰船性能。

同一FlightTuning内可调重型舰曲线：heavyMassThresholdKg=100000；heavySpeedExponent=.65；heavyMinimumSpeedFraction=.35；heavyManeuverExponent=.35；heavyMinimumManeuverFraction=.45。100吨以内不加额外惩罚；按当前原始质量，t7限速约149.6m/s，t8约88.1m/s，原基础上限250m/s。舰体损失或重量倍率改变会重新计算。

垂发巡航速度196m/s、初速24.5m/s，均比之前降低30%；转向330度/秒和角加速度1080保持。

镜像：M切换，核心的位置和朝向决定左右镜像面。两侧全部通过后才安装；任一侧冲突时整次拒绝并显示原因。中线自对称部件只放一份。镜像模式下删除对应部件也整体检查连接性。

武器组：拖动到组框并松手后生效，随即清空选择；此后点击组框仅显示成员。默认第一组受保护，其他组提供减号删除。
