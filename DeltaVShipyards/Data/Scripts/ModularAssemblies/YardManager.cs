using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI;
using System.Linq;
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

            var existingAssemblies = _api.GetAllAssemblies();
            foreach (var assemblyId in existingAssemblies)
            {
                var parts = _api.GetMemberParts(assemblyId);
                if (parts != null && parts.Length > 0)
                {
                    _yards[assemblyId] = new YardStructure(_api, assemblyId);
                    foreach (var block in parts)
                    {
                        _yards[assemblyId].AddBlock(block);
                    }
                }
            }
        }

        public void Update()
        {
            foreach (var yard in _yards.Values)
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
        private List<IMyCubeBlock> _conveyors = new List<IMyCubeBlock>();
        private bool _isValid;
        private int _notificationTicks;
        private const int NOTIFICATION_INTERVAL = 300; // 5 seconds at 60 ticks per second

        public YardStructure(ModularDefinitionApi api, int assemblyId)
        {
            _api = api;
            _assemblyId = assemblyId;
        }

        private int _validationDelay = 0;
        private const int VALIDATION_DELAY_TICKS = 10;

        public void AddBlock(IMyCubeBlock block)
        {
            if (block.BlockDefinition.SubtypeName == "ShipyardCorner_Large")
                _corners.Add(block);
            else
                _conveyors.Add(block);

            _validationDelay = VALIDATION_DELAY_TICKS; // Set delay instead of immediate validation
        }

        public void RemoveBlock(IMyCubeBlock block)
        {
            if (block.BlockDefinition.SubtypeName == "ShipyardCorner_Large")
                _corners.Remove(block);
            else
                _conveyors.Remove(block);

            ValidateStructure();
        }

        public void Update()
        {
            if (_validationDelay > 0)
            {
                _validationDelay--;
                if (_validationDelay == 0)
                {
                    ValidateStructure();
                }
            }

            _notificationTicks++;
            if (_notificationTicks >= NOTIFICATION_INTERVAL)
            {
                if (_isValid)
                {
                    MyAPIGateway.Utilities.ShowMessage("Shipyard Status", $"Assembly {_assemblyId} is valid and operational.");
                }
                else if (_corners.Count > 0) // Only show invalid message if we have some corners
                {
                    string reason = GetInvalidReason();
                    MyAPIGateway.Utilities.ShowMessage("Shipyard Status",
                        $"Assembly {_assemblyId} is not valid. {reason}");
                }
                _notificationTicks = 0;
            }
        }

        private string GetInvalidReason()
        {
            if (_corners.Count < 8)
                return $"Need 8 corners, currently has {_corners.Count}.";

            var connectionMap = BuildConnectionMap();

            // Check corner connections
            var invalidCorners = _corners.Count(corner =>
                !connectionMap.ContainsKey(corner) || connectionMap[corner].Count < 3);
            if (invalidCorners > 0)
                return $"{invalidCorners} corners have insufficient connections (need 3 each).";

            // Check conveyor connections
            var invalidConveyors = _conveyors.Count(conveyor =>
                !connectionMap.ContainsKey(conveyor) || connectionMap[conveyor].Count < 2);
            if (invalidConveyors > 0)
                return $"{invalidConveyors} conveyors have insufficient connections (need 2 each).";

            // Check if all corners are connected
            var allConnected = new HashSet<IMyCubeBlock>();
            var toCheck = new Queue<IMyCubeBlock>();
            if (_corners.Count > 0)
            {
                toCheck.Enqueue(_corners[0]);
                allConnected.Add(_corners[0]);

                while (toCheck.Count > 0)
                {
                    var current = toCheck.Dequeue();
                    foreach (var connected in connectionMap[current])
                    {
                        if (allConnected.Add(connected) && connected is IMyCubeBlock)
                            toCheck.Enqueue(connected);
                    }
                }

                var disconnectedCorners = _corners.Count(c => !allConnected.Contains(c));
                if (disconnectedCorners > 0)
                    return $"{disconnectedCorners} corners are not connected to the main structure.";
            }

            return "Unknown issue.";
        }

        private void ValidateStructure()
        {
            bool isValid = false;
            if (_corners.Count == 8)
            {
                var connectionMap = BuildConnectionMap();
                _api.Log($"Connection map built with {connectionMap.Count} entries");

                foreach (var corner in _corners)
                {
                    var connections = connectionMap.ContainsKey(corner) ? connectionMap[corner].Count : 0;
                    _api.Log($"Corner {corner.EntityId} has {connections} connections");
                }

                isValid = ValidateConnections(connectionMap);
            }

            _api.Log($"Validation result: {isValid} (Corners: {_corners.Count})");
            SetValidState(isValid);
        }

        private Dictionary<IMyCubeBlock, List<IMyCubeBlock>> BuildConnectionMap()
        {
            var connectionMap = new Dictionary<IMyCubeBlock, List<IMyCubeBlock>>();

            foreach (var corner in _corners)
            {
                var connectedBlocks = _api.GetConnectedBlocks(corner, "YardDefinition");
                connectionMap[corner] = connectedBlocks.ToList();
            }

            foreach (var conveyor in _conveyors)
            {
                var connectedBlocks = _api.GetConnectedBlocks(conveyor, "YardDefinition");
                connectionMap[conveyor] = connectedBlocks.ToList();
            }

            return connectionMap;
        }

        private bool ValidateConnections(Dictionary<IMyCubeBlock, List<IMyCubeBlock>> connectionMap)
        {
            // Check if each corner has at least 3 connections
            foreach (var corner in _corners)
            {
                if (!connectionMap.ContainsKey(corner) || connectionMap[corner].Count < 3)
                    return false;
            }

            // Check if each conveyor has at least 2 connections
            foreach (var conveyor in _conveyors)
            {
                if (!connectionMap.ContainsKey(conveyor) || connectionMap[conveyor].Count < 2)
                    return false;
            }

            // Check if all corners are connected in a single structure
            var allConnected = new HashSet<IMyCubeBlock>();
            var toCheck = new Queue<IMyCubeBlock>();

            toCheck.Enqueue(_corners[0]);
            allConnected.Add(_corners[0]);

            while (toCheck.Count > 0)
            {
                var current = toCheck.Dequeue();
                foreach (var connected in connectionMap[current])
                {
                    if (allConnected.Add(connected))
                        toCheck.Enqueue(connected);
                }
            }

            return _corners.All(c => allConnected.Contains(c));
        }

        private void SetValidState(bool isValid)
        {
            if (_isValid != isValid)
            {
                _isValid = isValid;
                _api.SetAssemblyProperty(_assemblyId, "IsValidYard", _isValid);
                OnValidStateChanged();
            }
        }

        private void OnValidStateChanged()
        {
            if (_isValid)
            {
                MyAPIGateway.Utilities.ShowMessage("Shipyard Status",
                    $"Assembly {_assemblyId} is now valid and operational!");
            }
            else
            {
                MyAPIGateway.Utilities.ShowMessage("Shipyard Status",
                    $"Assembly {_assemblyId} is no longer valid. {GetInvalidReason()}");
            }
        }
    }
}