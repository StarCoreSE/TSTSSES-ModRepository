using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Noise.Combiners;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace WarpDriveMod
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "SlipspaceCoreLarge", "SlipspaceCoreSmall", "PrototechSlipspaceCoreLarge", "PrototechSlipspaceCoreSmall")]
    public class WarpDrive : MyGameLogicComponent
    {
        public IMyFunctionalBlock Block { get; private set; }
        public WarpSystem System { get; private set; }
        public Settings Settings { get; private set; }
        public static WarpDrive Instance;
        public bool HasPower => sink.CurrentInputByType(WarpConstants.ElectricityId) >= prevRequiredPower;
        public bool BlockWasON = false;

        private T CastProhibit<T>(T ptr, object val) => (T)val;

        // Ugly workaround
        public float RequiredPower
        {
            get
            {
                return _requiredPower;
            }
            set
            {
                prevRequiredPower = _requiredPower;
                _requiredPower = (float)value;
            }
        }
        private float prevRequiredPower;
        private float _requiredPower;
        private MyResourceSinkComponent sink;
        private long initStart;
        private bool started = false;
        private int BlockOnTick = 0;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Instance = this;
            Block = (IMyFunctionalBlock)Entity;
            Settings = Settings.Load();

            InitPowerSystem();

            if (WarpDriveSession.Instance != null)
                initStart = WarpDriveSession.Instance.Runtime;

            MyVisualScriptLogicProvider.PlayerLeftCockpit += PlayerLeftCockpit;

            if (!MyAPIGateway.Utilities.IsDedicated)
                Block.AppendingCustomInfo += Block_AppendingCustomInfo;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        private void Block_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder Info)
        {
            if (arg1 == null || Settings == null || System == null)
                return;

            float _mass = 1;
            if (System.GridsMass != null && System.GridsMass.Count > 0 && arg1.CubeGrid != null && System.GridsMass.ContainsKey(arg1.CubeGrid.EntityId))
                System.GridsMass.TryGetValue(arg1.CubeGrid.EntityId, out _mass);
            else
            {
                if (arg1.CubeGrid != null)
                {
                    _mass = System.CulcucateGridGlobalMass(arg1.CubeGrid);
                    System.GridsMass[arg1.CubeGrid.EntityId] = _mass;
                }
            }

            float SpeedNormalize = (float)(Settings.maxSpeed * 0.06); // 60 / 1000
            float SpeedCalc = 1f + (SpeedNormalize * SpeedNormalize);

            float MassCalc;
            if (arg1.CubeGrid.GridSizeEnum == MyCubeSize.Small)
                MassCalc = _mass * (SpeedCalc / 0.528f) / 700000f;
            else
                MassCalc = _mass * (SpeedCalc / 0.528f) / 1000000f;

            float MaxNeededPower;

            if (arg1.CubeGrid.GridSizeEnum == MyCubeSize.Large)
            {
                if (System.currentSpeedPt != Settings.maxSpeed)
                    MaxNeededPower = (MassCalc + Settings.baseRequiredPower * 3) / Settings.powerRequirementBySpeedDeviderLarge * 0.9725f;
                else
                    MaxNeededPower = RequiredPower;
            }
            else
            {
                if (System.currentSpeedPt != Settings.maxSpeed)
                    MaxNeededPower = (MassCalc + Settings.baseRequiredPowerSmall * 3) / Settings.powerRequirementBySpeedDeviderSmall * 0.9725f;
                else
                    MaxNeededPower = RequiredPower;
            }
            // ToDo HUMAN READABLE NUMBERS


            Info?.AppendLine($"Max Required Power: {ConvertToReadable(MaxNeededPower, 2)}Watt");
            Info?.AppendLine($"Required Power: {ConvertToReadable(RequiredPower, 2)}Watt");

            if (sink != null)
                Info?.AppendLine($"Current Power: {ConvertToReadable(sink.CurrentInputByType(WarpConstants.ElectricityId), 2)}Watt");

            Info?.Append("FSD Heat: ").Append(System.DriveHeat).Append("%\n");
        }

        /// <summary>
        /// Make numbers to strings with physical notations
        /// </summary>
        /// <param name="value"></param>
        /// <param name="currentPrefix">how big is the number [0.0, 1.k, 2.M, 3.G, 4.T, 5.P ...]</param>
        /// <returns>string of number with prefix</returns>
        private string ConvertToReadable(float value, int currentPrefix = 0)
        {
            string[] suffixes = { "", "kilo", "Mega", "Giga", "Terra", "Peta", "Exa", "Zetta", "Yotta", "Ronna", "Quetta", "insane" };

            if (value < 1000)
                return value.ToString("0.## ") + suffixes[currentPrefix]; // No suffix for values less than 1000

            int suffixIndex = 0;

            // Divide the value by 1000 until it's less than 1000
            while (value >= 1000 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1000;
                suffixIndex++;
            }

            // just to prefet array out of index
            suffixIndex = suffixIndex + currentPrefix;
            if (suffixIndex > suffixes.Length -1)
                suffixIndex = suffixes.Length -1;

            // Format the number with decimal places if necessary
            return value.ToString("0.## ") + suffixes[suffixIndex];
        }

        public override void UpdateBeforeSimulation10()
        {
            if (WarpDriveSession.Instance == null || Block == null)
                return;

            // init once
            if (Block != null)
                WarpDriveSession.Instance.InitJumpControl();

            if (BlockWasON)
            {
                if (BlockOnTick++ > 20)
                {
                    Block.Enabled = true;
                    BlockWasON = false;
                    BlockOnTick = 0;
                }
            }

            if (!MyAPIGateway.Utilities.IsDedicated)
                Block.RefreshCustomInfo();
        }

        public override void UpdateBeforeSimulation()
        {
            if (WarpDriveSession.Instance == null)
            {
                MyLog.Default.WriteLineAndConsole($"[WarpDriveMod] WarpDriveSession.Instance is null");
                return;
            }

            if (System == null)
            {
                System = WarpDriveSession.Instance.GetWarpSystem(this);
                if (System == null)
                {
                    MyLog.Default.WriteLineAndConsole($"[WarpDriveMod] Failed to get WarpSystem");
                    return;
                }
            }

            if (!started)
            {
                if (System != null && System.Valid)
                    started = true;
                else if (initStart <= WarpDriveSession.Instance.Runtime - WarpConstants.groupSystemDelay)
                {
                    System = WarpDriveSession.Instance.GetWarpSystem(this);
                    if (System == null)
                        return;

                    System.OnSystemInvalidatedAction += OnSystemInvalidated;
                    started = true;
                }
            }
            else
            {
                sink.Update();
            }
        }

        public override void Close()
        {
            if (System == null)
                return;

            System.OnSystemInvalidatedAction -= OnSystemInvalidated;

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                if (Block != null)
                    Block.AppendingCustomInfo -= Block_AppendingCustomInfo;

                System.StopBlinkParticleEffect();
            }

            if (Block != null && Block.CubeGrid != null && System.GridsMass.ContainsKey(Block.CubeGrid.EntityId))
                System.GridsMass.Remove(Block.CubeGrid.EntityId);
        }

        private void InitPowerSystem()
        {
            MyResourceSinkComponent powerSystem = new MyResourceSinkComponent();

            powerSystem.Init(
                MyStringHash.GetOrCompute("Utility"),
                (float)(Settings.baseRequiredPowerSmall * Settings.powerRequirementMultiplier),
                ComputeRequiredPower,
                (MyCubeBlock)Entity);

            Entity.Components.Add(powerSystem);
            sink = powerSystem;
            sink.Update();
        }

        private float ComputeRequiredPower()
        {
            if (System == null || System.WarpState == WarpSystem.State.Idle)
                RequiredPower = 0;

            return RequiredPower;
        }

        public void PlayerLeftCockpit(string entityName, long playerId, string gridName)
        {
            if (Block == null || System == null)
                return;

            WarpDrive drive = Block?.GameLogic?.GetAs<WarpDrive>();
            if (drive == null)
                return;
            else if (drive.System.WarpState == WarpSystem.State.Idle)
                return;

            if (entityName != "")
            {
                long temp_id;
                if (long.TryParse(entityName, out temp_id))
                {
                    var dump_cockpit = MyAPIGateway.Entities.GetEntityById(temp_id) as IMyShipController;
                    var CockpitGrid = dump_cockpit?.CubeGrid as MyCubeGrid;
                    HashSet<IMyShipController> FoundCockpits = new HashSet<IMyShipController>();

                    if (CockpitGrid == null)
                        return;

                    if ((bool)(drive.System.grid?.cockpits?.TryGetValue(CockpitGrid, out FoundCockpits)))
                    {
                        if (FoundCockpits.Count > 0 && FoundCockpits.Contains(dump_cockpit))
                        {
                            if (dump_cockpit.CubeGrid.EntityId != drive.Block.CubeGrid.EntityId)
                                return;

                            drive.System.SafeTriggerON = true;

                            if (MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Multiplayer.IsServer)
                            {
                                drive.System.currentSpeedPt = -1f;
                                dump_cockpit.CubeGrid?.Physics?.ClearSpeed();

                                drive.System.Dewarp(true);
                                Block.Enabled = false;
                                BlockWasON = true;
                            }

                            drive.System.SafeTriggerON = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gridMatrix"></param>
        /// <param name="mainGrid"></param>
        /// <param name="mainGridRadius"></param>
        /// <param name="gridSpeed"></param>
        /// <param name="controlledCockpit"></param>
        /// <returns></returns>
        public string ProxymityDangerInWarp(MatrixD gridMatrix, IMyCubeGrid mainGrid, double mainGridRadius, double gridSpeed, IMyShipController controlledCockpit)
        {
            if (mainGrid == null || controlledCockpit == null)
                return null;

            List<IMyCubeGrid> attachedList = new List<IMyCubeGrid>();           // All conntected grids
            List<IMyEntity> entList = new List<IMyEntity>();                    // Possible entities to check for collision
            Vector3D start = gridMatrix.Translation;                            // grid.WorldVolume.Center; does not work, physics off... // or ideally the whole construct's volume, spheres can be Include()'d
            float mass = mainGrid.Physics.Mass;
            //double radius = mainGrid.WorldVolume.Radius + (mass / 100000d);   // more mass == more warp bubble
            //double radius = 100;     // more mass == more warp bubble
            double length = 250 + gridSpeed;

            MyAPIGateway.GridGroups.GetGroup(mainGrid, GridLinkTypeEnum.Physical, attachedList);

            Vector3D forward = controlledCockpit.WorldMatrix.Forward;           /* velocity normalized */
            Vector3D up = controlledCockpit.WorldMatrix.Up;                     /* velocity normalized */

            double halfLength = length * 0.5;
            Vector3D center = start + forward * halfLength;
            Vector3D halfExtents = new Vector3D(mainGridRadius, mainGridRadius, halfLength);

            Quaternion quat = Quaternion.CreateFromRotationMatrix(MatrixD.CreateFromDir(forward)); // a bit overkill, maybe you can find a proper up vector for CreateFromForwardUp()
            //Quaternion quat = Quaternion.CreateFromForwardUp(forward, up); // could work, ToDo check

            MyOrientedBoundingBoxD obb = new MyOrientedBoundingBoxD(center, halfExtents, quat);

            BoundingBoxD bb = obb.GetAABB();
            entList = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref bb);

            // DEBUGGING
            //SendMessage("Count_" + entList.Count, 0.016f);
            //DrawBB(bb, Color.Red);

            foreach (IMyEntity ent in entList)
            {
                // return a string for all object that deny charging
                if (ent is MySafeZone)
                    return "SafeZone";

                // Shall we ignore characters? What about missiles? bullets? (We should ignore floating objects)
                if (!(ent is MyCubeGrid || ent is MyVoxelMap))
                    continue;

                if (ent is IMyCubeGrid)
                {
                    // Skip own MainGrid
                    IMyCubeGrid foundGrid = (IMyCubeGrid)ent;
                    if (attachedList.Contains(foundGrid))
                        continue;

                    // Not own grid?
                    string msg = ent.DisplayName;
                    string cut_msg = msg.Length > 12 ? msg.Substring(0, 12) : msg;      // want a max string of 12 to display              
                    //SendMessage("Debug_Grid: " + cut_msg, 0.016f);
                    return cut_msg;
                }

                if (ent is MyVoxelMap)
                {
                    // How to make collision detection even more precise?
                    // BoundingBoxEffect? -> asteroids have a big BB ....
                    // Lore: Dust around asteorids prevent establishing a warp bubble.

                    MyVoxelMap vMap = (MyVoxelMap)ent;
                    string msg = (vMap.StorageName).Split('_')[0];                      // Sanitize the sting Asteroid-2342343434 ?!
                    msg.Replace("Proc", "");
                    string cut_msg = msg.Length > 12 ? msg.Substring(0, 12) : msg;      // want a max string of 12 to display
                    //SendMessage("Debug: " + cut_msg, 0.016f);
                    return cut_msg;
                }
            }
            // No boundingbox collision -> inWarp
            return null;
        }

        /// String or null >> string is name of object that prevent warp
        /// ToDo: radius influence by mass
        /// <param name="gridMatrix">MatrixD as WorldMatrix of the grid</param>
        /// <param name="WarpGrid">"Main" grid which want to warp</param>
        /// <returns>string, null: charge/warp  <>  "Entity.GetFriendlyName()"</returns>
        public string ProxymityDangerCharge(MatrixD gridMatrix, IMyCubeGrid warpGrid)
        {
            if (warpGrid == null || warpGrid.Physics == null)
                return null;

            // Safezone check?
            if (MyAPIGateway.Session?.Player != null)
            {
                bool allowed = MySessionComponentSafeZones.IsActionAllowed(
                                    MyAPIGateway.Session.Player.Character.WorldMatrix.Translation,
                                    CastProhibit(MySessionComponentSafeZones.AllowedActions, 1)
                               );

                if (!allowed)
                    return "SafeZone";
            }

            // Prepare lists
            List<IMyEntity> entList = new List<IMyEntity>();
            List<IMyCubeGrid> attachedList = new List<IMyCubeGrid>();

            Vector3D center = warpGrid.WorldVolume.Center;
            float mass = warpGrid.Physics.Mass; // check landing gear connected mass?!
            double radius = warpGrid.WorldVolume.Radius + (mass / 100000d); // more mass == more warp bubble
            BoundingSphereD sphere = new BoundingSphereD(center, radius);

            // Get entities safely
            var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            if (entities != null)
                entList.AddRange(entities);

            // Get all subgrids grids and locked on landing gear.
            MyAPIGateway.GridGroups.GetGroup(warpGrid, GridLinkTypeEnum.Physical, attachedList);

            foreach (IMyEntity ent in entList)
            {
                // Skip entities that are not relevant
                if (ent is MySafeZone)
                    return "SafeZone";

                if (!(ent is MyCubeGrid) && !(ent is MyVoxelMap))
                    continue;

                var foundGrid = ent as IMyCubeGrid;
                if (foundGrid != null)
                {
                    // Skip own MainGrid
                    if (attachedList.Contains(foundGrid))
                        continue;

                    // Not own grid?
                    string msg = ent.DisplayName;
                    string cut_msg = msg.Length > 12 ? msg.Substring(0, 12) : msg; // Max string length 12
                    return cut_msg;
                }

                var vMap = ent as MyVoxelMap;
                if (vMap != null)
                {
                    // Asteroid collision detection (simplified)
                    string msg = (vMap.StorageName).Split('_')[0];
                    string cut_msg = msg.Length > 12 ? msg.Substring(0, 12) : msg;
                    return cut_msg;
                }
            }

            // No bounding box collision -> charge
            return null;
        }

        public bool EnemyProxymityDangerCharge(IMyCubeGrid WarpGrid)
        {
            if (WarpGrid == null || WarpGrid.Physics == null)
                return false;

            var Gridlocation = WarpGrid.PositionComp.GetPosition();
            var sphere = new BoundingSphereD(Gridlocation, Settings.DetectEnemyGridInRange);

            // Safely copy entities to avoid concurrent modification
            List<IMyEntity> entList = new List<IMyEntity>();
            var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            if (entities != null)
                entList.AddRange(entities);

            if (entList.Count == 0)
                return false;

            var AttachedList = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(WarpGrid, GridLinkTypeEnum.Physical, AttachedList);

            var WarpGridOwner = WarpGrid.BigOwners.FirstOrDefault();
            var WarpGridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(WarpGridOwner);

            foreach (var ent in entList)
            {
                var foundGrid = ent as IMyCubeGrid;
                if (foundGrid == null || AttachedList.Contains(foundGrid))
                    continue;

                if (foundGrid.BigOwners != null && foundGrid.BigOwners.FirstOrDefault() != 0L)
                {
                    var FoundGridOwner = foundGrid.BigOwners.FirstOrDefault();
                    if (FoundGridOwner == WarpGridOwner)
                        continue;

                    var FoundGridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(FoundGridOwner);
                    if (WarpGridFaction != null && FoundGridFaction != null)
                    {
                        if (FoundGridFaction.FactionId == WarpGridFaction.FactionId)
                            continue;

                        var FactionsRelationship = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(
                            FoundGridFaction.FactionId, WarpGridFaction.FactionId);
                        if (FactionsRelationship != MyRelationsBetweenFactions.Enemies)
                            continue;

                        // Found enemy grid in sphere
                        return true;
                    }
                    else
                    {
                        // Found enemy grid with no faction
                        return true;
                    }
                }
            }

            return false;
        }

        private void OnSystemInvalidated(WarpSystem system)
        {
            if (Block.MarkedForClose || Block.CubeGrid.MarkedForClose)
                return;

            WarpDriveSession.Instance.DelayedGetWarpSystem(this);
        }

        public void SetWarpSystem(WarpSystem system)
        {
            System = system;
            System.OnSystemInvalidatedAction += OnSystemInvalidated;
        }

        public override bool Equals(object obj)
        {
            var drive = obj as WarpDrive;

            return drive != null && EqualityComparer<IMyFunctionalBlock>.Default.Equals(Block, drive.Block);
        }

        public override int GetHashCode()
        {
            return 957606482 + EqualityComparer<IMyFunctionalBlock>.Default.GetHashCode(Block);
        }

        #region DEBUGGING
        private void DrawBB(BoundingBoxD obb, Color color, MySimpleObjectRasterizer draw = MySimpleObjectRasterizer.SolidAndWireframe, BlendTypeEnum blend = BlendTypeEnum.PostPP, bool extraSeeThrough = true)
        {

            MatrixD wm = MatrixD.CreateTranslation(obb.Center);

            //wm.Translation = obb.Center;

            //BoundingBoxD localBB = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);

            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref obb, ref color, draw, 1, faceMaterial: null, lineMaterial: null, blendType: blend);

        }

        private void DrawSphere(BoundingSphereD sphere, Color color, MySimpleObjectRasterizer draw = MySimpleObjectRasterizer.SolidAndWireframe, BlendTypeEnum blend = BlendTypeEnum.PostPP)
        {
            MatrixD wm = MatrixD.CreateTranslation(sphere.Center);
            MySimpleObjectDraw.DrawTransparentSphere(ref wm, (float)sphere.Radius, ref color, draw, 24, null, null, 0.02f, blendType: blend);
        }
        #endregion

    }
}
