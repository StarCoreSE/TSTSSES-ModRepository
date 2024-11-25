using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using System.Collections.Generic;
using System.Linq;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace SuitOrganicInducer
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, "SuitOrganicInducer", "SmallSuitOrganicInducer")]
    public class SuitOrganicInducer : MyGameLogicComponent
    {
        private const float ChargeAmount = 0.01f;
        private IMyBeacon _inducerBlock;
        private Dictionary<long, int> _characterDrawFrames = new Dictionary<long, int>();
        private const int DrawFramesDuration = 30; // Draw for half a second (30 frames)
        private static readonly MyStringId MaterialSquare = MyStringId.GetOrCompute("Square");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _inducerBlock = Entity as IMyBeacon;

            if (_inducerBlock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            if (_inducerBlock?.CubeGrid?.Physics == null)
                return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                if (_inducerBlock.IsWorking)
                {
                    _inducerBlock.HudText = "Charging Suit Energy...";
                    BoundingSphereD sphere = new BoundingSphereD(_inducerBlock.GetPosition(), _inducerBlock.Radius);
                    var targetentities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);

                    foreach (IMyEntity entity in targetentities)
                    {
                        var character = entity as IMyCharacter;
                        if (character != null && !character.IsDead)
                        {
                            var controllingPlayer = character.ControllerInfo?.ControllingIdentityId;
                            if (controllingPlayer.HasValue)
                            {
                                var playerid = controllingPlayer.Value;
                                var elevel = MyVisualScriptLogicProvider.GetPlayersEnergyLevel(playerid);
                                elevel += ChargeAmount;

                                // Set draw frames for this character when charged
                                _characterDrawFrames[character.EntityId] = DrawFramesDuration;

                                if (elevel >= 1)
                                {
                                    MyVisualScriptLogicProvider.SetPlayersEnergyLevel(playerid, 1);
                                }
                                else
                                {
                                    MyVisualScriptLogicProvider.SetPlayersEnergyLevel(playerid, elevel);
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"SuitOrganicInducer: Error in UpdateAfterSimulation100: {e}");
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session?.Player != null)
                {
                    DrawDebugLinesToCharactersInRange();

                    var expiredEntries = new List<long>();
                    foreach (var kvp in _characterDrawFrames.ToList())
                    {
                        _characterDrawFrames[kvp.Key] = kvp.Value - 1;
                        if (_characterDrawFrames[kvp.Key] <= 0)
                        {
                            expiredEntries.Add(kvp.Key);
                        }
                    }
                    foreach (var expiredEntry in expiredEntries)
                    {
                        _characterDrawFrames.Remove(expiredEntry);
                    }
                }
            }
            catch (System.Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"SuitOrganicInducer: Error in UpdateAfterSimulation: {e}");
            }
        }

        private void DrawDebugLinesToCharactersInRange()
        {
            if (_inducerBlock != null && _inducerBlock.IsWorking)
            {
                Vector3D sourcePosition = _inducerBlock.GetPosition();
                Vector4 blue = Color.Blue.ToVector4();

                foreach (var characterId in _characterDrawFrames.Keys)
                {
                    var character = MyAPIGateway.Entities.GetEntityById(characterId) as IMyCharacter;
                    if (character != null && !character.IsDead)
                    {
                        Vector3D characterPosition = character.GetPosition();
                        MySimpleObjectDraw.DrawLine(sourcePosition, characterPosition, MaterialSquare, ref blue, 0.1f);
                    }
                }
            }
        }
    }
}