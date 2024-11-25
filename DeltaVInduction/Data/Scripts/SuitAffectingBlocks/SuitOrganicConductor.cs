using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;

namespace SuitOrganicConductor
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), true, new string[] { "SuitOrganicConductor", "SmallSuitOrganicConductor" })]
    public class SuitOrganicConductor : MyGameLogicComponent
    {
        private VRage.ObjectBuilders.MyObjectBuilder_EntityBase _objectBuilder;

        public override void Init(VRage.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder)
        {
            _objectBuilder = objectBuilder;

            var suitOrganicConductor = (Entity as IMyBeacon);

            if (suitOrganicConductor != null && (suitOrganicConductor.BlockDefinition.SubtypeId.Equals("SuitOrganicConductor") || suitOrganicConductor.BlockDefinition.SubtypeId.Equals("SmallSuitOrganicConductor")))
            {
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override VRage.ObjectBuilders.MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return _objectBuilder;
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                if (((IMyBeacon)Entity).IsWorking)
                {
                    BoundingSphereD sphere = new BoundingSphereD(((IMyBeacon)Entity).GetPosition(), ((IMyBeacon)Entity).Radius);
                    if (Entity != null) ((IMyBeacon)Entity).HudText = "Dealing damage to suits...";
                    var targetentities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
                    foreach (VRage.ModAPI.IMyEntity entity in targetentities)
                    {
                        var player = entity as IMyCharacter;
                        if (player != null)
                        {
                            var playerid = player.ControllerInfo.ControllingIdentityId;
                            var health = MyVisualScriptLogicProvider.GetPlayersHealth(playerid);
                            health -= 5f; // Decrease health by 1 point
                            if (health <= 0)
                            {
                                MyVisualScriptLogicProvider.SetPlayersHealth(playerid, 0);
                                // You might want to add logic here to handle player death
                            }
                            else
                            {
                                MyVisualScriptLogicProvider.SetPlayersHealth(playerid, health);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}