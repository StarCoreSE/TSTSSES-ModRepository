using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Definitions;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using NaniteConstructionSystem.Particles;
using NaniteConstructionSystem.Extensions;
using NaniteConstructionSystem.Entities.Beacons;
using VRage.Voxels;
using VRage.ModAPI;

namespace NaniteConstructionSystem.Entities.Targets
{
    public class NaniteMiningTarget
    {
        public int ParticleCount { get; set; }
        public double StartTime { get; set; }
        public double CarryTime { get; set; }
        public double LastUpdate { get; set; }
    }

    public class NaniteMiningItem
    {
        public byte VoxelMaterial { get; set; }
        public Vector3D Position { get; set; }
        public Vector3I VoxelPosition { get; set; }
        public MyVoxelMaterialDefinition Definition { get; set; }
        public long VoxelId { get; set; }
        public float Amount { get; set; }
    }

    public class NaniteMiningTargets : NaniteTargetBlocksBase
    {
        public override string TargetName
        {
            get { return "Mining"; }
        }

        private ConcurrentQueue<NaniteMiningItem> m_potentialMiningTargets = new ConcurrentQueue<NaniteMiningItem>();
        private List<NaniteMiningItem> alreadyCreatedMiningTarget = new List<NaniteMiningItem>();
        private Dictionary<long, IMyEntity> voxelEntities = new Dictionary<long, IMyEntity>();
        private ConcurrentBag<NaniteMiningItem> finalAddList = new ConcurrentBag<NaniteMiningItem>();
        private float m_maxDistance = 500f;
        private ConcurrentDictionary<NaniteMiningItem, NaniteMiningTarget> m_targetTracker;
        private static HashSet<Vector3D> m_globalPositionList;
        private Random rnd;
        private int m_oldMinedPositionsCount;
        private int m_scannertimeout;
        private int m_minedPositionsCount;
        private string beaconDataCode = "";

        public NaniteMiningTargets(NaniteConstructionBlock constructionBlock) : base(constructionBlock)
        {
            m_maxDistance = NaniteConstructionManager.Settings.MiningMaxDistance;
            m_targetTracker = new ConcurrentDictionary<NaniteMiningItem, NaniteMiningTarget>();
            m_globalPositionList = new HashSet<Vector3D>();
            rnd = new Random();
        }

        public override void ClearInternalTargetList()
        {
            ResetMiningTargetsAndRescan();
        }

        public override int GetMaximumTargets()
        {
            return (int)Math.Min((NaniteConstructionManager.Settings.MiningNanitesNoUpgrade *
                                  m_constructionBlock.FactoryGroup.Count)
                                 + m_constructionBlock.UpgradeValue("MiningNanites"),
                NaniteConstructionManager.Settings.MiningMaxStreams);
        }

        public override float GetPowerUsage()
        {
            return Math.Max(1, NaniteConstructionManager.Settings.MiningPowerPerStream
                               - (int)m_constructionBlock.UpgradeValue("PowerNanites"));
        }

        public override float GetMinTravelTime()
        {
            return Math.Max(1f, NaniteConstructionManager.Settings.MiningMinTravelTime
                                - m_constructionBlock.UpgradeValue("MinTravelTime"));
        }

        public override float GetSpeed()
        {
            return NaniteConstructionManager.Settings.MiningDistanceDivisor
                   + m_constructionBlock.UpgradeValue("SpeedNanites");
        }

        public override bool IsEnabled(NaniteConstructionBlock factory)
        {
            if (((IMyFunctionalBlock)factory.ConstructionBlock) == null || !((IMyFunctionalBlock)factory
                                                                            .ConstructionBlock).Enabled
                                                                        || !((IMyFunctionalBlock)factory
                                                                            .ConstructionBlock).IsFunctional
                                                                        || (NaniteConstructionManager.TerminalSettings
                                                                                .ContainsKey(factory.ConstructionBlock
                                                                                    .EntityId)
                                                                            && !NaniteConstructionManager
                                                                                .TerminalSettings[
                                                                                    factory.ConstructionBlock.EntityId]
                                                                                .AllowMining))
            {
                factory.EnabledParticleTargets[TargetName] = false;
                return false;
            }

            factory.EnabledParticleTargets[TargetName] = true;
            return true;
        }

        private static void ClampVoxelCoord(IMyStorage storage, ref Vector3I voxelCoord, int distance = 1)
        {
            if (storage == null) return;
            Vector3I newSize = storage.Size - distance;
            Vector3I.Clamp(ref voxelCoord, ref Vector3I.Zero, ref newSize, out voxelCoord);
        }

