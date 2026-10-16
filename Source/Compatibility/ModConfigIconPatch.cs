using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RatkinDiningEtiquette
{
    // The vanilla configuration list draws only a content-source marker.
    // Give our own entry a separate icon slot, keeping that marker and the
    // vanilla row's selection, sorting, warnings and preview behavior.
    [HarmonyPatch(typeof(Page_ModsConfig), "DoModRow")]
    internal static class Page_ModsConfig_DiningIcon
    {
        private const string PackageId = "lenghuan.ratkindiningetiquette";
        private static readonly ConstructorInfo CopyRect = AccessTools.Constructor(typeof(Rect), new[] { typeof(Rect) });
        private static readonly MethodInfo NameRect = AccessTools.Method(typeof(Page_ModsConfig_DiningIcon), nameof(InitializeNameRect));

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> original = instructions.ToList();
            if (CopyRect == null || NameRect == null || original.Count(i => i.opcode == OpCodes.Call && Equals(i.operand, CopyRect)) != 1)
            {
                Log.Warning("[This Is Your Table!!] Configuration-list icon hook unavailable; using vanilla rows.");
                return original;
            }

            List<CodeInstruction> patched = new List<CodeInstruction>(original.Count + 1);
            foreach (CodeInstruction instruction in original)
            {
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, CopyRect))
                {
                    // Stack contains the destination Rect reference and row
                    // Rect for the struct constructor. Arg 2 is its mod.
                    CodeInstruction modArgument = new CodeInstruction(OpCodes.Ldarg_2);
                    modArgument.labels.AddRange(instruction.labels);
                    modArgument.blocks.AddRange(instruction.blocks);
                    patched.Add(modArgument);
                    patched.Add(new CodeInstruction(OpCodes.Call, NameRect));
                }
                else patched.Add(instruction);
            }
            return patched;
        }

        private static void InitializeNameRect(ref Rect nameRect, Rect row, ModMetaData mod)
        {
            nameRect = new Rect(row);
            if (mod == null || !mod.SamePackageId(PackageId, true)) return;
            Texture2D icon = mod.Icon;
            if (icon == null) return;

            float slotWidth = row.height;
            Rect iconRect = new Rect(row.x + 28f + 2f, row.y + 2f, slotWidth - 4f, slotWidth - 4f);
            Color previousColor = GUI.color;
            try
            {
                GUI.color = mod.Active ? Color.white : Color.grey;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = previousColor; }
            nameRect.xMin += slotWidth;
        }
    }
}
