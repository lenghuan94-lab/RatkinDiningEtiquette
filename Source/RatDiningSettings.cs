using Verse;

namespace RatkinDiningEtiquette
{
    public sealed class RatDiningSettings : ModSettings
    {
        public bool enableChairTable = true;
        public bool enableToddlerHelp = true;
        public bool enableColonistToddlerHelp = true;
        public bool enableVisitorToddlerHelp;
        public bool enablePrisonerToddlerHelp;
        public bool enableSlaveToddlerHelp;
        public bool enableColonistSpecialSeats;
        public bool enableVisitorSpecialSeats;
        public bool enablePrisonerSpecialSeats = true;
        public bool enableSlaveSpecialSeats = true;
        public float chairSearchRadius = 32f;
        public float helperSearchRadius = 60f;
        public int normalWaitTicks = 1200;
        public int hungryWaitTicks = 450;
        public float criticalFoodLevel = 0.03f;
        public bool allowNonParents = true;
        public bool showRequestMote = true;
        public bool debugLogging;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableChairTable, "enableChairTable", true);
            Scribe_Values.Look(ref enableToddlerHelp, "enableToddlerHelp", true);
            Scribe_Values.Look(ref enableColonistToddlerHelp, "enableColonistToddlerHelp", true);
            Scribe_Values.Look(ref enableVisitorToddlerHelp, "enableVisitorToddlerHelp", false);
            Scribe_Values.Look(ref enablePrisonerToddlerHelp, "enablePrisonerToddlerHelp", false);
            Scribe_Values.Look(ref enableSlaveToddlerHelp, "enableSlaveToddlerHelp", false);
            Scribe_Values.Look(ref enableColonistSpecialSeats, "enableColonistSpecialSeats", false);
            Scribe_Values.Look(ref enableVisitorSpecialSeats, "enableVisitorSpecialSeats", false);
            Scribe_Values.Look(ref enablePrisonerSpecialSeats, "enablePrisonerSpecialSeats", true);
            Scribe_Values.Look(ref enableSlaveSpecialSeats, "enableSlaveSpecialSeats", true);
            Scribe_Values.Look(ref chairSearchRadius, "chairSearchRadius", 32f);
            Scribe_Values.Look(ref helperSearchRadius, "helperSearchRadius", 60f);
            Scribe_Values.Look(ref normalWaitTicks, "normalWaitTicks", 1200);
            Scribe_Values.Look(ref hungryWaitTicks, "hungryWaitTicks", 450);
            Scribe_Values.Look(ref criticalFoodLevel, "criticalFoodLevel", 0.03f);
            Scribe_Values.Look(ref allowNonParents, "allowNonParents", true);
            Scribe_Values.Look(ref showRequestMote, "showRequestMote", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
        }
    }

}