        private static void ComputeSphereBounds(
            MyVoxelBase voxelMap,
            ref BoundingSphereD shapeSphere,
            out Vector3I voxelMin,
            out Vector3I voxelMax)
        {
            // Get the voxel map's world matrix and compute its transpose
            MatrixD worldMatrixTranspose = MatrixD.Transpose(voxelMap.WorldMatrix);

            // Calculate min and max world positions based on the bounding sphere
            Vector3D minWorld = shapeSphere.Center - new Vector3D(shapeSphere.Radius);
            Vector3D maxWorld = shapeSphere.Center + new Vector3D(shapeSphere.Radius);

            // Get the reference world position (voxel map's minimum corner)
            // Vector3D referenceWorldPosition = voxelMap.PositionLeftBottomCorner;
            Vector3D referenceWorldPosition = voxelMap.PositionLeftBottomCorner;

            // Convert world positions into directions relative to the voxel map's reference position
            Vector3D minWorldDirection = minWorld - referenceWorldPosition;
            Vector3D maxWorldDirection = maxWorld - referenceWorldPosition;

            // Transform world directions into local (body) directions using the transposed world matrix
            Vector3D localMin = Vector3D.TransformNormal(minWorldDirection, worldMatrixTranspose);
            Vector3D localMax = Vector3D.TransformNormal(maxWorldDirection, worldMatrixTranspose);

            // Convert local positions to voxel coordinates by flooring them
            Vector3I floorMin = Vector3I.Floor(localMin);
            Vector3I floorMax = Vector3I.Floor(localMax);

            // Correct the voxel coordinates by ensuring min is less than max for all axes
            voxelMin = new Vector3I(Math.Min(floorMin.X, floorMax.X), Math.Min(floorMin.Y, floorMax.Y), Math.Min(floorMin.Z, floorMax.Z));
            voxelMax = new Vector3I(Math.Max(floorMin.X, floorMax.X), Math.Max(floorMin.Y, floorMax.Y), Math.Max(floorMin.Z, floorMax.Z));

            voxelMin += voxelMap.StorageMin;
            voxelMax += voxelMap.StorageMin + 2;
            
            // Ensure the voxel coordinates are within the valid range of the voxel storage
            voxelMap.Storage.ClampVoxel(ref voxelMin);
            voxelMap.Storage.ClampVoxel(ref voxelMax);
        }

        private static bool IsAlignedWithGlobal(MatrixD worldMatrix)
        {
            // Define the global up direction (Y-axis in global coordinate system)
            Vector3D globalUpDirection = Vector3D.Up;

            // Get the up direction of the voxel map's world matrix
            Vector3D voxelMapUpDirection = worldMatrix.Up;

            // Calculate the dot product between the global up direction and the voxel map's up direction
            double dotProduct = Vector3D.Dot(voxelMapUpDirection, globalUpDirection);

            // If the dot product is close to 1 or -1, the vectors are parallel or anti-parallel, indicating alignment
            // Here, we use a small tolerance to account for floating-point precision errors
            return Math.Abs(dotProduct - 1.0) < 1e-3 || Math.Abs(dotProduct + 1.0) < 1e-3;
        }

        public static void VoxelCoordToWorldPosition(
            Vector3I voxelCoord, 
            MyVoxelBase voxelMap, 
            out Vector3D worldPosition)
        {
            // Convert voxel coordinate to local space of the voxel map
            Vector3D localPosition = (voxelCoord * voxelMap.VoxelSize) - voxelMap.SizeInMetresHalf;

            // Add the position of the voxel map to convert to world space
            worldPosition = Vector3D.Transform(localPosition, voxelMap.WorldMatrix);
        }

