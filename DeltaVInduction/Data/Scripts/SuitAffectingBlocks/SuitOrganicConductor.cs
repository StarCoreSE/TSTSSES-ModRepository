using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace SuitOrganicConductor
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, "SuitOrganicConductor", "SmallSuitOrganicConductor")]
    public class SuitOrganicConductor : MyGameLogicComponent
    {
        private const float DamageAmount = 1f;
        private const float ImpulseStrength = 1000f; // Increased by 100x
        private IMyBeacon _conductorBlock;
        private Dictionary<long, CharacterData> _characterData = new Dictionary<long, CharacterData>();
        private const int ChargeUpTime = 30; // 3 seconds * 10 updates per second
        private static readonly MyStringId MaterialSquare = MyStringId.GetOrCompute("Square");

        private class CharacterData
        {
            public int ChargeUpCounter { get; set; }
            public bool IsCharged { get; set; }
        }

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
            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateAfterSimulation10()
        {
            try
            {
                if (_conductorBlock.IsWorking)
                {
                    _conductorBlock.HudText = "Charging up...";

                    BoundingSphereD sphere = new BoundingSphereD(_conductorBlock.GetPosition(), _conductorBlock.Radius);
                    var targetentities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);

                    foreach (IMyEntity entity in targetentities)
                    {
                        var character = entity as IMyCharacter;
                        if (character != null && !character.IsDead)
                        {
                            if (!_characterData.ContainsKey(character.EntityId))
                            {
                                _characterData[character.EntityId] = new CharacterData();
                            }

                            var data = _characterData[character.EntityId];
                            if (!data.IsCharged)
                            {
                                data.ChargeUpCounter++;
                                if (data.ChargeUpCounter >= ChargeUpTime)
                                {
                                    data.IsCharged = true;
                                }
                            }
                            else
                            {
                                var controllingPlayer = character.ControllerInfo?.ControllingIdentityId;
                                if (controllingPlayer.HasValue)
                                {
                                    var playerid = controllingPlayer.Value;
                                    var health = MyVisualScriptLogicProvider.GetPlayersHealth(playerid);
                                    health -= DamageAmount;

                                    if (health <= 0)
                                    {
                                        MyVisualScriptLogicProvider.SetPlayersHealth(playerid, 0);
                                        MyVisualScriptLogicProvider.CreateLightning();
                                    }
                                    else
                                    {
                                        MyVisualScriptLogicProvider.SetPlayersHealth(playerid, health);
                                    }

                                    // Apply impulse
                                    Vector3D impulseDirection = Vector3D.Normalize(character.GetPosition() - _conductorBlock.GetPosition());
                                    character.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, impulseDirection * ImpulseStrength, null, null);

                                    // Reset charge
                                    data.IsCharged = false;
                                    data.ChargeUpCounter = 0;
                                }
                            }
                        }
                    }

                    // Remove characters that are no longer in range
                    var charactersToRemove = _characterData.Keys.Where(id => !targetentities.Any(e => e.EntityId == id)).ToList();
                    foreach (var id in charactersToRemove)
                    {
                        _characterData.Remove(id);
                    }
                }
            }
            catch (System.Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"SuitOrganicConductor: Error in UpdateAfterSimulation10: {e}");
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session?.Player != null)
                {
                    DrawDebugLinesToCharactersInRange();
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

                foreach (var kvp in _characterData)
                {
                    var character = MyAPIGateway.Entities.GetEntityById(kvp.Key) as IMyCharacter;
                    if (character != null && !character.IsDead)
                    {
                        Vector3D characterPosition = character.GetPosition();
                        float chargeProgress = (float)kvp.Value.ChargeUpCounter / ChargeUpTime;
                        Vector4 color = Vector4.Lerp(Color.Green.ToVector4(), Color.Red.ToVector4(), chargeProgress);
                        MySimpleObjectDraw.DrawLine(sourcePosition, characterPosition, MaterialSquare, ref color, 0.1f);
                    }
                }
            }
        }
    }
}