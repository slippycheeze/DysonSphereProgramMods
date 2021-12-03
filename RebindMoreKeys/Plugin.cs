using BepInEx.Logging;
using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RebindMoreKeys
{

    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin {
        public const string GUID    = "net.rimspace.RebindMoreKeys";
        public const string NAME    = "RebindMoreKeys";
        public const string VERSION = "1.0.0";

        internal static ManualLogSource Log;

        private static Harmony _harmony;

        private void Awake() {
            Plugin.Log = base.Logger;

            Logger.LogInfo($"{NAME}: Patching UnlockInventoryBinding");
            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), GUID);
        }

        private void OnDestroy() {
            Logger.LogInfo($"{NAME}: Unpatching in OnDestroy");
            _harmony?.UnpatchSelf();
        }
    }

    [HarmonyPatch]
    internal class UnlockInventoryBinding {
        // translations for my own lack of the native language.
        private const int OpenInventoryID = BuiltinKey.打开物品清单;
        private const int InventoryClosePanelsID = BuiltinKey.关闭面板0;

        // The UIGame._OnUpdate method requires the same keybind for both "inventory" and "inventory
        // closes panels", which sucks.  So we *only* allow rebinding of the "inventory" panel here;
        // it'll make the keybind config screen wrong, but at least it'll make it practical to
        // rebind the inventory key...
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DSPGame), nameof(DSPGame.Awake))]
        private static void adjust_builtinkey_inventory_canoverride(DSPGame __instance) {
            for (int index = 0; index < __instance.m_key.builtinKeys.Length; index++) {
                BuiltinKey keybind = __instance.m_key.builtinKeys[index];
                if (keybind.id == OpenInventoryID) {  // Open Inventory constant
                    Plugin.Log.LogInfo(
                        $"builtinKeys: canOverride inventorykeybind ({keybind.id}/{keybind.name}) default `{keybind.key}`"
                    );
                    __instance.m_key.builtinKeys[index].canOverride = true;
                };
            }
        }

        [HarmonyPatch(typeof(UIKeyEntry), nameof(UIKeyEntry.CheckKeyCanOverrided))]
        private static class UIKeyEntry_CheckKeyCanOverrided_FixInventoryClosePanels {
            private struct SavedBindings {
                public bool updated;
                public CombineKey? builtin;
                public CombineKey? option;
            }

            private static void Prefix(CombineKey[] overrideKeys, out SavedBindings __state) {
                __state = new SavedBindings{ updated = true };

                int slot = System.Array.FindIndex(DSPGame.key.builtinKeys, key => key.id == InventoryClosePanelsID);
                if (slot >= 0) {
                    Plugin.Log.LogInfo("saving builtin inv-close-panel bind");
                    __state.builtin = DSPGame.key.builtinKeys[slot].key;
                    DSPGame.key.builtinKeys[slot].key.SetNone();
                }
                if (!overrideKeys[InventoryClosePanelsID].IsNull()) {
                    Plugin.Log.LogInfo("saving override inv-close-panel bind");
                    __state.option = overrideKeys[InventoryClosePanelsID];
                    overrideKeys[InventoryClosePanelsID].SetNone();
                }
            }

            private static void Postfix(CombineKey[] overrideKeys, ref SavedBindings __state) {
                if (__state.updated) {
                    if (__state.builtin.HasValue) {
                        int slot = Array.FindIndex(DSPGame.key.builtinKeys, key => key.id == InventoryClosePanelsID);
                        if (slot >= 0)
                            DSPGame.key.builtinKeys[slot].key = __state.builtin.Value;
                    }
                    if (__state.option.HasValue) {
                        overrideKeys[InventoryClosePanelsID] = __state.option.Value;
                    }
                }
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnUpdate))]
        private static IEnumerable<CodeInstruction> vfinput_not_hardcoded_keybinds(IEnumerable<CodeInstruction> instructions) {
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
            Plugin.Log.LogInfo("Rewriting UIGame._OnUpdate() to remove hardcoded inventory keybind `E`");
            return new CodeMatcher(instructions)
                .MatchForward(
                    false,      // put cursor at start of match
                    new CodeMatch(i => i.LoadsConstant(UnityEngine.KeyCode.E)),
                    new CodeMatch(i => i.Calls(AccessTools.Method(
                                                   typeof(UnityEngine.Input),
                                                   nameof(UnityEngine.Input.GetKeyDown),
                                                   new System.Type[]{ typeof(UnityEngine.KeyCode) }
                                               ))
                    )
                )
                .RemoveInstruction()  // remove the now-useless LDC
                .Set(
                    OpCodes.Call,
                    AccessTools.PropertyGetter(typeof(VFInput), nameof(VFInput._openInventoryPanel))
                )
                .InstructionEnumeration();
        }
    }
}
