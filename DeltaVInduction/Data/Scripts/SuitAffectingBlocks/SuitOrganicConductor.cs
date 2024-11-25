using VRage.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRageRender;
using System.Collections.Generic;
using VRage.Game;
using VRage.Utils;

namespace SuitOrganicConductor
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), true, new string[] { "SuitOrganicConductor", "SmallSuitOrganicConductor" })]
    public class SuitOrganicConductor : MyGameLogicComponent
    {
        private VRage.ObjectBuilders.MyObjectBuilder_EntityBase _objectBuilder;
        private const float DamageAmount = 1f;
        private IMyBeacon _conductorBlock;
        private List<IMyCharacter> _charactersInRange = new List<IMyCharacter>();

        public override void Init(VRage.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder)
        {
            _objectBuilder = objectBuilder;

            _conductorBlock = Entity as IMyBeacon;

            if (_conductorBlock != null && (_conductorBlock.BlockDefinition.SubtypeId.Equals("SuitOrganicConductor") || _conductorBlock.BlockDefinition.SubtypeId.Equals("SmallSuitOrganicConductor")))
            {
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
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
                if (_conductorBlock.IsWorking)
                {
                    UpdateCharactersInRange();
                    DealDamageToCharacters();
                }
            }
            catch { }
        }

        private void UpdateCharactersInRange()
        {
            _charactersInRange.Clear();
            BoundingSphereD sphere = new BoundingSphereD(_conductorBlock.GetPosition(), _conductorBlock.Radius);
            var targetentities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
            foreach (VRage.ModAPI.IMyEntity entity in targetentities)
            {
                var character = entity as IMyCharacter;
                if (character != null && !character.IsDead)
                {
                    _charactersInRange.Add(character);
                }
            }
        }

        private void DealDamageToCharacters()
        {
            foreach (var character in _charactersInRange)
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
                    }
                    else
                    {
                        MyVisualScriptLogicProvider.SetPlayersHealth(playerid, health);
                    }
                }
            }
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            DrawDebugLinesToCharactersInRange();
        }

        private void DrawDebugLinesToCharactersInRange()
        {
            if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session != null && _conductorBlock != null && _conductorBlock.IsWorking)
            {
                Vector3D sourcePosition = _conductorBlock.GetPosition();
                Vector4 red = Color.Red.ToVector4();

                foreach (var character in _charactersInRange)
                {
                    Vector3D characterPosition = character.GetPosition();
                    MySimpleObjectDraw.DrawLine(sourcePosition, characterPosition, MyStringId.GetOrCompute("Square"), ref red, 0.1f);
                }
            }
        }
    }
}