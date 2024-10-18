using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using System.Collections.Generic;

namespace FORCERELATIONS
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Relations : MySessionComponentBase
    {
        public static bool isInit = false;
        public int runCount = 0;

        public override void UpdateBeforeSimulation()
        {
            if (!MyAPIGateway.Multiplayer.IsServer && !MyAPIGateway.Utilities.IsDedicated)
                return;

            if (!isInit)
                init();

            if (++runCount % 3600 > 0) // Runs every minute (3600 ticks = 1 minute)
                return;

            runCount = 0;
            main();
        }

        public void init()
        {
            isInit = true;
            MyLog.Default.WriteLineAndConsole("ForceRelations: Initialized with hardcoded faction logic.");
        }

        public void main()
        {
            try
            {
                var factionList = MyAPIGateway.Session.Factions.Factions;

                foreach (var faction in factionList)
                {
                    // ADM should be friendly to everyone
                    if (faction.Value.Tag == "ADM")
                    {
                        foreach (var otherFaction in factionList)
                        {
                            if (otherFaction.Value.Tag != "ADM")
                                MyVisualScriptLogicProvider.SetRelationBetweenFactions("ADM", otherFaction.Value.Tag, 1500);
                        }
                    }

                    // ECO and ECO-NPC should be friendly to each other
                    if (faction.Value.Tag == "ECO")
                    {
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions("ECO", "ECO-NPC", 1500);
                    }

                    if (faction.Value.Tag == "ECO-NPC")
                    {
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions("ECO-NPC", "ECO", 1500);
                    }

                    // GLF and GLF-NPC should be friendly to each other
                    if (faction.Value.Tag == "GLF")
                    {
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions("GLF", "GLF-NPC", 1500);
                    }

                    if (faction.Value.Tag == "GLF-NPC")
                    {
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions("GLF-NPC", "GLF", 1500);
                    }

                    // GLF and GLF-NPC should always be hostile to ECO and ECO-NPC
                    if (faction.Value.Tag == "GLF" || faction.Value.Tag == "GLF-NPC")
                    {
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions(faction.Value.Tag, "ECO", -1500);
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions(faction.Value.Tag, "ECO-NPC", -1500);
                    }

                    // ECO and ECO-NPC should always be hostile to GLF and GLF-NPC
                    if (faction.Value.Tag == "ECO" || faction.Value.Tag == "ECO-NPC")
                    {
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions(faction.Value.Tag, "GLF", -1500);
                        MyVisualScriptLogicProvider.SetRelationBetweenFactions(faction.Value.Tag, "GLF-NPC", -1500);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("Relations Main error: " + ex.ToString());
            }
        }
    }
}
