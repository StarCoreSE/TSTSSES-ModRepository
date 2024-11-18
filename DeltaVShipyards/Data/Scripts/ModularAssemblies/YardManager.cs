using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using Scripts.ModularAssemblies.Communication;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class YardManagerContainer : MySessionComponentBase
    {
        private YardManager _yardManager;
        private bool _isInitialized;

        public override void LoadData()
        {
            _yardManager = new YardManager();
        }

        public override void UpdateAfterSimulation()
        {
            if (!_isInitialized)
            {
                if (!ModularDefinition.ModularApi.IsReady)
                    return;

                _yardManager.Initialize(ModularDefinition.ModularApi);
                _isInitialized = true;
            }

            _yardManager.Update();
        }

        protected override void UnloadData()
        {
            _yardManager?.Cleanup();
        }
    }

    public class YardManager
    {
        private ModularDefinitionApi _api;
        private Dictionary<int, YardStructure> _yards = new Dictionary<int, YardStructure>();

        public void Initialize(ModularDefinitionApi api)
        {
            _api = api;
            _api.RegisterOnPartAdd("YardDefinition", OnPartAdd);
            _api.RegisterOnPartRemove("YardDefinition", OnPartRemove);
        }

        public void Update()
        {
            foreach (var yard in _yards.Values.ToList())
            {
                yard.Update();
            }
        }

        public void Cleanup()
        {
            _api.UnregisterOnPartAdd("YardDefinition", OnPartAdd);
            _api.UnregisterOnPartRemove("YardDefinition", OnPartRemove);
        }

        private void OnPartAdd(int assemblyId, IMyCubeBlock block, bool isBasePart)
        {
            if (!_yards.ContainsKey(assemblyId))
            {
                _yards[assemblyId] = new YardStructure(_api, assemblyId);
            }
            _yards[assemblyId].AddBlock(block);
        }

        private void OnPartRemove(int assemblyId, IMyCubeBlock block, bool isBasePart)
        {
            if (_yards.ContainsKey(assemblyId))
            {
                _yards[assemblyId].RemoveBlock(block);
                if (_api.GetMemberParts(assemblyId).Length == 0)
                {
                    _yards.Remove(assemblyId);
                }
            }
        }
    }

    public class YardStructure
    {
        private readonly ModularDefinitionApi _api;
        private readonly int _assemblyId;
        private List<IMyCubeBlock> _corners = new List<IMyCubeBlock>();
        private bool _isValid;

        public YardStructure(ModularDefinitionApi api, int assemblyId)
        {
            _api = api;
            _assemblyId = assemblyId;
        }

        public void AddBlock(IMyCubeBlock block)
        {
            if (block.BlockDefinition.SubtypeName == "ShipyardCorner_Large")
            {
                _corners.Add(block);
            }
            ValidateStructure();
        }

        public void RemoveBlock(IMyCubeBlock block)
        {
            if (block.BlockDefinition.SubtypeName == "ShipyardCorner_Large")
            {
                _corners.Remove(block);
            }
            ValidateStructure();
        }

        public void Update()
        {
            // Periodic validation or other updates if needed
        }

        private void ValidateStructure()
        {
            if (_corners.Count < 8)
            {
                SetValidState(false);
                return;
            }

            var connectionMap = BuildConnectionMap();
            bool isValid = ValidateConnections(connectionMap);

            SetValidState(isValid);
        }

        private Dictionary<IMyCubeBlock, List<IMyCubeBlock>> BuildConnectionMap()
        {
            var connectionMap = new Dictionary<IMyCubeBlock, List<IMyCubeBlock>>();

            foreach (var corner in _corners)
            {
                var connectedBlocks = _api.GetConnectedBlocks(corner, "YardDefinition");
                connectionMap[corner] = connectedBlocks
                    .Where(b => b.BlockDefinition.SubtypeName == "ShipyardCorner_Large" && b != corner)
                    .ToList();
            }

            return connectionMap;
        }

        private bool ValidateConnections(Dictionary<IMyCubeBlock, List<IMyCubeBlock>> connectionMap)
        {
            // Check if each corner has at least 7 connections
            foreach (var connections in connectionMap.Values)
            {
                if (connections.Count < 7)
                {
                    return false;
                }
            }

            // Additional geometric validation could be added here
            return true;
        }

        private void SetValidState(bool isValid)
        {
            if (_isValid != isValid)
            {
                _isValid = isValid;
                OnValidStateChanged();
            }
        }

        private void OnValidStateChanged()
        {
            if (_isValid)
            {
                MyAPIGateway.Utilities.ShowNotification($"Shipyard Assembly {_assemblyId} is now valid!", 2000);
                // Additional actions for valid state
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification($"Shipyard Assembly {_assemblyId} is no longer valid.", 2000);
                // Additional actions for invalid state
            }

            // You could also set an assembly property here
            _api.SetAssemblyProperty(_assemblyId, "IsValidYard", _isValid);
        }
    }
}