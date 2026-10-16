// These doubles model job handoff, stack splitting and cleanup only. They are not
// a substitute for RimWorld's pathfinding, reservation, rendering or save system.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } public HarmonyPatch(Type type, string method, Type[] arguments) { } }
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyAfter : Attribute
    { public HarmonyAfter(params string[] ids) { } }
    public static class AccessTools
    { public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); }
}

namespace Verse
{
    public enum Danger { Unspecified, None, Some, Deadly }
    public enum ThingPlaceMode { Near, Direct }
    public enum LoadSaveMode { Inactive, PostLoadInit, Saving, LoadingVars }
    public enum LookMode { Deep, Value }
    public enum DevelopmentalStage { Baby, Adult }
    public static class StageExtensions { public static bool Baby(this DevelopmentalStage stage) => stage == DevelopmentalStage.Baby; }
    public interface IExposable { void ExposeData(); }
    public class ModSettings : IExposable { public virtual void ExposeData() { } }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values
    {
        public static readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public static void Look<T>(ref T value, string key, T defaultValue = default(T))
        {
            if (Scribe.mode == LoadSaveMode.Saving) Values[key] = value;
            else if (Scribe.mode == LoadSaveMode.LoadingVars) value = Values.TryGetValue(key, out object saved) ? (T)saved : defaultValue;
        }
    }
    // Identity-only round trip; this does not implement RimWorld cross-reference
    // resolution or loading a real save file.
    public static class Scribe_References
    { public static void Look<T>(ref T value, string key) { Scribe_Values.Look(ref value, key); } }
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> value, string key, LookMode mode) { }
        public static void Look<K,V>(ref Dictionary<K,V> value, string key, LookMode keys, LookMode values) { }
    }
    public struct IntVec3
    {
        public int x;
        public IntVec3(int x) { this.x = x; }
        public static IntVec3 Invalid => new IntVec3(-1);
        public bool IsValid => x >= 0;
        public float AngleFlat => 0;
        public bool InBounds(Map map) => map != null && IsValid && x < map.CellCount;
        public Room GetRoom(Map map) => map.Rooms.TryGetValue(x, out Room room) ? room : null;
        public bool IsForbidden(Pawn pawn) => false;
        public bool HasEatSurface(Map map) => GetEdifice(map)?.def.surfaceType == SurfaceType.Eat;
        public bool Fogged(Map map) => map.FoggedCells.Contains(x);
        public Building GetEdifice(Map map) { if (!InBounds(map)) throw new IndexOutOfRangeException(); IntVec3 cell = this; return map.Buildings.FirstOrDefault(b => b.Spawned && !b.Destroyed && b.Position == cell); }
        public bool Standable(Map map) => InBounds(map) && !map.BlockedCells.Contains(x);
        public bool InAllowedArea(Pawn pawn) => !pawn.Restricted;
        public Danger GetDangerFor(Pawn pawn, Map map) => pawn.Danger;
        public int DistanceToSquared(IntVec3 other) => (x - other.x) * (x - other.x);
        public float DistanceTo(IntVec3 other) => Math.Abs(x - other.x);
        public static IntVec3 operator -(IntVec3 a, IntVec3 b) => new IntVec3(a.x - b.x);
        public static IntVec3 operator +(IntVec3 a, IntVec3 b) => new IntVec3(a.x + b.x);
        public static bool operator ==(IntVec3 a, IntVec3 b) => a.x == b.x;
        public static bool operator !=(IntVec3 a, IntVec3 b) => a.x != b.x;
        public override bool Equals(object o) => o is IntVec3 other && this == other;
        public override int GetHashCode() => x;
    }
    public struct Rot4 { private int index; public Rot4(int i) { index = i; } public IntVec3 FacingCell => GenAdj.CardinalDirections[index]; public static Rot4 FromAngleFlat(float angle) => new Rot4(); public static Rot4 FromIntVec3(IntVec3 cell) => new Rot4(); }
    public static class GenAdj { public static readonly IntVec3[] CardinalDirections = { new IntVec3(1), new IntVec3(-1), new IntVec3(100), new IntVec3(-100) }; }
    public class DefModExtension { }
    public enum SurfaceType { None, Eat }
    public class BuildingProperties { public bool isSittable; }
    public class IngestibleProperties { public bool useEatingSpeedStat = true, babiesCanIngest = true; public float nutrition = .1f; }
    public class ModContentPack { public string PackageIdPlayerFacing; }
    public static class ModsConfig { public static bool TableMannersActive; public static bool IsActive(string package) => TableMannersActive; }
    public class ThingDef
    {
        public Type thingClass;
        public string defName;
        public SurfaceType surfaceType;
        public BuildingProperties building;
        public IngestibleProperties ingestible = new IngestibleProperties();
        public ModContentPack modContentPack;
        public DefModExtension Extension;
        public T GetModExtension<T>() where T : DefModExtension => Extension as T;
        public bool IsNutritionGivingIngestible = true;
        public bool ExtendedRatkin;
        public bool rotatable;
        public bool HasModExtension<T>() => ExtendedRatkin;
    }
    public static class DefDatabase<T> where T : new()
    { public static readonly T Ratkin = new T(); public static T GetNamedSilentFail(string name) => Ratkin; }
    public class Thing
    {
        private static int nextId;
        public int thingIDNumber = ++nextId;
        public int stackCount = 1;
        public bool Destroyed, Spawned = true, IngestibleNow = true;
        public ThingDef def = new ThingDef();
        public Map Map;
        public virtual Map MapHeld => HeldBy?.MapHeld ?? Map;
        public IntVec3 Position = new IntVec3(10);
        public Rot4 Rotation;
        public Pawn HeldBy;
        public IntVec3 PositionHeld => HeldBy?.PositionHeld ?? Position;
        public bool Forbidden;
        public RimWorld.Faction Faction = RimWorld.Faction.OfPlayer;
        public bool Improper, Hostile;
        public readonly HashSet<Pawn> ImproperTo = new HashSet<Pawn>();
        public bool IsSociallyProper(Pawn pawn, bool prisonerAllowed = false, bool animalsCare = true)
            => !Improper && !ImproperTo.Contains(pawn);
        public bool HostileTo(Pawn pawn) => Hostile;
        public IEnumerable<IntVec3> OccupiedRect() { yield return Position; }
        public float Ingested(Pawn pawn, float nutrition) => nutrition;
        public bool IsForbidden(Pawn pawn) => Forbidden;
    }
    public class Building : Thing
    { public Building() { def.building = new BuildingProperties { isSittable = true }; } public bool Burning; public bool IsBurning() => Burning; }
    public class Corpse : Thing { public int BodyParts = 5; }
    public class Pawn : Thing
    {
        public RaceProperties RaceProps = new RaceProperties();
        public Pawn_AgeTracker ageTracker = new Pawn_AgeTracker();
        public void ExposeData() { }
        public bool Dead, Downed, InMentalState, Drafted, Restricted, MedicalRest, UnsafeTemperature;
        public bool Toddler = true, SelfFeeding = true;
        public bool CanReserveTargets = true, CanReachTargets = true;
        public bool IsColonist = true;
        public bool IsPrisonerOfColony, IsSlaveOfColony, FormingCaravan;
        public bool IsPrisoner => IsPrisonerOfColony;
        public bool IsSlave => IsSlaveOfColony;
        public Verse.AI.Group.Lord Lord;
        public Pawn_MindState mindState = new Pawn_MindState();
        public RimWorld.Faction HostFaction;
        public Pawn_GuestTracker guest;
        public readonly HashSet<RimWorld.WorkTypeDef> DisabledWork = new HashSet<RimWorld.WorkTypeDef>();
        public readonly HashSet<ThingDef> DisallowedFood = new HashSet<ThingDef>();
        public bool WorkTypeIsDisabled(RimWorld.WorkTypeDef work) => DisabledWork.Contains(work);
        public float EatingSpeed = 1f;
        public float GetStatValue(object stat) => EatingSpeed;
        public DevelopmentalStage DevelopmentalStage = DevelopmentalStage.Adult;
        public Danger Danger = Danger.None;
        public Pawn_NeedsTracker needs = new Pawn_NeedsTracker();
        public Pawn_RelationsTracker relations = new Pawn_RelationsTracker();
        public Pawn_HealthTracker health = new Pawn_HealthTracker();
        public Pawn_WorkSettings workSettings = new Pawn_WorkSettings();
        public Pawn_InventoryTracker inventory = new Pawn_InventoryTracker();
        public Pawn_CarryTracker carryTracker;
        public Verse.AI.Pawn_JobTracker jobs;
        public Verse.AI.Pawn_PathFollower pather;
        public object DrawPos;
        public Pawn() { guest = new Pawn_GuestTracker(this); carryTracker = new Pawn_CarryTracker(this); jobs = new Verse.AI.Pawn_JobTracker(this); pather = new Verse.AI.Pawn_PathFollower(this); def = DefDatabase<ThingDef>.Ratkin; }
        public Verse.AI.Job CurJob => jobs.curJob;
        public RimWorld.JobDef CurJobDef => CurJob?.def;
    }
    public class Pawn_AgeTracker { public float AgeBiologicalYearsFloat = 2f; }
    public class Pawn_NeedsTracker { public Need_Food food = new Need_Food(); public Need_Mood mood = new Need_Mood(); public Need_Food rest = new Need_Food(); }
    public class RaceProperties { public float FoodLevelPercentageWantEat = .3f; public bool Humanlike = true; }
    public class Pawn_MindState { public Verse.AI.PawnDuty duty; }
    public class Need_Food { public float CurLevelPercentage = 0.5f; public float MaxLevel = .8f; public float NutritionWanted => (1f - CurLevelPercentage) * MaxLevel; }
    public class Pawn_GuestTracker
    {
        private readonly Pawn pawn;
        public Pawn_GuestTracker(Pawn pawn) { this.pawn = pawn; }
        private bool prisonerSecure = true;
        private bool slaveSecure = true;
        public bool Released, CanBeBroughtFood = true;
        public bool SlaveIsSecure { get => slaveSecure && !Released && !pawn.InMentalState
            && (!pawn.Spawned || pawn.CurJob?.exitMapOnArrival != true); set => slaveSecure = value; }
        // Native release marks Released before the guest status/faction ends.
        public bool PrisonerIsSecure { get => prisonerSecure && !Released; set => prisonerSecure = value; }
    }
    public class Need_Mood { public ThoughtTracker thoughts = new ThoughtTracker(); }
    public class ThoughtTracker { public RimWorld.MemoryThoughtHandler memories = new RimWorld.MemoryThoughtHandler(); }
    public class Pawn_HealthTracker { public Pawn_CapacitiesTracker capacities = new Pawn_CapacitiesTracker(); }
    public class Pawn_WorkSettings
    {
        public bool Initialized = true, Childcare = true, Warden = true;
        public bool WorkIsActive(object work)
        {
            if (!Initialized) throw new InvalidOperationException("Native WorkIsActive would log and initialize NPC work settings");
            return work == RimWorld.WorkTypeDefOf.Warden ? Warden : Childcare;
        }
    }
    public class Pawn_CapacitiesTracker
    {
        public float TalkingLevel = 1f;
        public float GetLevel(object capacity) => capacity == RimWorld.PawnCapacityDefOf.Talking ? TalkingLevel : 1f;
        public bool CapableOf(object capacity) => true;
    }
    public class Pawn_RelationsTracker
    {
        public readonly List<Pawn> Parents = new List<Pawn>();
        public object GetDirectRelation(object def, Pawn pawn) => Parents.Contains(pawn) ? new object() : null;
    }
    public class ThingOwner
    {
        private readonly List<Thing> items;
        private readonly Pawn_CarryTracker carrier;
        public bool TransferFails, DropFails;
        public ThingOwner(List<Thing> items) { this.items = items; }
        public ThingOwner(Pawn_CarryTracker carrier) { this.carrier = carrier; }
        public int TryTransferToContainer(Thing food, ThingOwner target, int count, out Thing transferred, bool merge = true)
        {
            transferred = null;
            if (TransferFails || target.TransferFails || (items != null ? !items.Contains(food) : carrier.CarriedThing != food)) return 0;
            if (target.carrier != null && target.carrier.CarriedThing != null) return 0;
            int take = Math.Min(count, food.stackCount);
            if (take < food.stackCount) { food.stackCount -= take; transferred = new Thing { Map = food.Map, Spawned = false, def = food.def, stackCount = take }; }
            else { transferred = food; if (items != null) items.Remove(food); else carrier.CarriedThing = null; }
            if (target.items != null) target.items.Add(transferred); else target.carrier.CarriedThing = transferred;
            transferred.Spawned = false;
            return take;
        }
        public bool TryDrop(Thing food, IntVec3 position, Map map, ThingPlaceMode mode, int count, out Thing dropped)
        {
            dropped = null;
            if (DropFails || !items.Contains(food)) return false;
            if (count < food.stackCount) { food.stackCount -= count; dropped = new Thing { def = food.def, stackCount = count }; }
            else { items.Remove(food); dropped = food; }
            dropped.Map = map; dropped.Position = position; dropped.Spawned = true;
            return true;
        }
    }
    public class Pawn_InventoryTracker
    {
        public readonly List<Thing> Items = new List<Thing>();
        public readonly ThingOwner innerContainer;
        public Pawn_InventoryTracker() { innerContainer = new ThingOwner(Items); }
        public bool Contains(Thing thing) => Items.Contains(thing);
    }
    public class Pawn_CarryTracker
    {
        private readonly Pawn pawn;
        public Thing CarriedThing;
        public bool DropFails;
        public readonly ThingOwner innerContainer;
        public Pawn_CarryTracker(Pawn pawn) { this.pawn = pawn; innerContainer = new ThingOwner(this); }
        public int Capacity = 75;
        public int AvailableStackSpace(ThingDef def) => CarriedThing == null ? Capacity : CarriedThing.def == def ? Math.Max(0, Capacity - CarriedThing.stackCount) : 0;
        public bool TryDropCarriedThing(IntVec3 position, ThingPlaceMode mode, out Thing dropped)
        {
            dropped = null;
            if (DropFails || CarriedThing == null) return false;
            dropped = CarriedThing; CarriedThing = null;
            dropped.Position = position; dropped.Spawned = true; dropped.Map = pawn.MapHeld;
            dropped.HeldBy = null;
            return true;
        }
        public void Take(Thing target, int count)
        {
            if (CarriedThing != null) throw new InvalidOperationException("Cannot pick up while already carrying something.");
            if (target is Pawn child)
            {
                child.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, false);
                child.Spawned = false; CarriedThing = child; child.HeldBy = pawn;
                child.jobs.StartJob(Verse.AI.JobMaker.MakeJob(RimWorld.JobDefOf.Carried), Verse.AI.JobCondition.InterruptForced);
            }
            else
            {
                if (target.stackCount > count)
                {
                    target.stackCount -= count;
                    CarriedThing = new Thing { stackCount = count, Map = target.Map, def = target.def, Spawned = false };
                }
                else { CarriedThing = target; target.Spawned = false; }
            }
        }
    }
    public class Map
    {
        public bool IsPlayerHome = true;
        public int CellCount = 10000;
        public readonly Dictionary<int, Room> Rooms = new Dictionary<int, Room>();
        public readonly HashSet<int> BlockedCells = new HashSet<int>();
        public readonly MapPawns mapPawns = new MapPawns();
        public readonly List<Building> Buildings = new List<Building>();
        public readonly HashSet<int> FoggedCells = new HashSet<int>();
        public readonly ListerBuildings listerBuildings;
        public Map() { listerBuildings = new ListerBuildings(Buildings); }
        public RatkinDiningEtiquette.MapComponent_RatToddlerDining Manager;
        public ReservationManager reservationManager = new ReservationManager();
        public T GetComponent<T>() where T : class => Manager as T;
    }
    public class Room { public bool IsPrisonCell; public bool ProperRoom = true; }
    public class MapPawns
    {
        public readonly List<Pawn> SlavesAndPrisonersOfColonySpawned = new List<Pawn>();
        public readonly List<Pawn> AllPawnsSpawned = new List<Pawn>();
        public IEnumerable<Pawn> SpawnedPawnsInFaction(RimWorld.Faction faction) => AllPawnsSpawned.Where(p => p.Faction == faction && p.Spawned);
    }
    public class ListerBuildings { public readonly List<Building> allBuildingsColonist; public ListerBuildings(List<Building> list) { allBuildingsColonist = list; } }
    public class ReservationManager
    {
        public sealed class Reservation
        {
            public Pawn Pawn;
            public Verse.AI.Job Job;
            public Verse.AI.LocalTargetInfo Target;
            public int MaxPawns, Count;
        }
        public readonly List<Reservation> Reservations = new List<Reservation>();
        public Pawn FirstRespectedReserver(Verse.AI.LocalTargetInfo target, Pawn claimant)
            => Reservations.FirstOrDefault(r => SameTarget(r.Target, target))?.Pawn;
        private static bool SameTarget(Verse.AI.LocalTargetInfo a, Verse.AI.LocalTargetInfo b)
            => a.Thing != null || b.Thing != null ? a.Thing == b.Thing : a.Cell == b.Cell;
        public int CanReserveStack(Pawn pawn, Verse.AI.LocalTargetInfo target, int maxPawns)
        {
            if (!pawn.CanReserveTargets) return 0;
            int total = target.Thing?.stackCount ?? 1;
            var others = Reservations.Where(r => SameTarget(r.Target, target) && r.Pawn != pawn).ToList();
            if (others.Count >= maxPawns || others.Any(r => r.MaxPawns != maxPawns)) return 0;
            return Math.Max(0, total - others.Sum(r => r.Count < 0 ? total : r.Count));
        }
        public bool CanReserve(Pawn pawn, Verse.AI.LocalTargetInfo target, int maxPawns, int count)
        {
            int wanted = count < 0 ? target.Thing?.stackCount ?? 1 : count;
            return wanted > 0 && wanted <= CanReserveStack(pawn, target, maxPawns);
        }
        public bool Reserve(Pawn pawn, Verse.AI.Job job, Verse.AI.LocalTargetInfo target, int maxPawns, int count, bool error)
        {
            if (!CanReserve(pawn, target, maxPawns, count))
            { if (error) Log.Error("Could not reserve"); return false; }
            if (!Reservations.Any(r => r.Pawn == pawn && r.Job == job && SameTarget(r.Target, target)))
                Reservations.Add(new Reservation { Pawn = pawn, Job = job, Target = target, MaxPawns = maxPawns, Count = count });
            return true;
        }
        public void Release(Thing target, Pawn pawn, Verse.AI.Job job)
            => Reservations.RemoveAll(r => r.Target.Thing == target && r.Pawn == pawn && r.Job == job);
        public void ReleaseAllForJob(Pawn pawn, Verse.AI.Job job)
            => Reservations.RemoveAll(r => r.Pawn == pawn && r.Job == job);
    }
    public class MapComponent
    {
        protected readonly Map map;
        public MapComponent(Map map) { this.map = map; }
        public virtual void ExposeData() { }
        public virtual void MapComponentTick() { }
        public virtual void FinalizeInit() { }
    }
    public static class Find { public static TickManager TickManager = new TickManager(); public static Targeter Targeter = new Targeter(); }
    public class TickManager { public int TicksGame; }
    public static class Log
    {
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Messages = new List<string>();
        public static void Message(string s) { Messages.Add(s); }
        public static void Warning(string s) { }
        public static void Error(string s) => Errors.Add(s);
    }
    public static class GenTemperature { public static bool SafeTemperatureAtCell(Pawn pawn, IntVec3 cell, Map map) => !pawn.UnsafeTemperature; }
    public static class MoteMaker
    {
        public static int Count;
        public static string LastText;
        public static void ThrowText(object position, Map map, string text, float size) { Count++; LastText = text; }
    }
    public static class TranslationExtensions
    {
        public static readonly Dictionary<string, string> Translations = new Dictionary<string, string>();
        public static string Translate(this string s, params object[] args) => Translations.TryGetValue(s, out string text) ? text : s;
    }
    public enum ThingRequestGroup { Pawn }
    public struct ThingRequest { public static ThingRequest ForGroup(ThingRequestGroup group) => new ThingRequest(); }
    public struct TargetInfo { public Thing Thing; }
    public class TargetingParameters { public bool canTargetBuildings; public Predicate<Verse.AI.LocalTargetInfo> validator; }
    public class Targeter { public void BeginTargeting(TargetingParameters parameters, Action<Verse.AI.LocalTargetInfo> action, object ignored) { } }
    public class FloatMenuOption { public FloatMenuOption(string label, Action action) { } }
    public static class ReservationExtensions
    {
        public static bool CanReserve(this Pawn pawn, Thing thing, int max = 1, int count = -1, object layer = null, bool forced = false)
            => pawn.Map.reservationManager.CanReserve(pawn, thing, max, count);
        public static bool Reserve(this Pawn pawn, Verse.AI.LocalTargetInfo target, Verse.AI.Job job, int max = 1, int count = -1, object layer = null, bool error = false)
            => pawn.Map.reservationManager.Reserve(pawn, job, target, max, count, error);
        public static bool ReserveSittableOrSpot(this Pawn pawn, IntVec3 cell, Verse.AI.Job job, bool error)
        {
            // Native ReservationUtility reads the edifice grid before reserving.
            // Model that bounds-sensitive read; other reservation semantics remain simplified.
            cell.GetEdifice(pawn.Map);
            return pawn.Map.reservationManager.Reserve(pawn, job, cell, 1, -1, error);
        }
        public static bool CanReserveSittableOrSpot(this Pawn pawn, IntVec3 cell)
        {
            cell.GetEdifice(pawn.Map);
            return pawn.Map.reservationManager.CanReserve(pawn, cell, 1, -1);
        }
        public static void ClearReservationsForJob(this Pawn pawn, Verse.AI.Job job) => pawn.Map.reservationManager.ReleaseAllForJob(pawn, job);
        public static bool CanReach(this Pawn pawn, Thing target, Verse.AI.PathEndMode mode, Danger danger) => pawn.CanReachTargets;
        public static bool CanReach(this Pawn pawn, IntVec3 target, Verse.AI.PathEndMode mode, Danger danger) => pawn.CanReachTargets;
        public static bool CanReserveAndReach(this Pawn pawn, Thing target, Verse.AI.PathEndMode mode, Danger danger) => pawn.CanReserve(target) && pawn.CanReachTargets;
    }
}

