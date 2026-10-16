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
    private static XDocument LoadSpeechLanguage(string file = "ChineseKeyed.xml")
    {
        var doc = XDocument.Load(Path.Combine(AppContext.BaseDirectory, file));
        TranslationExtensions.Translations.Clear();
        foreach (var element in doc.Root.Elements())
            TranslationExtensions.Translations[element.Name.LocalName] = element.Value;
        return doc;
    }

    private static void SpeechTests()
    {
        Test("request speech uses actual Talking capacity at both exact boundaries", () =>
        {
            var pawn = new Pawn();
            float[] levels = { 0, .3999f, .4f, .7999f, .8f, 1, 1.5f };
            string[] bands = { "Low", "Low", "Medium", "Medium", "High", "High", "High" };
            for (int i = 0; i < levels.Length; i++)
            {
                pawn.health.capacities.TalkingLevel = levels[i];
                Check(RatToddlerSpeech.RequestKey(pawn) == "RDE_RequestMote_" + bands[i], "boundary " + levels[i]);
            }
        });
        Test("a toddler's changed Talking capacity changes its next localized request", () =>
        {
            var language = LoadSpeechLanguage(); var pawn = new Pawn();
            foreach (var pair in new[] { (level: .2f, band: "Low"), (level: .6f, band: "Medium"), (level: 1f, band: "High") })
            {
                pawn.health.capacities.TalkingLevel = pair.level;
                Check(RatToddlerSpeech.RequestText(pawn) == language.Root.Element("RDE_RequestMote_" + pair.band).Value, "current speech, not cached speech");
            }
            pawn.ageTracker.AgeBiologicalYearsFloat = 3.99f;
            Check(RatToddlerSpeech.RequestKey(pawn).EndsWith("High"), "age does not substitute for Talking");
            LoadSpeechLanguage("EnglishKeyed.xml");
            Check(RatToddlerSpeech.RequestText(pawn) == "Lift me onto the chair, please! I want dinner!", "language switch stays localized");
        });
        Test("missing health and invalid Talking values safely select low speech", () =>
        {
            Check(RatToddlerSpeech.RequestKey(null).EndsWith("Low"), "missing pawn");
            var pawn = new Pawn { health = null };
            Check(RatToddlerSpeech.RequestKey(pawn).EndsWith("Low"), "missing health");
            pawn.health = new Pawn_HealthTracker { capacities = null };
            Check(RatToddlerSpeech.RequestKey(pawn).EndsWith("Low"), "missing capacities");
            pawn.health = new Pawn_HealthTracker();
            foreach (float level in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                pawn.health.capacities.TalkingLevel = level;
                Check(RatToddlerSpeech.RequestKey(pawn).EndsWith("Low"), "invalid capacity");
            }
        });
        Test("the thought's upper BabyTalk and lower Description are separate channels", () =>
        {
            var language = LoadSpeechLanguage();
            var chinese = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ChineseThoughtDefs.xml"));
            var defs = new[] { RDE_DefOf.RDE_UsedChairAsTable, RDE_DefOf.RDE_RatkinChairAsTable, RDE_DefOf.RDE_ToddlerNoDiningHelp };
            string[] groups = { "OrdinaryChair", "RatkinChair", "NoHelp" };
            for (int group = 0; group < defs.Length; group++) for (int stage = 0; stage < 3; stage++)
            {
                var thought = (Thought_Memory_RatDining)ThoughtMaker.MakeThought(defs[group], stage);
                string expected = language.Root.Element("RDE_BabyTalk_" + groups[group] + "_" + stage).Value;
                Check(thought.BabyTalk == expected, "no vanilla second babble conversion");
                Check(thought.Description != expected && thought.Description.Contains(' '), "readable English translation");
                string translated = chinese.Root.Element(defs[group].defName + ".stages." + stage + ".description").Value;
                Check(translated.Any(c => c >= '\u4e00' && c <= '\u9fff') && !translated.Contains("Nyaa") && !translated.Contains("Hup"), "readable Chinese translation");
            }
        });
        Test("BabyTalk override leaves older stages and another mod's defs to vanilla", () =>
        {
            LoadSpeechLanguage();
            foreach (var def in new[] { RDE_DefOf.RDE_UsedChairAsTable, RDE_DefOf.RDE_RatkinChairAsTable })
                for (int stage = 3; stage < 12; stage++)
                    Check(ThoughtMaker.MakeThought(def, stage).BabyTalk == "vanilla babble", "older stage untouched");
            var external = new ThoughtDef { defName = "ToddlerRatkinCannotReachTable", thoughtClass = typeof(Thought_Memory_RatDining) };
            Check(ThoughtMaker.MakeThought(external, 0).BabyTalk == "vanilla babble", "other mod untouched");
        });
        Test("preparing and reopened waiting each emit current speech once, never each tick", () =>
        {
            LoadSpeechLanguage(); RatDiningMod.Settings.showRequestMote = true;
            var f = new Fixture(); f.Child.health.capacities.TalkingLevel = .2f;
            var prep = Attach(new JobDriver_ToddlerPrepareDiningRequest(), f.Child);
            Run(prep, 0, 4);
            Check(MoteMaker.Count == 1 && MoteMaker.LastText == "Mabba... goo? Hup-hup!", "first request");
            f.Child.health.capacities.TalkingLevel = .6f;
            f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Request.food));
            var provider = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
            Run(provider, 0, 0);
            Attach(new JobDriver_ToddlerWaitForMeal(), f.Child);
            f.Helper.jobs.StartJob(JobMaker.MakeJob(new JobDef { defName = "InterruptProvider" }), JobCondition.InterruptForced);
            f.Tick(60);
            Check(f.Request.IsActive && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerWaitForDiningHelp, "real interrupted provider reopens help");
            var reopened = Attach(new JobDriver_ToddlerWaitForDiningHelp(), f.Child);
            Check(reopened.Count == 1, "saved wait toil indices unchanged");
            Run(reopened, 0, 0);
            Check(MoteMaker.Count == 2 && MoteMaker.LastText.Contains("抱抱"), "reopened request rechecks Talking");
            for (int tick = 0; tick < 60; tick++) reopened[0].tickIntervalAction(1);
            Check(MoteMaker.Count == 2, "no per-tick shouting");
        });
        Test("request motes respect the option and remain limited to eligible Ratkin toddlers", () =>
        {
            RatDiningMod.Settings.showRequestMote = false;
            LoadSpeechLanguage(); var pawn = new Pawn { Map = new Map() };
            RatToddlerSpeech.ShowRequest(pawn); Check(MoteMaker.Count == 0, "disabled option");
            RatDiningMod.Settings.showRequestMote = true;
            pawn.def = new ThingDef(); RatToddlerSpeech.ShowRequest(pawn); Check(MoteMaker.Count == 0, "other race");
            pawn.def = DefDatabase<ThingDef>.Ratkin; pawn.Toddler = false;
            RatToddlerSpeech.ShowRequest(pawn); Check(MoteMaker.Count == 0, "adult");
            pawn.Toddler = true; pawn.SelfFeeding = false;
            RatToddlerSpeech.ShowRequest(pawn); Check(MoteMaker.Count == 0, "cannot self-feed");
            pawn.SelfFeeding = true; pawn.Spawned = false;
            RatToddlerSpeech.ShowRequest(pawn); Check(MoteMaker.Count == 0, "unspawned");
        });
    }
}
