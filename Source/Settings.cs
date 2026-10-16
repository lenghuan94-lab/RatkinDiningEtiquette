using UnityEngine;
using Verse;

namespace RatkinDiningEtiquette
{
    public sealed class RatDiningMod : Mod
    {
        public static RatDiningSettings Settings { get; private set; }
        private Vector2 settingsScrollPosition;
        private float settingsContentHeight = 800f;

        public RatDiningMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RatDiningSettings>();
        }

        public override string SettingsCategory() => "RDE_SettingsTitle".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(inRect.height, settingsContentHeight));
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);
            Listing_Standard list = new Listing_Standard();
            list.Begin(viewRect);
            list.CheckboxLabeled("RDE_EnableChairTable".Translate(), ref Settings.enableChairTable);
            list.Gap();
            list.Label("RDE_SpecialSeats".Translate());
            list.Label("RDE_SpecialSeatsTooltip".Translate());
            list.CheckboxLabeled("RDE_SpecialSeatsColonists".Translate(), ref Settings.enableColonistSpecialSeats);
            list.CheckboxLabeled("RDE_SpecialSeatsVisitors".Translate(), ref Settings.enableVisitorSpecialSeats);
            list.CheckboxLabeled("RDE_SpecialSeatsPrisoners".Translate(), ref Settings.enablePrisonerSpecialSeats);
            list.CheckboxLabeled("RDE_SpecialSeatsSlaves".Translate(), ref Settings.enableSlaveSpecialSeats);
            list.Gap();
            list.CheckboxLabeled("RDE_EnableToddlerHelp".Translate(), ref Settings.enableToddlerHelp,
                "RDE_EnableToddlerHelpTooltip".Translate());
            if (Settings.enableToddlerHelp)
            {
                list.CheckboxLabeled("RDE_ToddlerHelpColonists".Translate(), ref Settings.enableColonistToddlerHelp);
                list.CheckboxLabeled("RDE_ToddlerHelpVisitors".Translate(), ref Settings.enableVisitorToddlerHelp);
                list.CheckboxLabeled("RDE_ToddlerHelpPrisoners".Translate(), ref Settings.enablePrisonerToddlerHelp);
                list.CheckboxLabeled("RDE_ToddlerHelpSlaves".Translate(), ref Settings.enableSlaveToddlerHelp);
            }
            list.Label("RDE_ChairRadius".Translate(Settings.chairSearchRadius.ToString("0")));
            Settings.chairSearchRadius = list.Slider(Settings.chairSearchRadius, 8f, 64f);
            list.Label("RDE_HelperRadius".Translate(Settings.helperSearchRadius.ToString("0")));
            Settings.helperSearchRadius = list.Slider(Settings.helperSearchRadius, 16f, 100f);
            list.Label("RDE_NormalWait".Translate((Settings.normalWaitTicks / 60f).ToString("0")));
            Settings.normalWaitTicks = (int)list.Slider(Settings.normalWaitTicks, 300, 3600);
            list.Label("RDE_HungryWait".Translate((Settings.hungryWaitTicks / 60f).ToString("0")));
            Settings.hungryWaitTicks = (int)list.Slider(Settings.hungryWaitTicks, 120, 1200);
            list.Label("RDE_CriticalFood".Translate((Settings.criticalFoodLevel * 100f).ToString("0")));
            Settings.criticalFoodLevel = list.Slider(Settings.criticalFoodLevel, 0.01f, 0.15f);
            list.CheckboxLabeled("RDE_AllowNonParents".Translate(), ref Settings.allowNonParents);
            list.CheckboxLabeled("RDE_ShowMote".Translate(), ref Settings.showRequestMote);
            list.CheckboxLabeled("RDE_Debug".Translate(), ref Settings.debugLogging);
            settingsContentHeight = list.CurHeight + 10f;
            list.End();
            Widgets.EndScrollView();
        }
    }
}