namespace Verse.AI.Group
{
    public class Lord { public object CurLordToil; }
    public class LordToil_DefendPoint { }
    public static class LordUtility { public static Lord GetLord(this Verse.Pawn pawn) => pawn.Lord; }
}

namespace Verse.AI
{
    using Verse;
    using RimWorld;
    public enum PathEndMode { Touch, OnCell, ClosestTouch }
    public class JobGiver_Wander { }
    public class PawnDuty { public RimWorld.DutyDef def; public PawnDuty(RimWorld.DutyDef def) { this.def = def; } }
    public enum JobCondition { None, Succeeded, Incompletable, Errored, ErroredPather, InterruptForced, QueuedNoLongerValid }
    public enum JobTag { SatisfyingNeeds }
    public enum TargetIndex { None, A, B, C }
    public enum ToilCompleteMode { Instant, Never, PatherArrival, Delay }
    public struct LocalTargetInfo
    {
        public Thing Thing;
        private IntVec3 cell;
        public Pawn Pawn => Thing as Pawn;
        public bool IsValid => Thing != null || cell.IsValid;
        public static LocalTargetInfo Invalid => new LocalTargetInfo(IntVec3.Invalid);
        public IntVec3 Cell => Thing?.Position ?? cell;
        public LocalTargetInfo(Thing thing) { Thing = thing; cell = IntVec3.Invalid; }
        public LocalTargetInfo(IntVec3 cell) { Thing = null; this.cell = cell; }
        public static implicit operator LocalTargetInfo(Thing thing) => new LocalTargetInfo(thing);
        public static implicit operator LocalTargetInfo(IntVec3 cell) => new LocalTargetInfo(cell);
    }
    public class Job
    {
        private static int nextId;
        public int loadID = ++nextId;
        public JobDef def;
        public WorkGiverDef workGiverDef;
        public int count = 1;
        public bool playerForced, exitMapOnArrival;
        public LocalTargetInfo targetA, targetB, targetC;
        public void SetTarget(TargetIndex index, LocalTargetInfo target)
        { if (index == TargetIndex.A) targetA = target; else if (index == TargetIndex.B) targetB = target; else targetC = target; }
        public LocalTargetInfo GetTarget(TargetIndex index) => index == TargetIndex.A ? targetA : index == TargetIndex.B ? targetB : targetC;
        public bool AnyTargetIs(LocalTargetInfo target) => target.Thing != null
            && (targetA.Thing == target.Thing || targetB.Thing == target.Thing || targetC.Thing == target.Thing);
    }
    public static class JobMaker
    {
        public static void ReturnToPool(Job job) { }
        public static Job MakeJob(JobDef def, int ticks) => new Job { def = def };
        public static Job MakeJob(JobDef def, LocalTargetInfo a = default, LocalTargetInfo b = default, LocalTargetInfo c = default)
            => new Job { def = def, targetA = a, targetB = b, targetC = c };
    }
    public class Pawn_JobTracker
    {
        private readonly Pawn pawn;
        public Job curJob;
        public JobDriver curDriver;
        public int Starts;
        public bool LastReservationsCanFail;
        public Pawn_JobTracker(Pawn pawn) { this.pawn = pawn; }
        public void StartJob(Job job, JobCondition condition = JobCondition.None, bool? keepCarryingThingOverride = null,
            JobTag? tag = null, bool preToilReservationsCanFail = false)
        {
            object[] mealArgs = { pawn, job, preToilReservationsCanFail, keepCarryingThingOverride };
            typeof(RatkinDiningEtiquette.Pawn_JobTracker_StartJob_CoordinateToddlerMeal)
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, mealArgs);
            job = (Job)mealArgs[1]; preToilReservationsCanFail = (bool)mealArgs[2]; keepCarryingThingOverride = (bool?)mealArgs[3];
            object[] args = { pawn, job, keepCarryingThingOverride };
            typeof(RatkinDiningEtiquette.Pawn_JobTracker_StartJob_PreserveToddlerMeal)
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            keepCarryingThingOverride = (bool?)args[2];
            var reservationPatch = typeof(RatkinDiningEtiquette.Pawn_JobTracker_StartJob_PreserveToddlerMeal).Assembly
                .GetType("RatkinDiningEtiquette.Pawn_JobTracker_StartJob_DiningReservations");
            if (reservationPatch != null)
            {
                object[] reservationArgs = { pawn, job, preToilReservationsCanFail };
                reservationPatch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, reservationArgs);
                preToilReservationsCanFail = (bool)reservationArgs[2];
            }
            LastReservationsCanFail = preToilReservationsCanFail;
            if (curJob != null) Cleanup(condition, keepCarryingThingOverride);
            curJob = job; Starts++;
            if (pawn.carryTracker.CarriedThing != null && !(keepCarryingThingOverride ?? !job.def.dropThingBeforeJob))
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
        public void EndCurrentJob(JobCondition condition, bool startNewJob = true)
        {
            Job finalizer = curDriver?.GetFinalizerJob(condition);
            Cleanup(condition, startNewJob && finalizer != null ? true : null);
            if (startNewJob && (condition == JobCondition.Errored || condition == JobCondition.ErroredPather))
                StartJob(JobMaker.MakeJob(JobDefOf.Wait, 250));
            else if (startNewJob && finalizer != null) StartJob(finalizer, JobCondition.None, keepCarryingThingOverride: true, preToilReservationsCanFail: true);
        }
        private void Cleanup(JobCondition condition, bool? keep)
        {
            Job old = curJob;
            if (old != null) pawn.ClearReservationsForJob(old);
            curDriver?.Cleanup(condition); curDriver = null; curJob = null;
            if (old != null && pawn.carryTracker.CarriedThing != null && !(keep ?? old.def.carryThingAfterJob))
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
        public void TryTakeOrderedJob(Job job) { job.playerForced = true; StartJob(job, JobCondition.InterruptForced); }
    }
    public class Pawn_PathFollower
    {
        private readonly Pawn pawn;
        public Pawn_PathFollower(Pawn pawn) { this.pawn = pawn; }
        public void StartPath(LocalTargetInfo target, PathEndMode mode) { pawn.Position = target.Cell; }
    }
    public class Toil
    {
        public Pawn actor;
        public Action initAction;
        public Action<int> tickIntervalAction;
        public ToilCompleteMode defaultCompleteMode;
        public string Name;
        public void ReturnToPool() { }
    }
    public static class ToilMaker { public static Toil MakeToil(string name) => new Toil { Name = name }; }
    public abstract class JobDriver
    {
        public Pawn pawn;
        public Job job;
        public bool Ended;
        public int CurToilIndex = -1;
        public TargetIndex rotateToFace;
        private Func<JobCondition, Job> finalizer;
        public readonly List<Action<JobCondition>> FinishActions = new List<Action<JobCondition>>();
        public readonly List<Func<bool>> Failures = new List<Func<bool>>();
        public virtual void ExposeData() { }
        public abstract bool TryMakePreToilReservations(bool error);
        protected abstract IEnumerable<Toil> MakeNewToils();
        public List<Toil> TestToils() { var result = MakeNewToils().ToList(); foreach (var toil in result) toil.actor = pawn; return result; }
        protected void AddFinishAction(Action<JobCondition> action) => FinishActions.Add(action);
        protected void SetFinalizerJob(Func<JobCondition, Job> factory) => finalizer = factory;
        public Job GetFinalizerJob(JobCondition condition) => finalizer?.Invoke(condition);
        public void Cleanup(JobCondition condition) { foreach (var action in FinishActions) action(condition); }
        public void EndJobWith(JobCondition condition) { Ended = true; pawn.jobs.EndCurrentJob(condition); }
        public void ReadyForNextToil() { }
    }
    public static class DriverExtensions
    {
        public static void FailOn(this JobDriver driver, Func<bool> failure) => driver.Failures.Add(failure);
        public static void FailOnDestroyedOrNull(this JobDriver driver, TargetIndex index)
            => driver.Failures.Add(() => driver.job.GetTarget(index).Thing == null || driver.job.GetTarget(index).Thing.Destroyed);
        public static void FailOnBurningImmobile(this JobDriver driver, TargetIndex index)
            => driver.Failures.Add(() => (driver.job.GetTarget(index).Thing as Building)?.Burning == true);
        public static Toil FailOnDestroyedOrNull(this Toil toil, TargetIndex index) => toil;
    }
    public static class Toils_Goto
    {
        public static Toil GotoThing(TargetIndex index, PathEndMode mode)
        { var toil = ToilMaker.MakeToil("GotoThing"); toil.initAction = () => toil.actor.pather.StartPath(toil.actor.CurJob.GetTarget(index), mode); return toil; }
        public static Toil GotoCell(TargetIndex index, PathEndMode mode) => GotoThing(index, mode);
    }
    public static class Toils_Jump
    {
        public static Toil LastTarget;
        public static bool LastJump;
        public static Toil JumpIf(Toil target, Func<bool> condition)
        {
            var toil = ToilMaker.MakeToil("JumpIf");
            toil.initAction = () => { LastTarget = target; LastJump = condition(); };
            return toil;
        }
    }
    public static class Toils_Haul
    {
        public static Toil StartCarryThing(TargetIndex index, bool queue = false, bool subtract = false, bool fail = false,
            bool reserve = true, bool canTakeFromInventory = false)
        {
            var toil = ToilMaker.MakeToil("StartCarryThing");
            toil.initAction = () => { var actor = toil.actor; actor.carryTracker.Take(actor.CurJob.GetTarget(index).Thing, actor.CurJob.count); actor.CurJob.SetTarget(index, actor.carryTracker.CarriedThing); };
            return toil;
        }
    }
}

