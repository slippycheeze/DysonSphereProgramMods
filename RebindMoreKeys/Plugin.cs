using BepInEx.Logging;
using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RebindMoreKeys
{
    public static class PluginInfo {
        public const string GUID    = "net.rimspace.RebindMoreKeys";
        public const string NAME    = "RebindMoreKeys";
        public const string VERSION = "1.0.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
    public class Plugin : BaseUnityPlugin {
        internal static ManualLogSource Log;

        private void Awake() {
            Plugin.Log = base.Logger;

            // At this point the DSPGame instance is not created, so we hook their Awake()
            Logger.LogInfo("Harmony.CreateAndPatchAll: calling on current assembly");
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo("Harmony.CreateAndPatchAll: completed");
        }
    }

    [HarmonyPatch(typeof(DSPGame), nameof(DSPGame.Awake))]
    internal class DSPGame_Awake_Patch {
        private static void Prefix(DSPGame __instance) {
            Plugin.Log.LogInfo($"DSPGame.Awake: KeyConfig scan...");
            for (int counter = 0; counter < __instance.m_key.builtinKeys.Length; counter++) {
                if (__instance.m_key.builtinKeys[counter].key._keyCode == UnityEngine.KeyCode.E) {
                    bool original = __instance.m_key.builtinKeys[counter].canOverride;
                    __instance.m_key.builtinKeys[counter].canOverride = true;
                    Plugin.Log.LogInfo($"BuiltinKey(id={__instance.m_key.builtinKeys[counter].id}) for `E` override flag {original} => true");
                }
            }
            Plugin.Log.LogInfo($"DSPGame.Awake: done");
        }
    }

    [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnUpdate))]
    internal class UIGame__OnUpdate_Patch_Hardcoded_KeyBinds {
        // This is a special horror: a huge function that calls a million things ... and has
        // a hard-coded check for the "E" keybind in the middle, instead of delegating to the
        // VFInput class.
        //
        // This patch rewrites that (ick!) to use the VFInput keybind check, which makes the
        // override system work correctly.
        //
        // Looks like this is the *one* place in that entire method that handles the logic this way,
        // though I suspect there are others to come...

        // open-coded:
        // IL_0604: ldc.i4.s     101 // 0x65
        // IL_0606: call         bool [UnityEngine.CoreModule]UnityEngine.Input::GetKeyDown(valuetype [UnityEngine.CoreModule]UnityEngine.KeyCode)
        // IL_060b: brfalse.s    IL_0617

        // replacement:
        // IL_0ae1: call         bool VFInput::get__openMechaPanel()
        // IL_0ae6: brfalse.s    IL_0af2
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            Plugin.Log.LogInfo("Transpiler patching UIGame._OnUpdate() for the `E` keybind");
            CodeMatcher editor = new CodeMatcher(instructions);

            editor
                .MatchForward(
                    false,      // put cursor at start of match
                    new CodeMatch(i => CodeInstructionExtensions.LoadsConstant(i, UnityEngine.KeyCode.E)),
                    new CodeMatch(
                        OpCodes.Call,
                        AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKeyDown))
                    )
                )
                .RemoveInstruction()  // remove the now-useless LDC
                .Set(
                    OpCodes.Call,
                    AccessTools.PropertyGetter(typeof(VFInput), nameof(VFInput._closePanelE))
                )
                .InstructionEnumeration();

            // log for debugging, and nothing else
            Plugin.Log.LogInfo(editor.Advance(-5).Instructions(15));

            return instructions;
        }
    }
}
