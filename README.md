# ForegoneConclusionFix

Slay the Spire 2 修复 mod：持有「先行指引 / Guiding Star」的 `DrawCardsNextTurnPower` 时打出「既定事项 / Foregone Conclusion」，下回合额外抽牌被吞掉的问题。

## 根因

`CombatManager.StartTurn` 中，`SetupPlayerTurn` 在 `BeforeHandDraw` 阶段被既定事项的选牌界面暂停时，`AfterSideTurnStart` 仍会照常触发；`DrawCardsNextTurnPower.AfterSideTurnStart` 因此提前移除了自己，等选牌结束 `ModifyHandDraw` 结算时力量已不存在。

## 修复方式

- 让 `DrawCardsNextTurnPower.AfterSideTurnStart` 不再移除力量（Harmony Prefix 跳过原方法）；
- 改为在 `Hook.AfterPlayerTurnStart`（`SetupPlayerTurn` 最后一步，永远在抽牌之后）移除（Harmony Postfix）。

## 构建

```bat
dotnet build ForegoneConclusionFix.csproj -c Release
```

构建后会自动把 `ForegoneConclusionFix.dll` 与 `ForegoneConclusionFix.json` 复制到
`<游戏目录>\mods\ForegoneConclusionFix\`（游戏目录可在 csproj 的 `Sts2Dir` 属性或 `/p:Sts2Dir=...` 覆盖）。

## 部署

游戏 ModManager 递归扫描 `<游戏目录>\mods\` 下的 `.json` 清单，并从“清单所在目录 + `id.dll`”加载程序集。
因此清单与 DLL 必须同名同目录（本项目的构建 target 已保证这一点），DLL 名必须等于清单的 `id`。

> 注意：`<游戏目录>\mods_disabled\` 不会被游戏读取（代码中无引用），mod 必须放在 `mods\` 下才会加载。
