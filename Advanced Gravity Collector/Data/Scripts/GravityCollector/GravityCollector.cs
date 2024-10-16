using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;


namespace Digi.GravityCollector {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "MediumGravityCollector", "LargeGravityCollector")]
    public class GravityCollector : MyGameLogicComponent
    {
        public const float RANGE_MIN = 0;
        public const float RANGE_MAX_MEDIUM = 400;
        public const float RANGE_MAX_LARGE = 600;
        public const float RANGE_OFF_EXCLUSIVE = 1;
        public const float STRENGTH_MIN = 1;
        public const float STRENGTH_MAX = 200;
        public const int APPLY_FORCE_SKIP_TICKS = 3;
        public const double MAX_VIEW_RANGE_SQ = 500 * 500;
        public const float MASS_MUL = 10;
        public const float MAX_MASS = 5000;
        public const string CONTROLS_PREFIX = "GravityCollector.";
        public readonly Guid SETTINGS_GUID = new Guid("0DFC6F70-310D-4D1C-A55F-C57913E20389");
        public const int SETTINGS_CHANGED_COUNTDOWN = (60 * 1) / 10;

        private AdvancedCollectionSystem collectionSystem;
        public float adaptiveForceMultiplier = 1.0f;

        public float Range
        {
            get { return Settings.Range; }
            set
            {
                Settings.Range = MathHelper.Clamp((int)Math.Floor(value), RANGE_MIN, maxRange);
                SettingsChanged();
                if (Settings.Range < RANGE_OFF_EXCLUSIVE)
                {
                    NeedsUpdate = MyEntityUpdateEnum.NONE;
                }
                else
                {
                    if ((NeedsUpdate & MyEntityUpdateEnum.EACH_10TH_FRAME) == 0)
                        NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                }
                block?.Components?.Get<MyResourceSinkComponent>()?.Update();
            }
        }

        public float StrengthMul
        {
            get { return Settings.Strength; }
            set
            {
                Settings.Strength = MathHelper.Clamp(value, STRENGTH_MIN / 100f, STRENGTH_MAX / 100f);
                SettingsChanged();
                block?.Components?.Get<MyResourceSinkComponent>()?.Update();
            }
        }

        IMyCollector block;
        MyPoweredCargoContainerDefinition blockDef;
        public readonly GravityCollectorBlockSettings Settings = new GravityCollectorBlockSettings();
        int syncCountdown;
        double coneAngle;
        float offset;
        float maxRange;
        int skipTicks;
        List<IMyFloatingObject> floatingObjects;
        GravityCollectorMod Mod => GravityCollectorMod.Instance;
        public CollectorNetwork network;

        bool DrawCone
        {
            get
            {
                if (MyAPIGateway.Utilities.IsDedicated || !block.ShowOnHUD)
                    return false;
                var localPlayer = MyAPIGateway.Session?.Player;
                if (localPlayer == null)
                    return false;
                long localPlayerId = localPlayer.IdentityId;
                var relation = block.GetUserRelationToOwner(localPlayerId);
                return relation != MyRelationsBetweenPlayerAndBlock.Enemies;
            }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                SetupTerminalControls<IMyCollector>();
                block = (IMyCollector)Entity;
                if (block.CubeGrid?.Physics == null)
                    return;
                blockDef = (MyPoweredCargoContainerDefinition)block.SlimBlock.BlockDefinition;
                floatingObjects = new List<IMyFloatingObject>();
                collectionSystem = new AdvancedCollectionSystem(this);

                switch (block.BlockDefinition.SubtypeId)
                {
                    case "MediumGravityCollector":
                        maxRange = RANGE_MAX_MEDIUM;
                        coneAngle = MathHelper.ToRadians(30);
                        offset = 0.75f;
                        break;
                    case "LargeGravityCollector":
                        maxRange = RANGE_MAX_LARGE;
                        coneAngle = MathHelper.ToRadians(25);
                        offset = 1.5f;
                        break;
                }

                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
                var sink = block.Components?.Get<MyResourceSinkComponent>();
                sink?.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, ComputeRequiredPower);
                Settings.Strength = 1.0f;
                Settings.Range = maxRange;

                if (!LoadSettings())
                {
                    ParseLegacyNameStorage();
                }

                SaveSettings();
                network = CollectorNetwork.GetNetwork(block.CubeGrid.EntityId);
                network.RegisterCollector(this);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Close()
        {
            try
            {
                if (block == null)
                    return;
                floatingObjects?.Clear();
                floatingObjects = null;
                network?.UnregisterCollector(this);
                network = null;
                block = null;
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        float ComputeRequiredPower()
        {
            if (!block.IsWorking)
                return 0f;
            var baseUsage = 0.002f;
            var maxPowerUsage = blockDef.RequiredPowerInput;
            var mul = (StrengthMul / (STRENGTH_MAX / 100f)) * (Range / maxRange);
            return baseUsage + maxPowerUsage * mul;
        }

        public override void UpdateBeforeSimulation10()
        {
            try
            {
                SyncSettings();
                FindFloatingObjects();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        void FindFloatingObjects()
        {
            var entities = Mod.Entities;
            entities.Clear();
            floatingObjects.Clear();

            if (Range < RANGE_OFF_EXCLUSIVE || !block.IsWorking || !block.CubeGrid.Physics.Enabled)
            {
                if ((NeedsUpdate & MyEntityUpdateEnum.EACH_FRAME) != 0)
                {
                    UpdateEmissive();
                    NeedsUpdate &= ~MyEntityUpdateEnum.EACH_FRAME;
                }
                return;
            }

            if ((NeedsUpdate & MyEntityUpdateEnum.EACH_FRAME) == 0)
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            var collectPos = GetCollectionPoint();
            var sphere = new BoundingSphereD(collectPos, Range + 10);
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Dynamic);

            foreach (var ent in entities)
            {
                var floatingObject = ent as IMyFloatingObject;
                if (floatingObject != null && floatingObject.Physics != null)
                    floatingObjects.Add(floatingObject);
            }

            entities.Clear();
        }

        private Color prevColor;

        void UpdateEmissive(bool pulling = false)
        {
            var color = Color.Red;
            float strength = 0f;
            if (block.IsWorking)
            {
                strength = 1f;
                if (pulling)
                    color = Color.Cyan;
                else
                    color = new Color(10, 255, 0);
            }
            if (prevColor == color)
                return;
            prevColor = color;
            block.SetEmissiveParts("Emissive", color, strength);
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (Range < RANGE_OFF_EXCLUSIVE || !block.IsWorking)
                    return;

                collectionSystem.Update();

                bool applyForce = (++skipTicks >= APPLY_FORCE_SKIP_TICKS);
                if (applyForce)
                    skipTicks = 0;

                if (!applyForce && MyAPIGateway.Utilities.IsDedicated)
                    return;

                ProcessVisuals();

                if (floatingObjects.Count == 0)
                {
                    UpdateEmissive(false);
                    return;
                }

                ProcessObjects(applyForce);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void ProcessVisuals()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            var conePos = block.WorldMatrix.Translation + (block.WorldMatrix.Forward * -offset);
            var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            bool inViewRange = Vector3D.DistanceSquared(cameraMatrix.Translation, conePos) <= MAX_VIEW_RANGE_SQ;

            if (inViewRange && DrawCone)
                DrawInfluenceCone(conePos);
        }
        private void ProcessObjects(bool applyForce)
        {
            int pulling = 0;
            var cameraPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            bool inViewRange = Vector3D.DistanceSquared(cameraPos, GetCollectionPoint()) <= MAX_VIEW_RANGE_SQ;

            foreach (var obj in floatingObjects)
            {
                if (!IsValidCollectionTarget(obj))
                    continue;

                var bestCollector = network.GetBestCollectorFor(obj);
                if (bestCollector == this)
                {
                    float priority = CalculateCollectionPriority(obj);
                    collectionSystem.QueueObject(obj, priority);
                    pulling++;

                    if (inViewRange && !MyAPIGateway.Utilities.IsDedicated)
                    {
                        var objPos = obj.GetPosition();
                        var mul = (float)Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds * 0.01);
                        var radius = obj.Render.GetModel().BoundingSphere.Radius * MinMaxPercent(0.75f, 1.25f, mul);
                        MyTransparentGeometry.AddPointBillboard(Mod.MATERIAL_DOT, Color.LightSkyBlue * MinMaxPercent(0.2f, 0.4f, mul), objPos, radius, 0);
                    }
                }
            }

            if (applyForce)
                UpdateEmissive(pulling > 0);
        }
        void DrawInfluenceCone(Vector3D conePos)
        {
            Vector4 color = Color.Cyan.ToVector4() * 10;
            Vector4 planeColor = (Color.White * 0.1f).ToVector4();
            const float LINE_THICK = 0.02f;
            const int WIRE_DIV_RATIO = 16;

            var coneMatrix = block.WorldMatrix;
            coneMatrix.Translation = conePos;

            float rangeOffset = Range + (offset * 2);
            float baseRadius = rangeOffset * (float)Math.Tan(coneAngle);

            var apexPosition = coneMatrix.Translation;
            var directionVector = coneMatrix.Forward * rangeOffset;
            var maxPosCenter = conePos + coneMatrix.Forward * rangeOffset;
            var baseVector = coneMatrix.Up * baseRadius;

            Vector3 axis = directionVector;
            axis.Normalize();

            float stepAngle = (float)(Math.PI * 2.0 / (double)WIRE_DIV_RATIO);

            var prevConePoint = apexPosition + directionVector + Vector3.Transform(baseVector, Matrix.CreateFromAxisAngle(axis, (-1 * stepAngle)));
            prevConePoint = (apexPosition + Vector3D.Normalize((prevConePoint - apexPosition)) * rangeOffset);

            var quad = default(MyQuadD);

            for (int step = 0; step < WIRE_DIV_RATIO; step++)
            {
                var conePoint = apexPosition + directionVector + Vector3.Transform(baseVector, Matrix.CreateFromAxisAngle(axis, (step * stepAngle)));
                var lineDir = (conePoint - apexPosition);
                lineDir.Normalize();
                conePoint = (apexPosition + lineDir * rangeOffset);

                MyTransparentGeometry.AddLineBillboard(Mod.MATERIAL_SQUARE, color, conePoint, (prevConePoint - conePoint), 1f, LINE_THICK);

                MyTransparentGeometry.AddLineBillboard(Mod.MATERIAL_SQUARE, color, apexPosition, lineDir, rangeOffset, LINE_THICK);

                MyTransparentGeometry.AddLineBillboard(Mod.MATERIAL_SQUARE, color, conePoint, (maxPosCenter - conePoint), 1f, LINE_THICK);

                quad.Point0 = prevConePoint;
                quad.Point1 = conePoint;
                quad.Point2 = apexPosition;
                quad.Point3 = apexPosition;
                MyTransparentGeometry.AddQuad(Mod.MATERIAL_SQUARE, ref quad, planeColor, ref Vector3D.Zero);

                quad.Point0 = prevConePoint;
                quad.Point1 = conePoint;
                quad.Point2 = maxPosCenter;
                quad.Point3 = maxPosCenter;
                MyTransparentGeometry.AddQuad(Mod.MATERIAL_SQUARE, ref quad, planeColor, ref Vector3D.Zero);

                prevConePoint = conePoint;
            }
        }

        bool LoadSettings()
        {
            if (block.Storage == null)
                return false;

            string rawData;
            if (!block.Storage.TryGetValue(SETTINGS_GUID, out rawData))
                return false;

            try
            {
                var loadedSettings = MyAPIGateway.Utilities.SerializeFromBinary<GravityCollectorBlockSettings>(Convert.FromBase64String(rawData));

                if (loadedSettings != null)
                {
                    Settings.Range = loadedSettings.Range;
                    Settings.Strength = loadedSettings.Strength;
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error loading settings!\n{e}");
            }

            return false;
        }

        bool ParseLegacyNameStorage()
        {
            string name = block.CustomName.TrimEnd(' ');

            if (!name.EndsWith("]", StringComparison.Ordinal))
                return false;

            int startIndex = name.IndexOf('[');

            if (startIndex == -1)
                return false;

            var settingsStr = name.Substring(startIndex + 1, name.Length - startIndex - 2);

            if (settingsStr.Length == 0)
                return false;

            string[] args = settingsStr.Split(';');

            if (args.Length == 0)
                return false;

            string[] data;

            foreach (string arg in args)
            {
                data = arg.Split('=');

                float f;
                int i;

                if (data.Length == 2)
                {
                    switch (data[0])
                    {
                        case "range":
                            if (int.TryParse(data[1], out i))
                                Range = i;
                            break;
                        case "str":
                            if (float.TryParse(data[1], out f))
                                StrengthMul = f;
                            break;
                    }
                }
            }

            block.CustomName = name.Substring(0, startIndex).Trim();
            return true;
        }

        void SaveSettings()
        {
            if (block == null)
                return;

            if (Settings == null)
                throw new NullReferenceException($"Settings == null on entId={Entity?.EntityId}; modInstance={GravityCollectorMod.Instance != null}");

            if (MyAPIGateway.Utilities == null)
                throw new NullReferenceException($"MyAPIGateway.Utilities == null; entId={Entity?.EntityId}; modInstance={GravityCollectorMod.Instance != null}");

            if (block.Storage == null)
                block.Storage = new MyModStorageComponent();

            block.Storage.SetValue(SETTINGS_GUID, Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Settings)));
        }

        void SettingsChanged()
        {
            if (syncCountdown == 0)
                syncCountdown = SETTINGS_CHANGED_COUNTDOWN;
        }

        void SyncSettings()
        {
            if (syncCountdown > 0 && --syncCountdown <= 0)
            {
                SaveSettings();

                Mod.CachedPacketSettings.Send(block.EntityId, Settings);
            }
        }

        public override bool IsSerialized()
        {
            try
            {
                SaveSettings();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            return base.IsSerialized();
        }

        public Vector3D GetCollectionPoint()
        {
            return block.WorldMatrix.Translation + (block.WorldMatrix.Forward * offset);
        }

        public float GetEffectiveVolume()
        {
            float height = Range;
            float radius = height * (float)Math.Tan(coneAngle);
            return (float)(Math.PI * radius * radius * height / 3.0);
        }

        public float GetIdealVelocityForDistance(double distance)
        {
            float normalizedDist = (float)(distance / Range);
            float baseVelocity = 10f;
            return baseVelocity * (1f - normalizedDist * 0.5f);
        }

        public void UpdateForceMultiplier(float multiplier)
        {
            adaptiveForceMultiplier = MathHelper.Clamp(multiplier, 0.1f, 2.0f);
        }

        private float CalculateCollectionPriority(IMyEntity entity)
        {
            float priority = 0f;

            var distance = Vector3D.Distance(GetCollectionPoint(), entity.GetPosition());
            priority += 1.0f - (float)(distance / Range);

            if (entity.Physics != null)
            {
                priority += Math.Min(entity.Physics.Mass / MAX_MASS, 1.0f) * 0.5f;
            }

            if (entity is IMyFloatingObject)
            {
                priority += 0.3f;
            }

            return priority;
        }

        private bool IsValidCollectionTarget(IMyEntity entity)
        {
            if (entity?.Physics == null || entity.MarkedForClose)
                return false;

            var objPos = entity.GetPosition();
            var collectPos = GetCollectionPoint();

            if (Vector3D.DistanceSquared(collectPos, objPos) > Range * Range)
                return false;

            var dirNormalized = Vector3D.Normalize(objPos - collectPos);
            var angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(block.WorldMatrix.Forward, dirNormalized), -1, 1));

            return angle <= coneAngle;
        }

        static float MinMaxPercent(float min, float max, float percentMul)
        {
            return min + (percentMul * (max - min));
        }

        #region Terminal controls

        static void SetupTerminalControls<T>()
        {
            var mod = GravityCollectorMod.Instance;

            if (mod.ControlsCreated)
                return;

            mod.ControlsCreated = true;

            var controlRange = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>(CONTROLS_PREFIX + "Range");
            controlRange.Title = MyStringId.GetOrCompute("Pull Range");
            controlRange.Tooltip = MyStringId.GetOrCompute("Max distance the cone extends to.");
            controlRange.Visible = Control_Visible;
            controlRange.SupportsMultipleBlocks = true;
            controlRange.SetLimits(Control_Range_Min, Control_Range_Max);
            controlRange.Getter = Control_Range_Getter;
            controlRange.Setter = Control_Range_Setter;
            controlRange.Writer = Control_Range_Writer;
            MyAPIGateway.TerminalControls.AddControl<T>(controlRange);

            var controlStrength = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>(CONTROLS_PREFIX + "Strength");
            controlStrength.Title = MyStringId.GetOrCompute("Pull Strength");
            controlStrength.Tooltip = MyStringId.GetOrCompute($"Formula used:\nForce = Min(ObjectMass * {MASS_MUL.ToString()}, {MAX_MASS.ToString()}) * (Strength / 100)");
            controlStrength.Visible = Control_Visible;
            controlStrength.SupportsMultipleBlocks = true;
            controlStrength.SetLimits(STRENGTH_MIN, STRENGTH_MAX);
            controlStrength.Getter = Control_Strength_Getter;
            controlStrength.Setter = Control_Strength_Setter;
            controlStrength.Writer = Control_Strength_Writer;
            MyAPIGateway.TerminalControls.AddControl<T>(controlStrength);
        }

        static GravityCollector GetLogic(IMyTerminalBlock block) => block?.GameLogic?.GetAs<GravityCollector>();

        static bool Control_Visible(IMyTerminalBlock block)
        {
            return GetLogic(block) != null;
        }

        static float Control_Strength_Getter(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return (logic == null ? STRENGTH_MIN : logic.StrengthMul * 100);
        }

        static void Control_Strength_Setter(IMyTerminalBlock block, float value)
        {
            var logic = GetLogic(block);
            if (logic != null)
                logic.StrengthMul = ((int)value / 100f);
        }

        static void Control_Strength_Writer(IMyTerminalBlock block, StringBuilder writer)
        {
            var logic = GetLogic(block);
            if (logic != null)
                writer.Append((int)(logic.StrengthMul * 100f)).Append('%');
        }

        static float Control_Range_Getter(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return (logic == null ? 0 : logic.Range);
        }

        static void Control_Range_Setter(IMyTerminalBlock block, float value)
        {
            var logic = GetLogic(block);
            if (logic != null)
                logic.Range = (int)Math.Floor(value);
        }

        static float Control_Range_Min(IMyTerminalBlock block)
        {
            return RANGE_MIN;
        }

        static float Control_Range_Max(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return (logic == null ? 0 : logic.maxRange);
        }

        static void Control_Range_Writer(IMyTerminalBlock block, StringBuilder writer)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                if (logic.Range < RANGE_OFF_EXCLUSIVE)
                    writer.Append("OFF");
                else
                    writer.Append(logic.Range.ToString("N2")).Append(" m");
            }
        }

        #endregion
    }
    /// <summary>
    /// Manages coordination between multiple collectors on the same grid
    /// </summary>
    public class CollectorNetwork
    {
        private static Dictionary<long, CollectorNetwork> gridNetworks = new Dictionary<long, CollectorNetwork>();
        private List<GravityCollector> collectors = new List<GravityCollector>();
        private Dictionary<IMyEntity, GravityCollector> objectAssignments = new Dictionary<IMyEntity, GravityCollector>();
        private readonly long gridId;

        public static CollectorNetwork GetNetwork(long gridId)
        {
            CollectorNetwork network;
            if (!gridNetworks.TryGetValue(gridId, out network))
            {
                network = new CollectorNetwork(gridId);
                gridNetworks[gridId] = network;
            }
            return network;
        }

        public CollectorNetwork(long gridId)
        {
            this.gridId = gridId;
        }

        public void RegisterCollector(GravityCollector collector)
        {
            if (!collectors.Contains(collector))
            {
                collectors.Add(collector);
                Log.Info($"Collector {collector.Entity.EntityId} registered to grid network {gridId}");
            }
        }

        public void UnregisterCollector(GravityCollector collector)
        {
            collectors.Remove(collector);

            var reassignObjects = objectAssignments.Where(kvp => kvp.Value == collector)
                                                 .Select(kvp => kvp.Key)
                                                 .ToList();
            foreach (var obj in reassignObjects)
            {
                objectAssignments.Remove(obj);
            }
        }

        public GravityCollector GetBestCollectorFor(IMyEntity entity)
        {
            GravityCollector assigned;
            if (objectAssignments.TryGetValue(entity, out assigned))
                return assigned;

            var pos = entity.GetPosition();
            return collectors.OrderBy(c => Vector3D.DistanceSquared(c.GetCollectionPoint(), pos))
                            .FirstOrDefault();
        }

        public void AssignObject(IMyEntity entity, GravityCollector collector)
        {
            objectAssignments[entity] = collector;
        }

        public void ReleaseObject(IMyEntity entity)
        {
            objectAssignments.Remove(entity);
        }
    }

    /// <summary>
    /// Handles advanced object collection behavior and queuing
    /// </summary>
    public class AdvancedCollectionSystem
    {
        private readonly GravityCollector collector;
        private readonly Dictionary<IMyEntity, CollectionPath> activePaths = new Dictionary<IMyEntity, CollectionPath>();
        private readonly PriorityQueue<IMyEntity> collectionQueue = new PriorityQueue<IMyEntity>();
        private readonly HashSet<IMyEntity> processingObjects = new HashSet<IMyEntity>();

        private const int MAX_CONCURRENT_COLLECTIONS = 8;
        private const float DENSITY_THRESHOLD = 5f;
        private const float ADAPTIVE_FORCE_MAX_MULTIPLIER = 2.0f;

        private int updateCounter = 0;
        private const int PERFORMANCE_LOG_INTERVAL = 600; // Log every 10 seconds at 60 updates/second

        public AdvancedCollectionSystem(GravityCollector collector)
        {
            this.collector = collector;
        }

        public void Update()
        {
            var startTime = DateTime.Now;

            UpdateActivePaths();
            ProcessQueue();
            AdaptForceSettings();

            if (++updateCounter >= PERFORMANCE_LOG_INTERVAL)
            {
                updateCounter = 0;
                var processingTime = (DateTime.Now - startTime).TotalMilliseconds;
                Log.Info($"Collection System Performance: {processingTime:F2}ms, Active Objects: {processingObjects.Count}, Queued: {collectionQueue.Count}");
            }
        }

        private void UpdateActivePaths()
        {
            foreach (var path in activePaths.ToList())
            {
                if (!IsPathValid(path.Key))
                {
                    activePaths.Remove(path.Key);
                    processingObjects.Remove(path.Key);
                    collector.network.ReleaseObject(path.Key);
                    continue;
                }

                path.Value.Update();
                ApplyPathForces(path.Key, path.Value);
            }
        }

        private void ProcessQueue()
        {
            while (processingObjects.Count < MAX_CONCURRENT_COLLECTIONS && collectionQueue.Count > 0)
            {
                var nextObject = collectionQueue.Dequeue();
                if (IsValidForCollection(nextObject))
                {
                    InitiateCollection(nextObject);
                }
            }
        }

        private void InitiateCollection(IMyEntity entity)
        {
            var path = new CollectionPath(collector, entity);
            activePaths[entity] = path;
            processingObjects.Add(entity);
            collector.network.AssignObject(entity, collector);
        }

        private void AdaptForceSettings()
        {
            var collectionPoint = collector.GetCollectionPoint();
            var activeObjects = processingObjects.Count;
            var volume = collector.GetEffectiveVolume();
            var density = activeObjects / volume;

            float adaptiveMul = MathHelper.Clamp(1.0f - (density / DENSITY_THRESHOLD),
                                               1.0f / ADAPTIVE_FORCE_MAX_MULTIPLIER,
                                               ADAPTIVE_FORCE_MAX_MULTIPLIER);

            collector.UpdateForceMultiplier(adaptiveMul);
        }

        public void QueueObject(IMyEntity entity, float priority)
        {
            if (!processingObjects.Contains(entity) && !activePaths.ContainsKey(entity))
            {
                collectionQueue.Enqueue(entity, priority);
            }
        }

        private bool IsPathValid(IMyEntity entity)
        {
            if (entity?.Physics == null || entity.MarkedForClose)
                return false;

            var distance = Vector3D.Distance(entity.GetPosition(), collector.GetCollectionPoint());
            return distance <= collector.Range;
        }

        private bool IsValidForCollection(IMyEntity entity)
        {
            return entity?.Physics != null && !entity.MarkedForClose;
        }

        private void ApplyPathForces(IMyEntity entity, CollectionPath path)
        {
            if (entity?.Physics == null)
            {
                Log.Info($"Failed to apply forces to entity {entity?.EntityId}: Physics null");
                return;
            }

            var currentVelocity = entity.Physics.LinearVelocity;
            var idealForce = path.GetIdealForce(currentVelocity);

            idealForce *= collector.adaptiveForceMultiplier;

            Vector3D randomOffset = new Vector3D(
                MyUtils.GetRandomFloat(-0.1f, 0.1f),
                MyUtils.GetRandomFloat(-0.1f, 0.1f),
                MyUtils.GetRandomFloat(-0.1f, 0.1f)
            );

            idealForce += idealForce * randomOffset;

            try
            {
                entity.Physics.AddForce(
                    MyPhysicsForceType.APPLY_WORLD_FORCE,
                    idealForce,
                    entity.GetPosition(),
                    null
                );

                Log.Info($"Applied force to entity {entity.EntityId}: Force={idealForce.Length():F2}");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to apply force to entity {entity.EntityId}: {e.Message}");
            }
        }
    }


    /// <summary>
    /// Represents and calculates an optimal collection path for an object
    /// </summary>
    /// <summary>
    /// Represents and calculates an optimal collection path for an object
    /// </summary>
    public class CollectionPath
    {
        private readonly GravityCollector collector;
        private readonly IMyEntity target;
        private readonly List<Vector3D> pathPoints = new List<Vector3D>();
        private Vector3D currentIdealPosition;
        private Vector3D currentIdealVelocity;

        private const int PATH_POINTS = 10;
        private const float PATH_UPDATE_FREQUENCY = 6;
        private const float ARRIVAL_THRESHOLD = 0.5f;

        public CollectionPath(GravityCollector collector, IMyEntity target)
        {
            this.collector = collector;
            this.target = target;
            CalculatePath();
        }

        public void Update()
        {
            if (MyAPIGateway.Session.GameDateTime.Millisecond % (1000 / PATH_UPDATE_FREQUENCY) == 0)
            {
                CalculatePath();
            }

            UpdateIdealPositionAndVelocity();
        }

        private void CalculatePath()
        {
            pathPoints.Clear();
            var start = target.GetPosition();
            var end = collector.GetCollectionPoint();

            for (int i = 0; i < PATH_POINTS; i++)
            {
                var t = i / (float)(PATH_POINTS - 1);
                var point = Vector3D.Lerp(start, end, t);

                point = AvoidObstacles(point);
                pathPoints.Add(point);
            }
        }

private Vector3D AvoidObstacles(Vector3D point)
{
    const double AVOIDANCE_RADIUS = 2.0;
    const double REPULSION_STRENGTH = 1.0;
    
    var nearbyEntities = new List<MyEntity>(); // Change to MyEntity
    var sphere = new BoundingSphereD(point, AVOIDANCE_RADIUS);
    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, nearbyEntities, MyEntityQueryType.Dynamic);

    Vector3D avoidanceForce = Vector3D.Zero;
    foreach (var entity in nearbyEntities)
    {
        if (entity == target) continue;
        
        var entityPos = entity.PositionComp.GetPosition();
        var direction = point - entityPos;
        var distance = direction.Length();
        
        if (distance < AVOIDANCE_RADIUS)
        {
            direction.Normalize();
            avoidanceForce += direction * (REPULSION_STRENGTH * (1.0 - distance/AVOIDANCE_RADIUS));
        }
    }

    return point + avoidanceForce;
}


        private void UpdateIdealPositionAndVelocity()
        {
            var currentPos = target.GetPosition();
            var collectorPos = collector.GetCollectionPoint();

            // Check if we've arrived at the collector
            if (Vector3D.Distance(currentPos, collectorPos) < ARRIVAL_THRESHOLD)
            {
                currentIdealVelocity = Vector3D.Zero;
                return;
            }

            var pathIndex = FindClosestPathIndex(currentPos);
            if (pathIndex < pathPoints.Count - 1)
            {
                currentIdealPosition = pathPoints[pathIndex + 1];
                currentIdealVelocity = Vector3D.Normalize(currentIdealPosition - currentPos) *
                                     collector.GetIdealVelocityForDistance(Vector3D.Distance(currentPos, collectorPos));
            }
        }

        public Vector3D GetIdealForce(Vector3D currentVelocity)
        {
            var targetVelocity = currentIdealVelocity;
            return (targetVelocity - currentVelocity) * target.Physics.Mass;
        }

        private int FindClosestPathIndex(Vector3D position)
        {
            int closestIndex = 0;
            double closestDistance = double.MaxValue;

            for (int i = 0; i < pathPoints.Count; i++)
            {
                double distance = Vector3D.DistanceSquared(position, pathPoints[i]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }
    }
    public class PriorityQueue<T>
    {
        private SortedDictionary<float, Queue<T>> queue = new SortedDictionary<float, Queue<T>>();

        public int Count { get; private set; }

        public void Enqueue(T item, float priority)
        {
            Queue<T> itemQueue;
            if (!queue.TryGetValue(priority, out itemQueue))
            {
                itemQueue = new Queue<T>();
                queue[priority] = itemQueue;
            }

            itemQueue.Enqueue(item);
            Count++;
        }

        public T Dequeue()
        {
            if (Count == 0)
                throw new InvalidOperationException("Queue is empty");

            var highestPriority = queue.Keys.Last();
            var itemQueue = queue[highestPriority];
            var item = itemQueue.Dequeue();

            if (itemQueue.Count == 0)
                queue.Remove(highestPriority);

            Count--;
            return item;
        }
    }
}