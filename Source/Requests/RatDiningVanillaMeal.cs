using Verse;

namespace RatkinDiningEtiquette
{
    // An indexed ingestion selected by this mod; it owns no furniture claims.
    public sealed class RatDiningVanillaMeal : IExposable
    {
        public Pawn pawn;
        public int jobId;
        public RatDiningCustodySnapshot custody = new RatDiningCustodySnapshot();
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref jobId, "jobId");
            custody.ExposeData();
        }
    }
}
