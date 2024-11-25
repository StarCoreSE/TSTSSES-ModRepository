using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;

namespace TeslaWirelessPower
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), true, new string[] { "TeslaWirelessPower", "SmallTeslaWirelessPower" })]
    public class TeslaWirelessPower : MyGameLogicComponent
    {

        private VRage.ObjectBuilders.MyObjectBuilder_EntityBase _objectBuilder;

        public override void Init(VRage.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder)
        {

            _objectBuilder = objectBuilder;

            var TeslaWirelessPower = (Entity as IMyBeacon);

            if (TeslaWirelessPower != null && (TeslaWirelessPower.BlockDefinition.SubtypeId.Equals("TeslaWirelessPower") || TeslaWirelessPower.BlockDefinition.SubtypeId.Equals("SmallTeslaWirelessPower")))
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
                if ((Entity as IMyBeacon).IsWorking)
                {
                    BoundingSphereD sphere = new BoundingSphereD((Entity as IMyBeacon).GetPosition(), (Entity as IMyBeacon).Radius);
                    (Entity as IMyBeacon).HudText = "Tesla Wireless Power";
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
