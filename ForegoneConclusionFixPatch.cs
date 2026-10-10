using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Powers;

namespace ForegoneConclusionFix;

/// <summary>
/// 修复：玩家持有 <see cref="T:MegaCrit.Sts2.Core.Models.Powers.DrawCardsNextTurnPower" />（如先行指引 Guiding Star）
/// 且打出过既定事项 Foregone Conclusion 时，下回合的额外抽牌被吞掉的问题。
///
/// 根因（对照 CombatManager.StartTurn 确认）：
///   1. SetupPlayerTurn 开始；
///   2. Hook.BeforeHandDraw 执行；既定事项的力量在其中打开选牌界面（CardSelectCmd.FromCombatPile），
///      使回合 setup 暂停在玩家选择上；
///   3. CombatManager 只用 WaitForPauseOrCompletionWithoutAssigningTask 等待“暂停或完成”，
///      因此暂停期间就直接继续执行 Hook.AfterSideTurnStart；
///   4. DrawCardsNextTurnPower.AfterSideTurnStart 看到 AmountOnTurnStart != 0（本回合开始时就在），
///      认为本回合抽牌已发生，于是移除自己；
///   5. 选牌结束、SetupPlayerTurn 恢复并执行 ModifyHandDraw 时力量已消失，额外抽牌丢失。
///
/// 修复：不再在 AfterSideTurnStart 移除该力量，改为在玩家回合 setup 的末尾
/// （Hook.AfterPlayerTurnStart，永远在抽牌之后执行、无论是否暂停过）移除——ModifyHandDraw 先吃到加成，
/// 随后立即清理，与原流程意图一致。
///
/// 为什么不改 CombatManager：被暂停的选牌游戏动作要等 StartTurn 末尾的 ActionExecutor.Unpause()
/// 才能开始执行，而它排在 AfterSideTurnStart 之后；在 AfterSideTurnStart 前等待暂停的 setup 会死锁。
/// 在本力量层面修补更安全、局部、且联机确定性一致。
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ForegoneConclusionDrawFixPatch
{
    public static void Initialize()
    {
        new Harmony("ForegoneConclusionFix").PatchAll(typeof(ForegoneConclusionDrawFixPatch).Assembly);
    }

    /// <summary>
    /// 永远不要在这里移除力量：AfterSideTurnStart 可能在玩家回合 setup 仍暂停时触发
    /// （既定事项的选牌就在 BeforeHandDraw 里），即 ModifyHandDraw 结算加成之前。
    ///
    /// 注意：本方法返回 Task。Harmony 的 Prefix 返回 false 跳过原方法时，对 Task 返回型方法
    /// 会返回 default(T)（即 null），外层 Hook.AfterSideTurnStart 的 `await model.AfterSideTurnStart(...)`
    /// 会对 null 取 awaiter 抛 NullReferenceException（表现为“结束回合后战斗卡死/软锁”）。
    /// 因此必须通过 `out Task __result` 提供非 null 的已完成任务。
    /// </summary>
    [HarmonyPatch(typeof(DrawCardsNextTurnPower), nameof(DrawCardsNextTurnPower.AfterSideTurnStart))]
    private static class NoRemoveAtSideTurnStart
    {
        [HarmonyPrefix]
        private static bool Prefix(out Task __result)
        {
            __result = Task.CompletedTask;
            return false;
        }
    }

    /// <summary>
    /// AfterPlayerTurnStart 是 SetupPlayerTurn 的最后一步，永远在抽牌之后、对玩家侧每名玩家执行一次，
    /// 无论其 setup 是否被暂停过。这里是唯一的移除点。
    ///
    /// 注意：Hook.AfterPlayerTurnStart 是 async Task 方法，Harmony 要求其 Postfix 采用 pass-through 形式
    /// （返回 Task、第一个参数为 Task __result），否则 PatchAll 会因“Return type of pass through postfix
    /// does not match type of its first parameter”抛异常。await __result 确保原方法（含全部模型钩子）已执行完。
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    private static class RemoveAfterHandDraw
    {
        [HarmonyPostfix]
        private static async Task Postfix(Task __result, Player player)
        {
            await __result;
            if (player?.Creature == null)
            {
                return;
            }
            DrawCardsNextTurnPower? power = player.Creature.GetPower<DrawCardsNextTurnPower>();
            if (power == null || power.AmountOnTurnStart == 0)
            {
                // AmountOnTurnStart == 0 表示力量是在本回合内（回合开始快照之后）才施加的，
                // 是给下一回合用的，不能移除。
                return;
            }
            // 与 PowerCmd.Remove 相同的清理序列，只是去掉了装饰性的 CustomScaledWait，
            // 避免给每名受影响的玩家回合开始增加 0.2-0.4s 延迟。
            power.RemoveInternal();
            await power.AfterRemoved(power.Owner);
        }
    }
}
