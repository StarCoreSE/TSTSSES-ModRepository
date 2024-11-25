using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using VRage.Utils;
using System.Collections.Generic;
using System.Linq;
using VRage.ObjectBuilders;

namespace SuitOrganicConductor
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, "SuitOrganicConductor", "SmallSuitOrganicConductor")]
    public class SuitOrganicConductor : MyGameLogicComponent
    {
        private const float DamageAmount = 1f;
        private IMyBeacon _conductorBlock;
        private Dictionary<long, int> _characterDrawFrames = new Dictionary<long, int>();
        private const int DrawFramesDuration = 30; // Draw for half a second (30 frames)
        private static readonly MyStringId MaterialSquare = MyStringId.GetOrCompute("Square");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _conductorBlock = Entity as IMyBeacon;

            if (_conductorBlock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            if (_conductorBlock?.CubeGrid?.Physics == null)
                return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                if (_conductorBlock.IsWorking)
                {
                    BoundingSphereD sphere = new BoundingSphereD(_conductorBlock.GetPosition(), _conductorBlock.Radius);
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
                                var health = MyVisualScriptLogicProvider.GetPlayersHealth(playerid);
                                health -= DamageAmount;

                                _characterDrawFrames[character.EntityId] = DrawFramesDuration;

                                if (health <= 0)
                                {
                                    MyVisualScriptLogicProvider.SetPlayersHealth(playerid, 0);
                                }
                                else
                                {
                                    MyVisualScriptLogicProvider.SetPlayersHealth(playerid, health);
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"SuitOrganicConductor: Error in UpdateAfterSimulation100: {e}");
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
                MyLog.Default.WriteLineAndConsole($"SuitOrganicConductor: Error in UpdateAfterSimulation: {e}");
            }
        }

        private void DrawDebugLinesToCharactersInRange()
        {
            if (_conductorBlock != null && _conductorBlock.IsWorking)
            {
                Vector3D sourcePosition = _conductorBlock.GetPosition();
                Vector4 red = Color.Red.ToVector4();

                foreach (var characterId in _characterDrawFrames.Keys)
                {
                    var character = MyAPIGateway.Entities.GetEntityById(characterId) as IMyCharacter;
                    if (character != null && !character.IsDead)
                    {
                        Vector3D characterPosition = character.GetPosition();
                        MySimpleObjectDraw.DrawLine(sourcePosition, characterPosition, MaterialSquare, ref red, 0.1f);
                    }
                }
            }
        }
    }
}