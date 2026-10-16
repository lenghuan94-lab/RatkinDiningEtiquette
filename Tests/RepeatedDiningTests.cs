using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static Thought_Memory_RatDining ChairMeal(Pawn pawn, Building chair)
    {
        pawn.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, new Thing(), chair, new IntVec3(19)));
        RatDiningThoughts.ApplyChair(pawn, chair);
        return (Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RatDiningThoughts.ForChair(chair));
    }

    private static float OwnDiningMood(Pawn pawn) => pawn.needs.mood.thoughts.memories.Memories
        .Where(m => RatDiningThoughts.IsOwnChairThought(m.def) || m.def == RDE_DefOf.RDE_ToddlerNoDiningHelp)
        .Sum(m => m.def.stages[m.stage]);

    private static void RepeatedDiningTests()
    {
        Test("all 51 active stages have nonzero mood within the requested limits", () =>
        {
            var doc = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ThoughtDefs.xml"));
            var stages = doc.Root.Elements().SelectMany(e => e.Element("stages").Elements()).ToList();
            Check(stages.Count == 51, "48 age/seat texts plus three help complaints");
            foreach (var stage in stages)
            {
                float value = float.Parse(stage.Element("baseMoodEffect").Value, System.Globalization.CultureInfo.InvariantCulture);
                Check(value != 0 && value >= -3 && value <= 4, "bounded, nonzero active mood");
            }
            Check(doc.Root.Elements().All(e => e.Element("stackLimit").Value == "1"), "physical memory stacking cannot multiply totals");
        });
        Test("every job and active thought stage has Chinese label and description", () =>
        {
            var jobs = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "JobDefs.xml"));
            var chineseJobs = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ChineseJobDefs.xml"));
            Check(chineseJobs.Root.Elements().Count() == jobs.Root.Elements().Count(), "all runtime jobs translated");
            foreach (var job in jobs.Root.Elements())
                Check(chineseJobs.Root.Element(job.Element("defName").Value + ".reportString") != null, "translated job");
            var thoughts = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ThoughtDefs.xml"));
            var chineseThoughts = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ChineseThoughtDefs.xml"));
            var labels = thoughts.Root.Elements().SelectMany(t => t.Element("stages").Elements().Select((s, i) =>
            {
                string key = t.Element("defName").Value + ".stages." + i;
                Check(chineseThoughts.Root.Element(key + ".description") != null, "Chinese description");
                return chineseThoughts.Root.Element(key + ".label")?.Value;
            })).ToList();
            Check(labels.All(l => !string.IsNullOrEmpty(l)) && labels.Distinct().Count() == 51, "51 different Chinese titles");
        });
        Test("repeat meals upgrade total effect and stop at three for every age and chair", () =>
        {
            float[] years = { 2, 8, 16, 25 };
            float[][] ordinary = { new float[] { 1, 2, 3 }, new float[] { 1, 2, 3 }, new float[] { -1, -2, -3 }, new float[] { -1, -2, -3 } };
            float[][] small = { new float[] { 2, 3, 4 }, new float[] { 2, 3, 4 }, new float[] { 1, 2, 3 }, new float[] { 1, 2, 3 } };
            for (int band = 0; band < 4; band++) foreach (bool ratkin in new[] { false, true })
            {
                var map = new Map(); var pawn = new Pawn { Map = map }; pawn.ageTracker.AgeBiologicalYearsFloat = years[band];
                var chair = Furniture(map, 20, true, ratkin);
                for (int meal = 0; meal < 9; meal++)
                {
                    var memory = ChairMeal(pawn, chair); int layer = Math.Min(3, meal + 1);
                    Check(memory.repetitions == layer && memory.stage == band * 3 + layer - 1, "right age/layer text");
                    Check(OwnDiningMood(pawn) == (ratkin ? small : ordinary)[band][layer - 1], "capped total, not sum of stages");
                    Check(pawn.needs.mood.thoughts.memories.Memories.Count == 1, "one displayed total");
                }
            }
        });
        Test("the same completed job cannot count as two meals", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var chair = Furniture(map, 20, true, true);
            var first = ChairMeal(pawn, chair); first.age = 123;
            RatDiningThoughts.ApplyChair(pawn, chair);
            Check(first == pawn.needs.mood.thoughts.memories.Memories.Single() && first.repetitions == 1 && first.age == 123, "duplicate callback ignored");
            Check(ChairMeal(pawn, chair).repetitions == 2, "another job is a new meal");
        });
        Test("changing chair type, age band or finishing a normal meal resets repetition", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var small = Furniture(map, 20, true, true); var ordinary = Furniture(map, 30, true, false);
            ChairMeal(pawn, small); ChairMeal(pawn, small);
            Check(ChairMeal(pawn, ordinary).repetitions == 1, "switch type");
            ChairMeal(pawn, ordinary); pawn.ageTracker.AgeBiologicalYearsFloat = 14;
            Check(ChairMeal(pawn, ordinary).repetitions == 1, "birthday band");
            var food = new Thing(); pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, food));
            Thing_Ingested_ClearPreviousChairThought.Postfix(food, pawn, .5f);
            Check(ChairMeal(pawn, ordinary).repetitions == 1, "normal meal");
        });
        Test("expired memory never grants an old repeat bonus", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var chair = Furniture(map, 20, true, true);
            ChairMeal(pawn, chair); ChairMeal(pawn, chair); var last = ChairMeal(pawn, chair); last.age = 60001;
            Check(ChairMeal(pawn, chair).repetitions == 1 && OwnDiningMood(pawn) == 2, "expired layer resets");
        });
        Test("repeat counts, age and completed-job identity survive save fields and load reconciliation", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; pawn.ageTracker.AgeBiologicalYearsFloat = 25;
            var chair = Furniture(map, 20, true, true); ChairMeal(pawn, chair); ChairMeal(pawn, chair); var old = ChairMeal(pawn, chair); old.age = 4321;
            Scribe.mode = LoadSaveMode.Saving; old.ExposeData();
            var restored = (Thought_Memory_RatDining)ThoughtMaker.MakeThought(old.def, 0);
            Scribe.mode = LoadSaveMode.LoadingVars; restored.ExposeData();
            var memories = pawn.needs.mood.thoughts.memories; memories.RemoveMemory(old); memories.TryGainMemory(restored);
            Scribe.mode = LoadSaveMode.PostLoadInit; Pawn_ExposeData_RetireTableThoughts.Postfix(pawn);
            var current = (Thought_Memory_RatDining)memories.Memories.Single();
            Check(current.repetitions == 3 && current.stage == 11 && current.age == 4321 && current.lastMealJobId == old.lastMealJobId, "saved logical stacking state");
            current.TryMergeWithExistingMemory(out bool loadBubble); Check(!loadBubble, "load reconciliation does not replay a new mood bubble");
            Scribe.mode = LoadSaveMode.Inactive; RatDiningThoughts.ApplyChair(pawn, chair);
            Check(current == memories.Memories.Single() && current.age == 4321, "loaded same job remains deduplicated");
        });
        Test("previous base-class memories migrate as one event without renewing lifetime", () =>
        {
            var pawn = new Pawn(); pawn.ageTracker.AgeBiologicalYearsFloat = 16;
            pawn.needs.mood.thoughts.memories.TryGainMemory(new Thought_Memory { def = RDE_DefOf.RDE_UsedChairAsTable, stage = 2, age = 50000 });
            RatDiningThoughts.RefreshLoadedMemories(pawn);
            var memory = (Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.Memories.Single();
            Check(memory.repetitions == 1 && memory.stage == 6 && memory.age == 50000, "old four-stage schema migrated");
        });
        Test("bad saved repeat counts clamp safely and expired memories stay expired", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var chair = Furniture(map, 20, true, true);
            var memory = ChairMeal(pawn, chair); memory.repetitions = int.MaxValue;
            Check(ChairMeal(pawn, chair).repetitions == 3 && OwnDiningMood(pawn) == 4, "increment cannot overflow");
            memory = (Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.Memories.Single(); memory.repetitions = int.MinValue;
            RatDiningThoughts.RefreshLoadedMemories(pawn);
            Check(((Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.Memories.Single()).repetitions == 1, "negative corrupt count");
            pawn.needs.mood.thoughts.memories.Memories.Single().age = 60001; RatDiningThoughts.RefreshLoadedMemories(pawn);
            Check(pawn.needs.mood.thoughts.memories.Memories.Count == 0, "load does not resurrect expired reward");
        });
        Test("help timeout repeats cap at minus three and success resolves the complaint", () =>
        {
            var pawn = new Pawn();
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                RatDiningThoughts.ApplyHelpTimeout(pawn);
                var memory = (Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.Memories.Single();
                Check(memory.repetitions == Math.Min(3, attempt) && OwnDiningMood(pawn) == -Math.Min(3, attempt), "bounded help complaint");
            }
            RatDiningThoughts.ApplyChair(pawn, Furniture(new Map(), 20, true, true));
            Check(pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RDE_DefOf.RDE_ToddlerNoDiningHelp) == null, "help complaint resolved");
        });
        Test("mixed own events never combine rewards or penalties past the limits", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var ordinary = Furniture(map, 20, true, false); var small = Furniture(map, 30, true, true);
            var external = new ThoughtDef { defName = "RatkinTableRatkinSeatAge18Plus" }; pawn.needs.mood.thoughts.memories.TryGainMemory(external);
            for (int i = 0; i < 60; i++)
            {
                pawn.ageTracker.AgeBiologicalYearsFloat = i % 2 == 0 ? 2 : 25;
                if (i % 5 == 0) RatDiningThoughts.ApplyHelpTimeout(pawn); else ChairMeal(pawn, i % 3 == 0 ? ordinary : small);
                Check(OwnDiningMood(pawn) >= -3 && OwnDiningMood(pawn) <= 4 && OwnDiningMood(pawn) != 0, "aggregate own modifier bounded and nonzero");
                Check(pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(external) != null, "other mod untouched");
            }
        });
    }
}
