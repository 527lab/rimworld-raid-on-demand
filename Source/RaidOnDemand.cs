using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidOnDemand
{
    public enum CallType
    {
        Raid,
        MechCluster,
        Infestation,
        Manhunter
    }

    public class MainTabWindow_RaidOnDemand : MainTabWindow
    {
        private static CallType type = CallType.Raid;
        private static Faction faction;
        private static RaidStrategyDef strategy;
        private static PawnsArrivalModeDef arrival;
        private static float multiplier = 1f;
        private static int waves = 1;

        public override Vector2 RequestedTabSize => new Vector2(560f, 380f);

        private static Map Map => Find.CurrentMap;

        private static float BasePoints => Map == null ? 0f : StorytellerUtility.DefaultThreatPointsNow(Map);

        private static float Points => Mathf.Max(35f, BasePoints * multiplier);

        private static IncidentDef DefFor(CallType t)
        {
            switch (t)
            {
                case CallType.MechCluster: return IncidentDefOf.MechCluster;
                case CallType.Infestation: return IncidentDefOf.Infestation;
                case CallType.Manhunter: return IncidentDefOf.ManhunterPack;
                default: return IncidentDefOf.RaidEnemy;
            }
        }

        private static string TypeLabel(CallType t)
        {
            return ("ROD_Type_" + t).Translate();
        }

        private static readonly HashSet<string> HiddenDefs = new HashSet<string>
        {
            "ImmediateAttackFriendly", "SpecificDropDebug", "MechClusterDrop"
        };

        private static string NiceLabel(Def def, string prefix)
        {
            string key = prefix + def.defName;
            if (key.CanTranslate())
            {
                return key.Translate();
            }
            return def.label.NullOrEmpty() ? def.defName : (string)def.LabelCap;
        }

        private static IncidentParms MakeParms()
        {
            var parms = new IncidentParms
            {
                target = Map,
                points = Points,
                forced = true
            };
            if (type == CallType.Raid)
            {
                parms.faction = faction;
                parms.raidStrategy = strategy;
                parms.raidArrivalMode = arrival;
            }
            return parms;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Map == null)
            {
                Widgets.Label(inRect, "ROD_NoMap".Translate());
                return;
            }
            if (faction != null && (faction.deactivated || !faction.HostileTo(Faction.OfPlayer)))
            {
                faction = null;
                strategy = null;
                arrival = null;
            }

            var l = new Listing_Standard();
            l.Begin(inRect);
            Text.Font = GameFont.Medium;
            l.Label("ROD_Title".Translate());
            Text.Font = GameFont.Small;
            l.GapLine();

            if (l.ButtonTextLabeledPct("ROD_Type".Translate(), TypeLabel(type), 0.3f))
            {
                TypeMenu();
            }
            if (type == CallType.Raid)
            {
                string fLabel = faction == null ? (string)"ROD_Random".Translate() : faction.Name + " (" + (string)faction.def.LabelCap + ")";
                if (l.ButtonTextLabeledPct("ROD_Faction".Translate(), fLabel, 0.3f))
                {
                    FactionMenu();
                }
                string sLabel = faction == null ? (string)"ROD_PickFactionFirst".Translate() : (strategy == null ? (string)"ROD_Auto".Translate() : NiceLabel(strategy, "ROD_Strat_"));
                if (l.ButtonTextLabeledPct("ROD_Strategy".Translate(), sLabel, 0.3f) && faction != null)
                {
                    StrategyMenu();
                }
                string aLabel = faction == null ? (string)"ROD_PickFactionFirst".Translate() : (arrival == null ? (string)"ROD_Auto".Translate() : NiceLabel(arrival, "ROD_Arr_"));
                if (l.ButtonTextLabeledPct("ROD_Arrival".Translate(), aLabel, 0.3f) && faction != null)
                {
                    ArrivalMenu();
                }
            }

            l.Gap();
            l.Label("ROD_Strength".Translate(multiplier.ToString("0.##"), Points.ToString("F0"), BasePoints.ToString("F0")));
            multiplier = Mathf.Max(0.25f, Mathf.Round(l.Slider(multiplier, 0.25f, 10f) * 4f) / 4f);
            l.Label("ROD_Waves".Translate(waves.ToString()));
            waves = Mathf.Clamp(Mathf.RoundToInt(l.Slider(waves, 1f, 10f)), 1, 10);

            l.Gap();
            if (l.ButtonText("ROD_Call".Translate()))
            {
                Call();
            }
            l.End();
        }

        private static void TypeMenu()
        {
            var opts = new List<FloatMenuOption>();
            foreach (CallType t in Enum.GetValues(typeof(CallType)))
            {
                if (DefFor(t) == null)
                {
                    continue;
                }
                CallType local = t;
                opts.Add(new FloatMenuOption(TypeLabel(t), () => type = local));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void FactionMenu()
        {
            var worker = IncidentDefOf.RaidEnemy.Worker as IncidentWorker_PawnsArrive;
            var check = new IncidentParms { target = Map, points = Points, forced = true };
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("ROD_Random".Translate(), () => { faction = null; strategy = null; arrival = null; })
            };
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading.OrderBy(x => x.Name))
            {
                bool ok;
                try
                {
                    ok = worker != null && worker.FactionCanBeGroupSource(f, check, desperate: true);
                }
                catch
                {
                    ok = false;
                }
                if (!ok)
                {
                    continue;
                }
                Faction local = f;
                opts.Add(new FloatMenuOption(f.Name + " (" + (string)f.def.LabelCap + ")", () => { faction = local; strategy = null; arrival = null; }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void StrategyMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("ROD_Auto".Translate(), () => { strategy = null; arrival = null; })
            };
            var parms = MakeParms();
            parms.raidStrategy = null;
            parms.raidArrivalMode = null;
            foreach (RaidStrategyDef s in DefDatabase<RaidStrategyDef>.AllDefs)
            {
                if (HiddenDefs.Contains(s.defName))
                {
                    continue;
                }
                bool ok;
                try
                {
                    ok = s.Worker.CanUseWith(parms, PawnGroupKindDefOf.Combat);
                }
                catch
                {
                    ok = false;
                }
                if (!ok)
                {
                    continue;
                }
                RaidStrategyDef local = s;
                opts.Add(new FloatMenuOption(NiceLabel(s, "ROD_Strat_"), () => { strategy = local; arrival = null; }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void ArrivalMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("ROD_Auto".Translate(), () => arrival = null)
            };
            var parms = MakeParms();
            parms.raidArrivalMode = null;
            foreach (PawnsArrivalModeDef a in DefDatabase<PawnsArrivalModeDef>.AllDefs)
            {
                if (HiddenDefs.Contains(a.defName))
                {
                    continue;
                }
                bool ok;
                try
                {
                    ok = a.Worker.CanUseWith(parms) && (strategy == null || strategy.arriveModes == null || strategy.arriveModes.Contains(a));
                }
                catch
                {
                    ok = false;
                }
                if (!ok)
                {
                    continue;
                }
                PawnsArrivalModeDef local = a;
                opts.Add(new FloatMenuOption(NiceLabel(a, "ROD_Arr_"), () => arrival = local));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void Call()
        {
            IncidentDef def = DefFor(type);
            int done = 0;
            for (int i = 0; i < waves; i++)
            {
                bool ok;
                try
                {
                    ok = def.Worker.TryExecute(MakeParms());
                }
                catch (Exception e)
                {
                    Log.Error("[RaidOnDemand] " + e);
                    ok = false;
                }
                if (ok)
                {
                    done++;
                }
            }
            if (done == 0)
            {
                Messages.Message("ROD_Failed".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Messages.Message("ROD_Done".Translate(done.ToString(), waves.ToString()), MessageTypeDefOf.NeutralEvent, historical: false);
            Close();
        }
    }
}