namespace RimWorld
{
    using Verse;
    using Verse.AI;
    public class JobDef
    { public string defName; public bool carryThingAfterJob, dropThingBeforeJob = true, casualInterruptible = true, suspendable = true; public bool playerInterruptible; }
    public static class JobDefOf
    {
        public static readonly JobDef Ingest = new JobDef { defName = "Ingest" };
        public static readonly JobDef Carried = new JobDef { defName = "Carried", carryThingAfterJob = true };
        public static readonly JobDef Wait = new JobDef { defName = "Wait" };
        public static readonly JobDef GotoWander = new JobDef { defName = "GotoWander" };
        public static readonly JobDef Wait_Wander = new JobDef { defName = "Wait_Wander" };
        public static readonly JobDef Wait_MaintainPosture = new JobDef { defName = "Wait_MaintainPosture" };
        public static readonly JobDef BottleFeedBaby = new JobDef { defName = "BottleFeedBaby" };
        public static readonly JobDef FeedPatient = new JobDef { defName = "FeedPatient" };
        public static readonly JobDef Breastfeed = new JobDef { defName = "Breastfeed" };
        public static readonly JobDef BreastfeedCarryToMom = new JobDef { defName = "BreastfeedCarryToMom" };
        public static readonly JobDef BabySuckle = new JobDef { defName = "BabySuckle" };
        public static readonly JobDef BringBabyToSafety = new JobDef { defName = "BringBabyToSafety" };
        public static readonly JobDef BringBabyToSafetyUnforced = new JobDef { defName = "BringBabyToSafetyUnforced" };
    }
    public class JobDriver_Ingest : JobDriver
    {
        private Toil chewing;
        public bool EatingFromInventory => pawn.inventory.Contains(job.targetA.Thing);
        public override bool TryMakePreToilReservations(bool error) => true;
        protected override IEnumerable<Toil> MakeNewToils() { yield break; }
    }
    public static class Toils_Ingest
    {
        public static IntVec3 TestDiningCell = IntVec3.Invalid;
        public static bool TestUseStandingSelector;
        public static Action TestCarryAction;
        public static Action TestFinderAction;
        public static Action TestFinalizeAction;
        private static Toil Protect(string type, Toil toil, params object[] arguments)
        {
            var args = arguments.Concat(new object[] { toil }).ToArray();
            typeof(RatkinDiningEtiquette.RatDiningSpotSafety).Assembly.GetType("RatkinDiningEtiquette." + type)
                .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
            return (Toil)args[args.Length - 1];
        }
        public static bool TryFindChairOrSpot(Pawn pawn, Thing food, out IntVec3 cell)
        {
            // Execute the production method-boundary hooks around the native
            // search contract. The pre-1.1.1 production assembly has no hooks.
            Type patch = typeof(RatkinDiningEtiquette.RatDiningSpotSafety).Assembly
                .GetType("RatkinDiningEtiquette.Toils_Ingest_ProtectPrisonerMealSelection");
            object[] begin = { pawn, food, null };
            patch?.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, begin);
            cell = IntVec3.Invalid; bool result = false; Exception error = null;
            try
            {
                TestFinderAction?.Invoke();
                cell = TestUseStandingSelector ? RCellFinder.SpotToChewStandingNear(pawn, food) : TestDiningCell;
                if (TestUseStandingSelector) cell.GetEdifice(pawn.Map); // Real vanilla reads danger before IsValid.
                result = cell.IsValid;
            }
            catch (Exception failure) { error = failure; }
            if (patch != null)
            {
                object[] finish = { pawn, food, begin[2], error, result, cell };
                try { error = (Exception)patch.GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, finish); }
                catch (TargetInvocationException failure) { throw failure.InnerException; }
                result = (bool)finish[4]; cell = (IntVec3)finish[5];
            }
            if (error != null) throw error;
            return result;
        }
        public static Toil CarryIngestibleToChewSpot(Pawn pawn, TargetIndex ingestibleInd)
        {
            var toil = ToilMaker.MakeToil("CarryIngestibleToChewSpot");
            toil.initAction = () => {
                if (TestCarryAction != null) { TestCarryAction(); return; }
                IntVec3 cell = RCellFinder.SpotToChewStandingNear(toil.actor, toil.actor.CurJob.GetTarget(ingestibleInd).Thing);
                cell.GetEdifice(pawn.Map); // Models Toddlers reserving the unchecked return cell.
                pawn.pather.StartPath(cell, PathEndMode.OnCell);
            };
            return Protect("Toils_Ingest_ProtectOwnedDiningSpot", toil, ingestibleInd);
        }
        public static Toil FindAdjacentEatSurface(TargetIndex eatSurfaceInd, TargetIndex foodInd)
        {
            var toil = ToilMaker.MakeToil("FindAdjacentEatSurface");
            toil.initAction = () => {
                for (int i = 0; i < 4; i++) {
                    IntVec3 cell = toil.actor.Position + new Rot4(i).FacingCell;
                    if (cell.HasEatSurface(toil.actor.Map)) { toil.actor.CurJob.SetTarget(eatSurfaceInd, cell); break; }
                }
            };
            return Protect("Toils_Ingest_ProtectOwnedDiningMapEdge", toil, eatSurfaceInd, foodInd);
        }
        public static Toil ChewIngestible(Pawn pawn, float duration, TargetIndex food, TargetIndex surface)
            => ToilMaker.MakeToil("ChewIngestible");
        public static Toil FinalizeIngest(Pawn ingester, TargetIndex index)
        {
            Pawn pawn = ingester;
            var toil = ToilMaker.MakeToil("FinalizeIngest");
            toil.initAction = () => {
                if (TestFinalizeAction != null) { TestFinalizeAction(); return; }
                Thing food = pawn.CurJob.GetTarget(index).Thing;
                if (pawn.needs.mood != null && food.def.IsNutritionGivingIngestible
                    && !(pawn.Position + pawn.Rotation.FacingCell).HasEatSurface(pawn.Map))
                    pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
                int eaten = Math.Min(food.stackCount, FoodUtility.WillIngestStackCountOf(pawn, food.def, FoodUtility.NutritionForEater(pawn, food)));
                if (food is Corpse) eaten = 1;
                RatkinDiningEtiquette.Thing_Ingested_ClearPreviousChairThought.Postfix(food, pawn, eaten * food.def.ingestible.nutrition);
                pawn.needs.food.CurLevelPercentage = Math.Min(1f, pawn.needs.food.CurLevelPercentage + eaten * food.def.ingestible.nutrition / pawn.needs.food.MaxLevel);
                bool depleted = food is Corpse corpse ? --corpse.BodyParts == 0 : (food.stackCount -= eaten) == 0;
                if (depleted) { food.Destroyed = true; if (pawn.carryTracker.CarriedThing == food) pawn.carryTracker.CarriedThing = null; }
            };
            return Protect("Toils_Ingest_ProtectOwnedDiningFinalization", toil, ingester);
        }
    }
    public static class RCellFinder
    {
        public static IntVec3 TestSpot = IntVec3.Invalid;
        public static IntVec3 SpotToChewStandingNear(Pawn pawn, Thing ingestible, Predicate<IntVec3> extraValidator = null)
        {
            IntVec3 result = TestSpot;
            var method = typeof(RatkinDiningEtiquette.RCellFinder_ScopedDiningResult).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
            ((Action<Pawn, IntVec3>)method.CreateDelegate(typeof(Action<Pawn, IntVec3>)))(pawn, result);
            return result;
        }
    }
    public static class Toils_Misc
    {
        public static Toil TakeItemFromInventoryToCarrier(Pawn pawn, TargetIndex index)
        { var toil = ToilMaker.MakeToil("TakeInventory"); toil.initAction = () => { var food = pawn.CurJob.GetTarget(index).Thing; pawn.inventory.Items.Remove(food); pawn.carryTracker.Take(food, pawn.CurJob.count); pawn.CurJob.SetTarget(index, pawn.carryTracker.CarriedThing); }; return toil; }
    }
    public class ThoughtDef
    {
        public string defName;
        public Type thoughtClass = typeof(Thought_Memory);
        public List<float> stages = new List<float> { 0 };
        public List<string> descriptions = new List<string> { "normal description" };
    }
    public class Thought_Memory
    {
        public ThoughtDef def; public int stage, age;
        public int CurStageIndex => stage;
        public virtual string Description => def.descriptions[stage];
        public virtual string BabyTalk => "vanilla babble";
        public bool ShouldDiscard => age > 60000;
        public void SetForcedStage(int value) { stage = value; }
        public virtual void ExposeData() { Scribe_Values.Look(ref age, "age", 0); Scribe_Values.Look(ref stage, "stageIndex", 0); }
        public virtual bool TryMergeWithExistingMemory(out bool showBubble) { showBubble = true; return false; }
    }
    public static class ThoughtMaker
    {
        public static Thought_Memory MakeThought(ThoughtDef def, int stage)
        { var thought = (Thought_Memory)Activator.CreateInstance(def.thoughtClass); thought.def = def; thought.stage = stage; return thought; }
    }
    public class MemoryThoughtHandler
    {
        public readonly List<Thought_Memory> Memories = new List<Thought_Memory>();
        public void TryGainMemory(ThoughtDef def, Pawn otherPawn = null, Precept sourcePrecept = null)
        {
            var method = typeof(RatkinDiningEtiquette.MemoryThoughtHandler_ChairMealTableThought).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            if (((Func<MemoryThoughtHandler, ThoughtDef, bool>)method.CreateDelegate(typeof(Func<MemoryThoughtHandler, ThoughtDef, bool>)))(this, def))
                TryGainMemory(ThoughtMaker.MakeThought(def, 0));
        }
        public void TryGainMemory(Thought_Memory thought) => Memories.Add(thought);
        public Thought_Memory GetFirstMemoryOfDef(ThoughtDef thought) => Memories.FirstOrDefault(m => m.def == thought);
        public void RemoveMemory(Thought_Memory memory) => Memories.Remove(memory);
    }
    public class Precept { }
    public static class ThoughtDefOf { public static readonly ThoughtDef AteWithoutTable = new ThoughtDef(); }
    public static class FoodUtility
    {
        public static Thing BabyFood;
        public static bool WillEat(this Pawn pawn, Thing food, Pawn getter = null,
            bool careIfNotAcceptableForTitle = true, bool allowVenerated = false)
            => pawn.needs.food != null && food?.def?.ingestible != null
                && (!pawn.DevelopmentalStage.Baby() || food.def.ingestible.babiesCanIngest)
                && !pawn.DisallowedFood.Contains(food.def);
        public static int WillIngestStackCountOf(Pawn child, ThingDef def, float nutrition) => Math.Max(1, (int)Math.Ceiling(child.needs.food.NutritionWanted / nutrition - .00001f));
        public static float NutritionForEater(Pawn child, Thing food) => food.def.ingestible.nutrition;
        public static int GetMaxAmountToPickup(Thing food, Pawn pawn, int wanted)
        {
            int count = Math.Min(Math.Max(1, wanted), food.stackCount);
            return food.Spawned ? Math.Min(count, food.Map.reservationManager.CanReserveStack(pawn, food, 10)) : count;
        }
    }
    public static class PawnRelationDefOf { public static object Parent = new object(); }
    public static class PawnCapacityDefOf { public static object Manipulation = new object(), Talking = new object(); }
    public class Faction { public static readonly Faction OfPlayer = new Faction(); public bool Hostile; public bool HostileTo(Faction other) => Hostile; }
    public class DutyDef { }
    public static class DutyDefOf { public static readonly DutyDef Defend = new DutyDef(); }
    public class WorkTypeDef { }
    public static class WorkTypeDefOf { public static WorkTypeDef Childcare = new WorkTypeDef(), Warden = new WorkTypeDef(); }
    public static class StatDefOf { public static object EatingSpeed = new object(); }
    public class WorkGiverDef { public Type giverClass; }
    public static class FeedPatientUtility { public static bool IsHungry(Pawn pawn) => pawn.needs.food.CurLevelPercentage < .9f; }
    public static class HealthAIUtility { public static bool ShouldSeekMedicalRest(Pawn pawn) => pawn.MedicalRest; }
    public static class ChildcareUtility
    {
        public static bool CanHaulBaby(Pawn helper, Pawn child, out object reason) { reason = null; return true; }
        public static Thing FindBabyFoodForBaby(Pawn helper, Pawn child) => FoodUtility.BabyFood;
        public static bool WantsSuckle(Pawn baby) { object[] args = { baby, true }; typeof(RatkinDiningEtiquette.ChildcareUtility_WantsSuckle_DiningRequest).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); return (bool)args[1]; }
    }
    public class JobGiver_GetFood { }
    public abstract class WorkGiver_Scanner
    {
        public virtual ThingRequest PotentialWorkThingRequest => new ThingRequest();
        public virtual PathEndMode PathEndMode => PathEndMode.Touch;
        public virtual bool Prioritized => false;
        public virtual IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) { yield break; }
        public virtual bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) => true;
        public virtual Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => null;
        public virtual float GetPriority(Pawn pawn, TargetInfo target) => 0;
    }
    public class FloatMenuContext { public Pawn FirstSelectedPawn; }
    public class WorkGiver_Warden : WorkGiver_Scanner
    {
        protected bool ShouldTakeCareOfPrisoner(Pawn helper, Pawn child, bool forced = false)
            => child.IsPrisonerOfColony && child.Spawned && child.guest.PrisonerIsSecure && !child.InMentalState
                && !child.Forbidden && !child.FormingCaravan && helper.CanReserveAndReach(child, PathEndMode.Touch, Danger.Some);
    }
    public class WorkGiver_Warden_Feed : WorkGiver_Warden { }
    public class WorkGiver_PlayWithBaby : WorkGiver_Scanner { }
    public abstract class FloatMenuOptionProvider
    {
        protected virtual bool Drafted => false;
        protected virtual bool Undrafted => true;
        protected virtual bool Multiselect => false;
        protected virtual bool RequiresManipulation => true;
        public virtual bool TargetPawnValid(Pawn pawn, FloatMenuContext context) => true;
        protected virtual FloatMenuOption GetSingleOptionFor(Pawn pawn, FloatMenuContext context) => null;
    }
    public static class FloatMenuUtility { public static FloatMenuOption DecoratePrioritizedTask(FloatMenuOption option, Pawn pawn, Thing thing) => option; }
}

