# Neck Snapping and Force Choke

Choke an enemy with your bare hands: grab him by the **neck** and hold on.

- **Force choke** - the enemy clutches, convulses, buckles and wheezes while you squeeze.
  12 HP per second, death in about 8 seconds.
- **Neck snapping** - press **B** on the right controller (or Y on the left) while holding
  the neck and it breaks with the game's own fracture system, not a scripted animation.
- **Choking out is not instant.** He fights you: twitching, kicking legs, going limp.
- Release the neck and he stops taking damage immediately - a half-choked enemy survives.

Everything reuses the game's systems - the neck joint breaker, the damage types, the ragdoll
joints and the enemy voice bank - so it behaves like a vanilla mechanic.

## Requirements

- **BRUTALISTICK VR** (Steam, app 2669380, Unity 6000.0.59f2)
- **MelonLoader 0.7.3** or newer, x64, net6

## Install

1. Install [MelonLoader](https://melonwiki.xyz/) into BRUTALISTICK VR (once).
2. Drop `plugins/NeckSnappingAndForceChoke.dll` into `<game folder>\Mods`.
3. Start the game. The MelonLoader console should say `NeckSnappingAndForceChoke: loaded`.

Install with r2modman or the Thunderstore Mod Manager and steps 1-2 happen for you.

## Uninstall

Delete `NeckSnappingAndForceChoke.dll` from `<game folder>\Mods`.

## How to play it

1. Walk up to an enemy and grab him by the **neck** - not the head, not the chest. You will
   hear him start to wheeze and he will claw at you.
2. Keep holding. He loses his footing, kicks his legs and his health drains.
3. Press **B** at any moment to snap the neck and finish it.
4. Let go early and he lives with the damage he already took.

Grabbing the head or chest does nothing - only the neck chokes.

## Troubleshooting

- Log file: `<game folder>\MelonLoader\Latest.log`. Search for `Neck Snapping and Force Choke`.
- `kneeJoints=0` in the log means a game update probably moved the ragdoll joints - the choke
  and the snap keep working, only the leg animation stops.
- If the mod seems to do nothing, check that only one copy of the DLL is in `Mods` - an older
  `ChokeMelonMod.dll` should be removed.

## Compatibility

- Single player. Not tested with multiplayer mods; damage is applied locally, so other players
  will not see the choke the same way.
- No known conflicts. It reads `StickHealth`, `ColliderBridge`, `StickJointBreaker` and
  `ActRagJoint` and changes only the enemy it is choking.

## Building from source

```
dotnet build NeckSnappingAndForceChoke.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\BRUTALISTICK VR"
```

Without `GameDir` it uses the default Steam path. Requires the MelonLoader assemblies for the
game (`MelonLoader\net6` and `MelonLoader\Il2CppAssemblies`).

## Credits

- Made for **BRUTALISTICK VR** by Kubunautilus.
- Built with an AI coding agent (OpenCode, model DeepSeek V4.1 Flash) using the
  [universal-modder](https://github.com/rehan-remade/universal-modder) toolkit; all game
  behaviour was discovered by inspecting the game's own IL2CPP assemblies and verified in VR.
- No third-party art or audio: the icon is the author's, all sounds are the game's own.

## License

MIT - see [LICENSE](LICENSE).

---

## По-русски

Удушение и перелом шеи. Схвати врага **за шею** и держи: он хватается, бьётся в конвульсиях,
теряет равновесие, хрипит и умирает примерно за 8 секунд (12 HP/с). Нажми **B** во время
удержания шеи - она хрустнет штатной системой переломов. Отпустил - урон сразу прекращается.
За голову и грудь не душит, только за шею.
