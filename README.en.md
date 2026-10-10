# ForegoneConclusionFix

A bug-fix mod for **Slay the Spire 2**: the extra cards granted by *Guiding Star*
(`DrawCardsNextTurnPower`) are silently lost if you also played *Foregone Conclusion*.

> 中文说明见 [README.md](README.md)

## The bug

1. Turn N: play *Guiding Star* (grants `DrawCardsNextTurnPower`) and *Foregone Conclusion*
   (grants `ForegoneConclusionPower`).
2. Turn N+1: `SetupPlayerTurn` runs `Hook.BeforeHandDraw`. Foregone Conclusion opens its
   card-select screen there, which **pauses** the turn setup on a player choice.
3. `CombatManager.StartTurn` only waits for pause-or-completion
   (`WaitForPauseOrCompletionWithoutAssigningTask`), so it keeps going and fires
   `Hook.AfterSideTurnStart` while that setup is still paused.
4. `DrawCardsNextTurnPower.AfterSideTurnStart` sees `AmountOnTurnStart != 0` (the power was
   there when the turn began), assumes this turn's hand draw already happened, and removes
   itself.
5. When the select screen closes, the setup resumes and runs `ModifyHandDraw` — but the power
   is already gone, so the bonus draw is never granted.

Result: the extra draw is silently swallowed.

## The fix

`DrawCardsNextTurnPower` is no longer removed in `AfterSideTurnStart`. Removal is deferred to
the end of the player's turn setup (`Hook.AfterPlayerTurnStart`), which always runs **after**
the hand draw — whether the setup was paused or not.

Two Harmony patches:

| Kind | Target | Purpose |
| --- | --- | --- |
| Prefix (skip) | `DrawCardsNextTurnPower.AfterSideTurnStart` | Suppress the premature self-removal, returning `Task.CompletedTask` via `out Task __result` |
| Postfix (pass-through) | `Hook.AfterPlayerTurnStart` | Remove the power after the hand draw when `AmountOnTurnStart != 0` |

### Why not reorder CombatManager instead

Deferring `AfterSideTurnStart` until every `SetupPlayerTurn` completes deadlocks: the paused
card-select game action cannot start executing until `ActionExecutor.Unpause()` runs near the
end of `StartTurn`, which is *after* `AfterSideTurnStart`. Patching the power keeps the fix
local, safe and multiplayer-deterministic.

### Harmony pitfalls (for future maintainers)

Patching async (`Task`-returning) game methods has two traps — both were hit while developing
this mod:

1. **A postfix on an async method must be pass-through.** Return `Task` with `Task __result`
   as the **first** parameter (`static async Task Postfix(Task __result, Player player)`),
   otherwise `PatchAll` throws
   `Return type of pass through postfix ... does not match type of its first parameter`.
2. **A skipping prefix on a `Task`-returning method must supply a result.** Harmony returns
   `default(T)` — that is, `null` — when a prefix returns `false`, and the caller's
   `await model.AfterSideTurnStart(...)` then throws `NullReferenceException`, killing the
   combat turn loop (soft-lock that requires restarting the room). Always declare
   `out Task __result` and set it to `Task.CompletedTask`.

## Requirements

* Slay the Spire 2 `v0.111.0` or newer
* .NET SDK 9 (only to build from source)
* No dependencies — BaseLib and other mods are not required

## Build

```bat
dotnet build ForegoneConclusionFix.csproj -c Release
```

A Release build deploys automatically to `<GameDir>\mods\ForegoneConclusionFix\`. Override the
game path if needed:

```bat
dotnet build ForegoneConclusionFix.csproj -c Release /p:Sts2Dir="D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

Debug builds never deploy, so a stray `dotnet build` cannot overwrite a working installation.

## Install

Copy `ForegoneConclusionFix.dll` together with `ForegoneConclusionFix.json` into:

```
<GameDir>\mods\ForegoneConclusionFix\
```

The manifest and the DLL must sit in the same folder, and the DLL file name must equal the
manifest `id` (`ForegoneConclusionFix`) — that is exactly how `ModManager` loads a mod
(`Path.Combine(mod.path, id + ".dll")`).

> Note: `<GameDir>\mods_disabled\` is never read by the game. Mods must live under `mods\`.

## Repository layout

| Path | Tracked | Notes |
| --- | --- | --- |
| `ForegoneConclusionFix.csproj` | yes | Mod project (references `sts2.dll` / `0Harmony.dll` from the game install) |
| `ForegoneConclusionFix.json` | yes | Mod manifest |
| `ForegoneConclusionFixPatch.cs` | yes | The Harmony patch source |
| `Alchyr.Sts2.Templates.slnx` | yes | Solution file (originally from the StS2 mod template) |
| `README.md` / `README.en.md` | yes | Chinese / English documentation |
| `bin/`, `obj/`, `.vs/` | **no** | Build output and IDE caches — gitignored, regenerated on build |