        public override void ParallelUpdate(List<IMyCubeGrid> gridList, ConcurrentBag<BlockTarget> gridBlocks)
        {
            try
            {
                finalAddList = new ConcurrentBag<NaniteMiningItem>();

                MyAPIGateway.Parallel.Start(() =>
                {
                    DateTime start = DateTime.Now;

                    if (!IsEnabled(m_constructionBlock))
                    {
                        m_potentialMiningTargets = new ConcurrentQueue<NaniteMiningItem>();
                        return;
                    }

                    if (m_potentialMiningTargets.Count < 500 && finalAddList.Count < 500)
                    {
                        // DATA Z DETECTORU, teď z mining beaconu a nasypat je do finalAddList
                        var newBeaconDataCode = "";
                        foreach (var beaconBlock in NaniteConstructionManager.BeaconList.Where(x =>
                                     x.Value is NaniteBeaconMine))
                        {
                            var item = beaconBlock.Value.BeaconBlock;
                            var beaconElement = beaconBlock.Value as NaniteBeaconMine;

                            if (beaconElement == null)
                                continue;

                            if (item != null && !item.Enabled && item.IsFunctional &&
                                (beaconElement.stopTick + 18000) < m_constructionBlock.UpdateCount)
                            {
                                item.Enabled = true;
                            }

                            if (item == null || !item.Enabled || !item.IsFunctional
                                || !MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(
                                    item.GetUserRelationToOwner(m_constructionBlock.ConstructionBlock.OwnerId))
                                || !IsInRange(item.GetPosition(), m_maxDistance))
                                continue;

                            var beaconFoundNoData = true;
                            var beaconWasScanning = false;
                            int beaconData = 0;

                            if (!int.TryParse(item.CustomData, out beaconData))
                            {
                                item.CustomData = "";
                            }

                            if ((beaconData - 1) >= 0 && (beaconData - 1) < NaniteConstructionManager.OreList.Count)
                            {
                                // pass
                            }
                            else
                            {
                                beaconData = 0;
                                item.CustomData = "";
                            }

                            newBeaconDataCode += item.CustomData.ToString();

                            // if grid is not named, name it
                            IMyCubeGrid parentGrid = item.CubeGrid;
                            if (parentGrid == null)
                                continue;

                            string gridCustomName = parentGrid.CustomName;
                            if (gridCustomName.Contains("Large Grid") || gridCustomName.Contains("Small Grid") ||
                                gridCustomName.Contains("Static Grid"))
                            {
                                parentGrid.CustomName = "Nanite Mining Beacon";
                            }

                            // valid friendly mining beacon in range
                            //get all the materials if we have less than 500 targets

                            var currentTime = DateTime.Now;
                            TimeSpan difference = (currentTime - beaconElement.lastScanTime);

                            if (difference.TotalMilliseconds < 500)
                            {
                                continue;
                            }
                            else
                            {
                                beaconElement.lastScanTime = currentTime;
                            }

                            List<MyVoxelBase> detected = new List<MyVoxelBase>();
                            Vector3D position = item.GetPosition();
                            BoundingSphereD boundingSphereD = new BoundingSphereD(position, 20);
                            MyGamePruningStructure.GetAllVoxelMapsInSphere(ref boundingSphereD, detected);
                            float randomFloat = (float)(rnd.Next(4, 12) / 10.0);
                            float randomOffset = (float)(rnd.Next(-4, 7) / 10.0);

                            if (finalAddList.Count < 500)
                            {
                                beaconWasScanning = true;
                            }

                            foreach (MyVoxelBase voxelMap in detected)
                            {
                                if (finalAddList.Count > 500)
                                {
                                    break;
                                }

                                // check voxel state
                                if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null)
                                    continue;

                                // Voxel base detected within the sphere
                                // MyVisualScriptLogicProvider.ShowNotificationToAll($"PASS 1 : voxelBase {voxelMap.StorageName}", 4000);

                                bool isRotated = !IsAlignedWithGlobal(voxelMap.WorldMatrix);

                                // voxel entity id
                                var targetEntityId = voxelMap.EntityId;

                                // create storage cache
                                MyStorageData storageCache =
                                    new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
                                storageCache.Resize(new Vector3I(1));
                                var myVoxelRequestFlag = MyVoxelRequestFlags.ContentCheckedDeep;

                                // min max
                                Vector3I minVoxel;
                                Vector3I maxVoxel;

                                if (isRotated)
                                {
                                    ComputeSphereBounds(voxelMap, ref boundingSphereD, out minVoxel, out maxVoxel);
                                }
                                else
                                {
                                    var min = boundingSphereD.Center - new Vector3D(boundingSphereD.Radius);
                                    var max = boundingSphereD.Center + new Vector3D(boundingSphereD.Radius);
                                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxelMap.PositionLeftBottomCorner,
                                        ref min, out minVoxel);
                                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxelMap.PositionLeftBottomCorner,
                                        ref max, out maxVoxel);
                                    ClampVoxelCoord(voxelMap.Storage, ref minVoxel);
                                    ClampVoxelCoord(voxelMap.Storage, ref maxVoxel);
                                }

                                float minVoxelX = (float)minVoxel.X;
                                float maxVoxelX = (float)maxVoxel.X;
                                float minVoxelY = (float)minVoxel.Y;
                                float maxVoxelY = (float)maxVoxel.Y;
                                float minVoxelZ = (float)minVoxel.Z;
                                float maxVoxelZ = (float)maxVoxel.Z;

                                for (var x = minVoxelX; x <= maxVoxelX; x += randomFloat)
                                {
                                    if (finalAddList.Count > 500) break;
                                    for (var y = minVoxelY; y <= maxVoxelY; y += randomFloat)
                                    {
                                        if (finalAddList.Count > 500) break;
                                        for (var z = minVoxelZ; z <= maxVoxelZ; z += randomFloat)
                                        {
                                            try
                                            {
                                                if (finalAddList.Count > 500) break;

                                                // check that position is within the sphere
                                                var voxelPosition = new Vector3I(x + randomOffset, y + randomOffset,
                                                    z + randomOffset);

                                                var worldPosition = new Vector3D(0);
                                                if (isRotated)
                                                {
                                                    VoxelCoordToWorldPosition(voxelPosition, voxelMap, out worldPosition);
                                                }
                                                else
                                                {
                                                    MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxelMap.PositionLeftBottomCorner, ref voxelPosition, out worldPosition);
                                                }

                                                var distance = Vector3D.Distance(worldPosition, boundingSphereD.Center);

                                                if (distance <= boundingSphereD.Radius)
                                                {
                                                    // voxel position is within the sphere, read cache

                                                    voxelMap.Storage.ReadRange(storageCache,
                                                        MyStorageDataTypeFlags.ContentAndMaterial, 0, voxelPosition,
                                                        voxelPosition, ref myVoxelRequestFlag);

                                                    var content = storageCache.Content(0);
                                                    if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY)
                                                        continue;

                                                    var materialByte = storageCache.Material(0);
                                                    MyVoxelMaterialDefinition materialDefinition =
                                                        MyDefinitionManager.Static.GetVoxelMaterialDefinition(
                                                            materialByte);

                                                    if (materialDefinition != null &&
                                                        materialDefinition.MinedOre != null)
                                                    {
                                                        var filteredOreName = "";

                                                        if (beaconData != null && beaconData != 0)
                                                        {
                                                            var parseOreIdent = beaconData;
                                                            parseOreIdent -= 1;
                                                            if (parseOreIdent >= 0 && parseOreIdent <
                                                                NaniteConstructionManager.OreList.Count)
                                                            {
                                                                filteredOreName =
                                                                    NaniteConstructionManager.OreList[parseOreIdent];
                                                            }
                                                        }

                                                        if (filteredOreName != "" &&
                                                            filteredOreName != materialDefinition.MinedOre)
                                                        {
                                                            continue;
                                                        }

                                                        NaniteMiningItem target = new NaniteMiningItem();
                                                        target.Position = worldPosition;
                                                        target.VoxelPosition = voxelPosition;
                                                        target.Definition = materialDefinition;
                                                        target.VoxelMaterial = materialByte;
                                                        target.VoxelId = targetEntityId;
                                                        target.Amount = 1f;

                                                        var ignored = false;
                                                        foreach (object ignoredItem in PotentialIgnoredList)
                                                        {
                                                            var miningTarget = ignoredItem as NaniteMiningItem;

                                                            if (miningTarget == null)
                                                                continue;

                                                            if (miningTarget.VoxelId == target.VoxelId &&
                                                                miningTarget.Position == target.Position &&
                                                                miningTarget.VoxelMaterial == target.VoxelMaterial)
                                                            {
                                                                ignored = true;
                                                                break;
                                                            }
                                                        }

                                                        if (!ignored && !alreadyCreatedMiningTarget.Contains(target) &&
                                                            !finalAddList.Contains(target) &&
                                                            !TargetList.Contains(target) &&
                                                            !m_globalPositionList.Contains(worldPosition))
                                                        {
                                                            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                            {
                                                                finalAddList.Add(target);
                                                                alreadyCreatedMiningTarget.Add(target);
                                                                beaconFoundNoData = false;
                                                            });
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                MyLog.Default.WriteLine($"##MOD: Nanite Facility, for cycle ERROR: {e}");
                                            }
                                        }
                                    }
                                }

                                // voxelMap.WorldMatrix = originalMatrix;
                            }

                            if (beaconWasScanning && beaconFoundNoData)
                            {
                                item.Enabled = false;
                                beaconElement.stopTick = m_constructionBlock.UpdateCount;
                            }
                        }

                        if (newBeaconDataCode != beaconDataCode)
                        {
                            beaconDataCode = newBeaconDataCode;

                            if (beaconDataCode != "")
                            {
                                finalAddList = new ConcurrentBag<NaniteMiningItem>();
                                m_potentialMiningTargets = new ConcurrentQueue<NaniteMiningItem>();
                            }
                        }
                    }

                    if (m_oldMinedPositionsCount == m_minedPositionsCount && m_minedPositionsCount > 0)
                    {
                        // MyVisualScriptLogicProvider.ShowNotificationToAll($"m_scannertimeout: {m_scannertimeout}", 2000);
                        if (m_scannertimeout++ > 20)
                        {
                            m_scannertimeout = 0;
                            m_minedPositionsCount = 0;
                            ResetMiningTargetsAndRescan();
                        }
                    }
                    else
                    {
                        m_oldMinedPositionsCount = m_minedPositionsCount;
                        m_scannertimeout = 0;
                    }

                    foreach (NaniteMiningItem miningTarget in finalAddList.ToList())
                    {
                        if (miningTarget != null)
                        {
                            m_potentialMiningTargets.Enqueue(miningTarget);
                        }
                    }

                    // MyVisualScriptLogicProvider.ShowNotificationToAll($"PASS 1 : {m_potentialMiningTargets.Count};{finalAddList.Count}", 4000);

                    PotentialTargetListCount = m_potentialMiningTargets.Count;
                });
            }
            catch (ArgumentException e)
            {
                MyLog.Default.WriteLine($"##MOD: Nanite Facility, Argument ERROR: {e}");
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"##MOD: Nanite Facility, ERROR: {e}");
            }
        }

        private void ResetMiningTargetsAndRescan()
        {
            // MyVisualScriptLogicProvider.ShowNotificationToAll($"resetting targets", 4000);

            finalAddList = new ConcurrentBag<NaniteMiningItem>();
            m_potentialMiningTargets = new ConcurrentQueue<NaniteMiningItem>();
            alreadyCreatedMiningTarget = new List<NaniteMiningItem>();
            m_targetTracker = new ConcurrentDictionary<NaniteMiningItem, NaniteMiningTarget>();
            m_globalPositionList = new HashSet<Vector3D>();

            MyLog.Default.WriteLine($"##MOD: Nanite Facility, RESET TARGETS");
        }

        private void TryAddNewVoxelEntity(long entityId)
        {
            try
            {
                IMyEntity entity; // For whatever reason, TryGetEntityById only works on the game thread
                if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
                    return;

                if (!voxelEntities.ContainsKey(entityId))
                {
                    Logging.Instance.WriteLine("[Mining] Adding new voxel entity to storage list.", 1);
                    voxelEntities.Add(entityId, entity);
                }
            }
            catch (Exception e)
            {
                Logging.Instance.WriteLine($"{e}");
            }
        }

        private void PrepareTarget(IMyEntity entity, NaniteMiningItem target)
        {
            try
            {
                if (entity == null)
                    return;

                if (IsValidVoxelTarget(target, entity))
                    finalAddList.Add(target);
            }
            catch (Exception e)
            {
                Logging.Instance.WriteLine($"{e}");
            }
        }

        private bool IsValidVoxelTarget(NaniteMiningItem target, IMyEntity entity)
        {
            if (entity == null)
                return false;

            byte material2 = 0;
            float amount = 0;
            IMyVoxelBase voxel = entity as IMyVoxelBase;

            bool isRotated = !IsAlignedWithGlobal(voxel.WorldMatrix);
            MyVoxelBase voxelBase = voxel as MyVoxelBase;
            Vector3I minVoxel, maxVoxel;

            if (voxelBase == null)
                return false;

            if (isRotated)
            {
                // Small radius to get precise bounds
                BoundingSphereD boundingSphere = new BoundingSphereD(target.Position, 0.1);
                ComputeSphereBounds(voxelBase, ref boundingSphere, out minVoxel, out maxVoxel);
            }
            else
            {
                Vector3D targetMin = target.Position;
                Vector3D targetMax = target.Position;
                MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetMin,
                    out minVoxel);
                MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetMax,
                    out maxVoxel);
            }

            minVoxel += voxelBase.StorageMin;
            maxVoxel += voxelBase.StorageMin;

            voxel.Storage.ClampVoxel(ref minVoxel);
            voxel.Storage.ClampVoxel(ref maxVoxel);

            MyStorageData cache = new MyStorageData();
            cache.Resize(minVoxel, maxVoxel);
            var flag = MyVoxelRequestFlags.AdviseCache;
            cache.ClearContent(0);
            cache.ClearMaterials(0);

            byte original = 0;

            voxel.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, minVoxel, maxVoxel, ref flag);

            original = cache.Content(0);
            material2 = cache.Material(0);

            if (original == MyVoxelConstants.VOXEL_CONTENT_EMPTY)
            {
                Logging.Instance.WriteLine("[Mining] Content is empty!", 2);
                MyAPIGateway.Utilities.InvokeOnGameThread(() => { AddMinedPosition(target); });
                return false;
            }

            Logging.Instance.WriteLine(
                $"[Mining] Material: SizeLinear: {cache.SizeLinear}, Size3D: {cache.Size3D}, AboveISO: {cache.ContainsVoxelsAboveIsoLevel()}",
                2);
            cache.Content(0, 0);

            var voxelMat = target.Definition;
            target.Amount = CalculateAmount(voxelMat, original * 8f);

            Logging.Instance.WriteLine($"[Mining] Removing: {target.Position} ({material2} {amount})", 2);

            if (material2 == 0)
            {
                Logging.Instance.WriteLine("[Mining] Material is 0", 2);
                MyAPIGateway.Utilities.InvokeOnGameThread(() => { AddMinedPosition(target); });
                return false;
            }

            if (target.Amount == 0f)
            {
                Logging.Instance.WriteLine("[Mining] Amount is 0", 2);
                MyAPIGateway.Utilities.InvokeOnGameThread(() => { AddMinedPosition(target); });
                return false;
            }

            return true;
        }

        public override void FindTargets(ref Dictionary<string, int> available, List<NaniteConstructionBlock> blockList)
        {
            var maxTargets = GetMaximumTargets();

            if (TargetList.Count >= maxTargets)
            {
                if (m_potentialMiningTargets.Count > 0)
                    InvalidTargetReason("Maximum targets reached. Add more upgrades!");

                return;
            }

            string LastInvalidTargetReason = "";

            int targetListCount = TargetList.Count;

            HashSet<Vector3D> usedPositions = new HashSet<Vector3D>();
            NaniteMiningItem item;

            while (m_potentialMiningTargets.TryDequeue(out item))
            {
                if (item == null)
                {
                    LastInvalidTargetReason = "Mining position is invalid";
                    continue;
                }

                if (TargetList.Contains(item))
                {
                    LastInvalidTargetReason = "Mining position is already mined";
                    continue;
                }

                if (m_globalPositionList.Contains(item.Position))
                {
                    LastInvalidTargetReason = "Mining position was already mined";
                    continue;
                }

                if (usedPositions.Contains(item.Position))
                {
                    LastInvalidTargetReason = "Mining position was already targeted";
                    continue;
                }

                if (!m_constructionBlock.HasRequiredPowerForNewTarget(this))
                {
                    LastInvalidTargetReason = "Insufficient power for another target";
                    break;
                }

                bool found = false;
                foreach (var block in blockList.ToList())
                    if (block != null &&
                        block.ConstructionBlock.EntityId != m_constructionBlock.ConstructionBlock.EntityId &&
                        block.GetTarget<NaniteMiningTargets>().TargetList
                            .FirstOrDefault(x => ((NaniteMiningItem)x).Position == item.Position) != null)
                    {
                        found = true;
                        LastInvalidTargetReason = "Another factory has this voxel as a target";
                        break;
                    }

                if (found)
                {
                    continue;
                }

                var nearestFactory = m_constructionBlock;
                if (IsInRange(nearestFactory, item.Position, m_maxDistance))
                {
                    /*Logging.Instance.WriteLine(string.Format("[Mining] Adding Mining Target: conid={0} pos={1} type={2}",
                    m_constructionBlock.ConstructionBlock.EntityId, item.Position, MyDefinitionManager.Static.GetVoxelMaterialDefinition(item.VoxelMaterial).MinedOre), 1);*/

                    usedPositions.Add(item.Position);

                    if (m_constructionBlock.IsUserDefinedLimitReached())
                    {
                        InvalidTargetReason("User defined maximum nanite limit reached");
                    }
                    else
                    {
                        TargetList.Add(item);
                    }

                    if (targetListCount++ >= maxTargets)
                        break;
                }
                else
                {
                    m_potentialMiningTargets.Enqueue(item);
                }
            }

            if (LastInvalidTargetReason != "")
            {
                InvalidTargetReason(LastInvalidTargetReason);
            }
        }

        public override void Update()
        {
            try
            {
                MyAPIGateway.Parallel.ForEach(TargetList.ToList(),
                    miningTarget => { ProcessMiningItem(miningTarget); });
            }
            catch (Exception e)
            {
                Logging.Instance.WriteLine($"Exception in NaniteMiningTargets.Update:\n{e}");
            }
        }

        private void ProcessMiningItem(object miningTarget)
        {
            var target = miningTarget as NaniteMiningItem;

            if (target == null)
                return;

            if (Sync.IsServer)
            {
                if (!m_targetTracker.ContainsKey(target))
                    m_constructionBlock.SendAddTarget(target);

                if (m_targetTracker.ContainsKey(target))
                {
                    var trackedItem = m_targetTracker[target];

                    if (MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - trackedItem.StartTime >=
                        trackedItem.CarryTime &&
                        MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - trackedItem.LastUpdate > 2000)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                TransferFromTarget(target);
                                trackedItem.LastUpdate = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                            }
                            catch (Exception e)
                            {
                                Logging.Instance.WriteLine(
                                    $"Exception in NaniteMiningTargets.ProcessMiningItem (Invocation 1):\n{e}");
                            }
                        });
                    }
                }
            }

            MyAPIGateway.Utilities.InvokeOnGameThread(() => { CreateMiningParticles(target); });
        }

        private void CreateMiningParticles(NaniteMiningItem target)
        {
            try
            {
                if (!m_targetTracker.ContainsKey(target))
                    CreateTrackerItem(target);

                Vector4 startColor = new Vector4(1.5f, 0.2f, 0.0f, 1f);
                Vector4 endColor = new Vector4(0.2f, 0.05f, 0.0f, 0.35f);

                var nearestFactory = GetNearestFactory(TargetName, target.Position);

                if (nearestFactory.ParticleManager.Particles.Count < NaniteParticleManager.MaxTotalParticles)
                    nearestFactory.ParticleManager.AddParticle(startColor, endColor, GetMinTravelTime() * 1000f,
                        GetSpeed(), target, null);
            }
            catch (Exception e)
            {
                VRage.Utils.MyLog.Default.WriteLine(
                    $"NaniteMiningTargets.CreateMiningParticles() exception: {e}");
            }
        }

        private void CreateTrackerItem(NaniteMiningItem target)
        {
            var nearestFactory = m_constructionBlock;
            double distance = Vector3D.Distance(nearestFactory.ConstructionBlock.GetPosition(), target.Position);
            int time = (int)Math.Max(GetMinTravelTime() * 1000f, (distance / GetSpeed()) * 1000f);

            NaniteMiningTarget miningTarget = new NaniteMiningTarget();
            miningTarget.ParticleCount = 0;
            miningTarget.StartTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
            miningTarget.LastUpdate = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
            miningTarget.CarryTime = time - 1000;

            MyAPIGateway.Utilities.InvokeOnGameThread(() => { m_targetTracker.GetOrAdd(target, miningTarget); });
        }

        private void TransferFromTarget(NaniteMiningItem target)
        {
            // Must be invoked from game thread
            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(target.VoxelId, out entity))
            {
                AddToIgnoreList(target);
                AddMinedPosition(target);
                CancelTarget(target);
                return;
            }

            try
            {
                if (entity == null || !IsValidVoxelTarget(target, entity))
                {
                    AddToIgnoreList(target);
                    AddMinedPosition(target);
                    CancelTarget(target);

                    return;
                }

                try
                {
                    var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(target.VoxelMaterial);
                    var item = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
                    var inventory = ((MyCubeBlock)m_constructionBlock.ConstructionBlock).GetInventory();
                    MyInventory targetInventory = ((MyCubeBlock)m_constructionBlock.ConstructionBlock).GetInventory();

                    if (targetInventory != null &&
                        targetInventory.CanItemsBeAdded((MyFixedPoint)(target.Amount), item.GetId()))
                    {
                        if (entity == null)
                        {
                            AddToIgnoreList(target);
                            AddMinedPosition(target);
                            CancelTarget(target);
                            return;
                        }

                        IMyVoxelBase voxel = entity as IMyVoxelBase;
                        MyVoxelBase voxelBase = voxel as MyVoxelBase;

                        if (voxelBase == null)
                        {
                            AddToIgnoreList(target);
                            AddMinedPosition(target);
                            CancelTarget(target);
                            return;
                        }

                        var ownerName = targetInventory.Owner as IMyTerminalBlock;
                        if (ownerName != null)
                            Logging.Instance.WriteLine(
                                $"[Mining] Transfer - Adding {target.Amount} {item.GetId().SubtypeName} to {ownerName.CustomName}",
                                1);

                        if (!targetInventory.AddItems((MyFixedPoint)(target.Amount), item))
                        {
                            Logging.Instance.WriteLine(
                                $"Error while transferring {target.Amount} {item.GetId().SubtypeName}! Aborting mining operation.");
                            return;
                        }

                        m_constructionBlock.VoxelRemovalQueue.TryAdd(target, voxelBase);

                        AddMinedPosition(target);
                        CompleteTarget(target);
                        return;
                    }

                    Logging.Instance.WriteLine(
                        "[Mining] Mined materials could not be moved. No free cargo space (probably)!", 1);
                    AddToIgnoreList(target);
                    AddMinedPosition(target);
                    CancelTarget(target);
                }
                catch (Exception e)
                {
                    Logging.Instance.WriteLine(
                        $"Exception in NaniteMiningTargets.TransferFromTarget (Invocation 0):\n{e}");
                }
            }
            catch (Exception e)
            {
                Logging.Instance.WriteLine($"Exception in NaniteMiningTargets.TransferFromTarget:\n{e}");
            }
        }

        private void AddMinedPosition(NaniteMiningItem target)
        {
            m_minedPositionsCount++;
            m_globalPositionList.Add(target.Position);
        }

        private static float CalculateAmount(MyVoxelMaterialDefinition material, float amount)
        {
            var oreObjBuilder =
                VRage.ObjectBuilders.MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(material.MinedOre);
            oreObjBuilder.MaterialTypeName = material.Id.SubtypeId;

            float amountCubicMeters = (float)(((float)amount / (float)MyVoxelConstants.VOXEL_CONTENT_FULL)
                                              * MyVoxelConstants.VOXEL_VOLUME_IN_METERS *
                                              Sandbox.Game.MyDrillConstants.VOXEL_HARVEST_RATIO);

            amountCubicMeters *= (float)material.MinedOreRatio;
            var physItem = MyDefinitionManager.Static.GetPhysicalItemDefinition(oreObjBuilder);
            MyFixedPoint amountInItemCount = (MyFixedPoint)(amountCubicMeters / physItem.Volume);
            return (float)amountInItemCount;
        }

        public override void CancelTarget(object obj)
        {
            var target = obj as NaniteMiningItem;
            Logging.Instance.WriteLine(string.Format(
                    "[Mining] Cancelled Mining Target: {0} - {1} (VoxelID={2},Position={3})",
                    m_constructionBlock.ConstructionBlock.EntityId, obj.GetType().Name, target.VoxelId,
                    target.Position),
                1);

            if (Sync.IsServer)
                m_constructionBlock.SendCompleteTarget((NaniteMiningItem)obj);

            m_constructionBlock.ParticleManager.CancelTarget(target);
            if (m_targetTracker.ContainsKey(target))
                m_targetTracker.Remove(target);

            Remove(obj);
        }

        public override void AddToIgnoreList(object obj)
        {
            if (PotentialIgnoredList.Contains(obj) == false)
            {
                PotentialIgnoredList.Add(obj);
                if (PotentialTargetList.Contains(obj))
                {
                    PotentialTargetList.Remove(obj);
                }
            }
        }

        public override void CompleteTarget(object obj)
        {
            var target = obj as NaniteMiningItem;
            Logging.Instance.WriteLine(string.Format(
                    "[Mining] Completed Mining Target: {0} - {1} (VoxelID={2},Position={3})",
                    m_constructionBlock.ConstructionBlock.EntityId, obj.GetType().Name, target.VoxelId,
                    target.Position),
                1);

            if (Sync.IsServer)
                m_constructionBlock.SendCompleteTarget((NaniteMiningItem)obj);

            m_constructionBlock.ParticleManager.CompleteTarget(target);
            if (m_targetTracker.ContainsKey(target))
                m_targetTracker.Remove(target);

            Remove(obj);
        }
    }
}