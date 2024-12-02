using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI;
using System.Linq;
using Scripts.ModularAssemblies.Communication;
using VRage.Game.Entity;
using VRage.Game;
using VRage.Utils;
using VRageRender;
using static VRageRender.MyBillboard;
using System;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class YardManagerContainer : MySessionComponentBase
    {
        private YardManager _yardManager;
        private bool _isInitialized;
        private ModularDefinitionApi _api;
        private Dictionary<int, YardStructure> _yards = new Dictionary<int, YardStructure>();


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

        private BoundingBoxD _yardSpace;
        private List<IMyCubeGrid> _containedGrids = new List<IMyCubeGrid>();
        private Vector3D _minCorner, _maxCorner;

        private int _validationDelay = 0;
        private const int VALIDATION_DELAY_TICKS = 10;

        private const int DEBUG_DRAW_TICKS = 60; // Draw every 60 ticks (1 second at 60 FPS)
        private int _debugDrawCounter = 0;
        private MyEntity _referenceEntity;

        public YardStructure(ModularDefinitionApi api, int assemblyId)
        {
            _api = api;
            _assemblyId = assemblyId;
        }

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

        private bool IsClient => MyAPIGateway.Multiplayer?.IsServer == false && MyAPIGateway.Utilities.IsDedicated == false;

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
                    UpdateContainedGrids();
                    if (IsClient) // Only show messages on client
                    {
                        MyAPIGateway.Utilities.ShowMessage("Shipyard Status",
                            $"Assembly {_assemblyId} is valid and operational.\n" +
                            $"Contains {_containedGrids.Count} grids\n" +
                            $"Volume: {_yardSpace.Volume:N0} m³");
                    }
                }
                else if (_corners.Count > 0)
                {
                    string reason = GetInvalidReason();
                    if (IsClient) // Only show messages on client
                    {
                        MyAPIGateway.Utilities.ShowMessage("Shipyard Status",
                            $"Assembly {_assemblyId} is no longer valid. {reason}");
                    }
                }
                _notificationTicks = 0;
            }

            // Only draw debug visuals on client
            if (IsClient && _isValid)
            {
                DrawDebugBox();
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

                if (isValid)
                {
                    CalculateYardSpace();
                    UpdateContainedGrids();
                }
            }

            _api.Log($"Validation result: {isValid} (Corners: {_corners.Count})");
            SetValidState(isValid);
        }

        public IReadOnlyList<IMyCubeGrid> GetContainedGrids()
        {
            return _containedGrids.AsReadOnly();
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

        private void CalculateYardSpace()
        {
            if (_corners.Count != 8 || !_isValid) return;

            var positions = _corners.Select(c => c.WorldMatrix.Translation).ToList();

            if (positions.All(p => p == Vector3D.Zero))
            {
                MyAPIGateway.Utilities.ShowMessage("Yard Error", "All corner positions are at 0,0,0. Something is wrong with block positions.");
                return;
            }

            _minCorner = new Vector3D(
                positions.Min(p => p.X),
                positions.Min(p => p.Y),
                positions.Min(p => p.Z)
            );

            _maxCorner = new Vector3D(
                positions.Max(p => p.X),
                positions.Max(p => p.Y),
                positions.Max(p => p.Z)
            );

            _yardSpace = new BoundingBoxD(_minCorner, _maxCorner);
            Vector3D center = (_minCorner + _maxCorner) * 0.5;

            MyAPIGateway.Utilities.ShowMessage("Yard Coordinates",
                $"Min: {_minCorner.ToString("F2")}\n" +
                $"Max: {_maxCorner.ToString("F2")}\n" +
                $"Center: {center.ToString("F2")}\n" +
                $"Size: {_yardSpace.Size.ToString("F2")}");

            // Debug each corner position
            for (int i = 0; i < _corners.Count; i++)
            {
                var pos = _corners[i].WorldMatrix.Translation;
                MyAPIGateway.Utilities.ShowMessage($"Corner {i}",
                    $"Position: {pos.ToString("F2")}");
            }
        }

        private void UpdateContainedGrids()
        {
            if (!_isValid) return;

            _containedGrids.Clear();

            // Get the grid that the yard is built on
            var yardGrid = _corners[0].CubeGrid;

            // Get all grids connected to the yard grid
            var gridGroup = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(yardGrid, GridLinkTypeEnum.Mechanical, gridGroup);

            foreach (var grid in gridGroup)
            {
                if (grid == yardGrid) // Skip the yard grid itself
                    continue;

                Vector3D gridCenter = grid.WorldVolume.Center;
                bool isInBox = _yardSpace.Contains(gridCenter) != ContainmentType.Disjoint;

                MyAPIGateway.Utilities.ShowMessage($"Grid Check: {grid.DisplayName}",
                    $"Grid Center: {gridCenter.ToString("F2")}\n" +
                    $"Is in box: {isInBox}\n" +
                    $"Yard Center: {_yardSpace.Center.ToString("F2")}");

                if (isInBox)
                {
                    _containedGrids.Add(grid);
                }
            }

            MyAPIGateway.Utilities.ShowMessage("Shipyard Contents",
                $"Found {_containedGrids.Count} grids inside yard\n" +
                $"Total grids checked: {gridGroup.Count}\n" +
                $"Yard Center: {_yardSpace.Center.ToString("F2")}");
        }

        private void DrawDebugBox()
        {
            if (_yardSpace.Size == Vector3D.Zero || !IsClient)
                return;

            try
            {
                Vector3D center = _yardSpace.Center;
                Vector3D size = _yardSpace.Size;
                Vector4 color = new Vector4(0, 1, 0, 1); // Green

                // Draw all 12 edges of the box
                Vector3D[] corners = new Vector3D[8];
                _yardSpace.GetCorners(corners);

                // Bottom square
                for (int i = 0; i < 4; i++)
                {
                    MySimpleObjectDraw.DrawLine(
                        corners[i],
                        corners[(i + 1) % 4],
                        MyStringId.GetOrCompute("Square"),
                        ref color,
                        0.2f);
                }

                // Top square
                for (int i = 0; i < 4; i++)
                {
                    MySimpleObjectDraw.DrawLine(
                        corners[i + 4],
                        corners[((i + 1) % 4) + 4],
                        MyStringId.GetOrCompute("Square"),
                        ref color,
                        0.2f);
                }

                // Vertical lines
                for (int i = 0; i < 4; i++)
                {
                    MySimpleObjectDraw.DrawLine(
                        corners[i],
                        corners[i + 4],
                        MyStringId.GetOrCompute("Square"),
                        ref color,
                        0.2f);
                }

                // Draw center point
                Vector4 centerColor = new Vector4(1, 1, 0, 1); // Yellow
                MySimpleObjectDraw.DrawLine(
                    center - Vector3D.Up * 2,
                    center + Vector3D.Up * 2,
                    MyStringId.GetOrCompute("Square"),
                    ref centerColor,
                    0.3f);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("Debug Draw Error", e.Message);
            }
        }
    }
}