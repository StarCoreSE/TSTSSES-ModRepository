using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;

namespace SuitOrganicInducer
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), true, new string[] { "SuitOrganicInducer", "SmallSuitOrganicInducer" })]
    public class SuitOrganicInducer : MyGameLogicComponent
    {

        private VRage.ObjectBuilders.MyObjectBuilder_EntityBase _objectBuilder;

        public override void Init(VRage.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder)
        {

            _objectBuilder = objectBuilder;

            var SuitOrganicInducer = (Entity as IMyBeacon);

            if (SuitOrganicInducer != null && (SuitOrganicInducer.BlockDefinition.SubtypeId.Equals("SuitOrganicInducer") || SuitOrganicInducer.BlockDefinition.SubtypeId.Equals("SmallSuitOrganicInducer")))
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
                    if (Entity != null) ((IMyBeacon)Entity).HudText = "Charging Suit Energy...";
                    var targetentities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
                    foreach (VRage.ModAPI.IMyEntity entity in targetentities)
                    {
                        var player = entity as IMyCharacter;
                        if (player != null)
                        {
                            var playerid = player.ControllerInfo.ControllingIdentityId;
                            var elevel = MyVisualScriptLogicProvider.GetPlayersEnergyLevel(playerid);
                            elevel += 0.01f;
                            if(elevel >= 1)
                            {
                                MyVisualScriptLogicProvider.SetPlayersEnergyLevel(playerid,1);
                            }
                            else
                            {
                                MyVisualScriptLogicProvider.SetPlayersEnergyLevel(playerid,elevel);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}