namespace RimWorld.Planet
{
    public static class CaravanExtensions { public static bool IsFormingCaravan(this Verse.Pawn pawn) => pawn.FormingCaravan; }
}

namespace Toddlers
{
    public static class BabyMoveUtility
    {
        public static Verse.AI.LocalTargetInfo TestBestPlace = Verse.AI.LocalTargetInfo.Invalid;
        public static Verse.AI.LocalTargetInfo BestPlaceForBaby(Verse.Pawn baby, Verse.Pawn helper) => TestBestPlace;
    }
    public static class ToddlerUtility { public static bool IsToddler(Verse.Pawn pawn) => pawn?.Toddler == true; public static bool IsBabyBusy(Verse.Pawn baby) => false; }
    public static class ToddlerLearningUtility
    { public static bool CanFeedSelf(Verse.Pawn pawn) => pawn.SelfFeeding; public static bool EatsOnFloor(Verse.Pawn pawn) => true; }
}

namespace RatkinDiningEtiquette
{
    using Verse;
    using RimWorld;
    public class RatDiningRaceExtension { }
    public static class RatDiningMod { public static RatDiningSettings Settings = new RatDiningSettings(); }
    public static class RDE_DefOf
    {
        public static JobDef RDE_EatUsingChairAsTable, RDE_ToddlerPrepareDiningRequest, RDE_ToddlerWaitForDiningHelp,
            RDE_AssistRatToddlerDining, RDE_ToddlerEatAtChair, RDE_ToddlerEatHeldFoodOnFloor,
            RDE_ProvideRatToddlerDiningMeal, RDE_ToddlerWaitForMeal, RDE_ReturnDiningChild;
        public static ThoughtDef RDE_UsedChairAsTable, RDE_ToddlerNoDiningHelp, RDE_RatkinChairAsTable,
            RDE_ToiletAsTable, RDE_SpecialSeatAsTable;
    }
    public static class TestFurniture
    {
        private static Building chair;
        public static Building Chair
        {
            get => chair;
            set
            {
                chair?.Map?.Buildings.Remove(chair);
                chair = value;
                if (chair != null && !chair.Map.Buildings.Contains(chair)) chair.Map.Buildings.Add(chair);
            }
        }
    }
}
