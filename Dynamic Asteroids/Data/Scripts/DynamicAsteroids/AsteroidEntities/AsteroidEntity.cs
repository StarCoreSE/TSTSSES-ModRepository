using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.IO;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using Color = VRageMath.Color;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities
{
    public enum AsteroidType
    {
        Ice,
        Stone,
        Iron,
        Nickel,
        Cobalt,
        Magnesium,
        Silicon,
        Silver,
        Gold,
        Platinum,
        Uraninite
    }

    public class AsteroidEntity : MyEntity, IMyDestroyableObject
    {
        private static readonly string[] IceAsteroidModels =
        {
            @"Models\IceAsteroid_1.mwm",
            @"Models\IceAsteroid_2.mwm",
            @"Models\IceAsteroid_3.mwm",
            @"Models\IceAsteroid_4.mwm"
        };

        private static readonly string[] StoneAsteroidModels =
        {
            @"Models\StoneAsteroid_1.mwm",
            @"Models\StoneAsteroid_2.mwm",
            @"Models\StoneAsteroid_3.mwm",
            @"Models\StoneAsteroid_4.mwm",
            @"Models\StoneAsteroid_5.mwm",
            @"Models\StoneAsteroid_6.mwm",
            @"Models\StoneAsteroid_7.mwm",
            @"Models\StoneAsteroid_8.mwm",
            @"Models\StoneAsteroid_9.mwm",
            @"Models\StoneAsteroid_10.mwm",
            @"Models\StoneAsteroid_11.mwm",
            @"Models\StoneAsteroid_12.mwm",
            @"Models\StoneAsteroid_13.mwm",
            @"Models\StoneAsteroid_14.mwm",
            @"Models\StoneAsteroid_15.mwm",
            @"Models\StoneAsteroid_16.mwm"
        };

        private static readonly string[] IronAsteroidModels = { @"Models\OreAsteroid_Iron.mwm" };
        private static readonly string[] NickelAsteroidModels = { @"Models\OreAsteroid_Nickel.mwm" };
        private static readonly string[] CobaltAsteroidModels = { @"Models\OreAsteroid_Cobalt.mwm" };
        private static readonly string[] MagnesiumAsteroidModels = { @"Models\OreAsteroid_Magnesium.mwm" };
        private static readonly string[] SiliconAsteroidModels = { @"Models\OreAsteroid_Silicon.mwm" };
        private static readonly string[] SilverAsteroidModels = { @"Models\OreAsteroid_Silver.mwm" };
        private static readonly string[] GoldAsteroidModels = { @"Models\OreAsteroid_Gold.mwm" };
        private static readonly string[] PlatinumAsteroidModels = { @"Models\OreAsteroid_Platinum.mwm" };
        private static readonly string[] UraniniteAsteroidModels = { @"Models\OreAsteroid_Uraninite.mwm" };

        public AsteroidType Type { get; private set; }
        public string ModelString = "";
        public AsteroidPhysicalProperties Properties { get; private set; }

        public float Integrity => Properties.CurrentIntegrity;

        public bool IsUnstable() => Properties.IsUnstable();

        public void UpdateInstability() => Properties.UpdateInstability();

        public void AddInstability(float amount) => Properties.AddInstability(amount);

        // Required property implementation for `IMyDestroyableObject`
        public bool UseDamageSystem => true;


        public static AsteroidEntity CreateAsteroid(Vector3D position, float size, Vector3D initialVelocity,
            AsteroidType type, Quaternion? rotation = null, long? entityId = null)
        {
            var ent = new AsteroidEntity();
            Log.Info(
                $"Creating AsteroidEntity at Position: {position}, Size: {size}, InitialVelocity: {initialVelocity}, Type: {type}");

            if (entityId.HasValue)
            {
                ent.EntityId = entityId.Value;
            }

            try
            {
                ent.Init(position, size, initialVelocity, type, rotation);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), "Failed to initialize AsteroidEntity");
                return null;
            }

            if (ent.EntityId == 0)
            {
                Log.Warning("EntityId is 0, which is invalid!");
                return null;
            }

            return ent;
        }

        private void Init(Vector3D position, float size, Vector3D initialVelocity, AsteroidType type, Quaternion? rotation)
        {
            try
            {
                if (MainSession.I == null || MainSession.I.ModContext == null ||
                    string.IsNullOrEmpty(MainSession.I.ModContext.ModPath))
                {
                    Log.Exception(new Exception("MainSession or ModContext not initialized correctly"),
                        typeof(AsteroidEntity), "Initialization failed.");
                    return;
                }

                Type = type;
                ModelString = SelectModelForAsteroidType(type);
                if (string.IsNullOrEmpty(ModelString))
                {
                    Log.Exception(new Exception("ModelString is null or empty"),
                        typeof(AsteroidEntity), "Failed to initialize asteroid model");
                    return;
                }

                // Initialize physical properties
                Properties = new AsteroidPhysicalProperties(size, AsteroidPhysicalProperties.DEFAULT_DENSITY, this);

                AsteroidSettings.MassRange massRange;
                if (AsteroidSettings.MinMaxMassByType.TryGetValue(type, out massRange))
                {
                    float clampedMass = MathHelper.Clamp(Properties.Mass, massRange.MinMass, massRange.MaxMass);
                    Properties = AsteroidPhysicalProperties.CreateFromMass(clampedMass, Properties.Density, this);
                }

                // Initialize model and position
                Init(null, ModelString, null, Properties.Diameter);
                SetupInitialPositionAndRotation(position, rotation);

                Log.Info("Adding asteroid to MyEntities");
                MyEntities.Add(this);

                Log.Info("Creating physics for asteroid");
                CreatePhysics();

                this.Physics.LinearVelocity = initialVelocity + RandVector() * AsteroidSettings.VelocityVariability;
                this.Physics.AngularVelocity =
                    RandVector() * AsteroidSettings.GetRandomAngularVelocity(MainSession.I.Rand);

                Log.Info($"Initial LinearVelocity: {this.Physics.LinearVelocity}, " +
                         $"Initial AngularVelocity: {this.Physics.AngularVelocity}");

                if (MyAPIGateway.Session.IsServer)
                {
                    this.SyncFlag = true;
                }

                Log.Info($"Initialized asteroid {EntityId} properties:\n" +
                         $"Mass: {Properties.Mass:F2}\n" +
                         $"Diameter: {Properties.Diameter:F2}\n" +
                         $"Integrity: {Properties.CurrentIntegrity:F2}/{Properties.MaximumIntegrity:F2}\n" +
                         $"Instability: {Properties.CurrentInstability:F2}/{Properties.MaxInstability:F2}");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), "Failed to initialize AsteroidEntity");
                this.Flags &= ~EntityFlags.Visible;
            }
        }

        private void SetupInitialPositionAndRotation(Vector3D position, Quaternion? rotation)
        {
            if (rotation.HasValue)
            {
                this.WorldMatrix = MatrixD.CreateFromQuaternion(rotation.Value) *
                                   MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Up);
            }
            else
            {
                var randomRotation = MatrixD.CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi));
                this.WorldMatrix = randomRotation * MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Up);
            }

            this.WorldMatrix.Orthogonalize();
            Log.Info($"WorldMatrix set for asteroid at position {position}");
        }

        private string SelectModelForAsteroidType(AsteroidType type)
        {
            // Select model based on asteroid type (same as before, refactor for clarity)
            string modPath = MainSession.I.ModContext.ModPath;
            switch (type)
            {
                case AsteroidType.Ice:
                    return GetRandomModel(IceAsteroidModels, modPath);
                case AsteroidType.Stone:
                    return GetRandomModel(StoneAsteroidModels, modPath);
                case AsteroidType.Iron:
                    return GetRandomModel(IronAsteroidModels, modPath);
                case AsteroidType.Nickel:
                    return GetRandomModel(NickelAsteroidModels, modPath);
                case AsteroidType.Cobalt:
                    return GetRandomModel(CobaltAsteroidModels, modPath);
                case AsteroidType.Magnesium:
                    return GetRandomModel(MagnesiumAsteroidModels, modPath);
                case AsteroidType.Silicon:
                    return GetRandomModel(SiliconAsteroidModels, modPath);
                case AsteroidType.Silver:
                    return GetRandomModel(SilverAsteroidModels, modPath);
                case AsteroidType.Gold:
                    return GetRandomModel(GoldAsteroidModels, modPath);
                case AsteroidType.Platinum:
                    return GetRandomModel(PlatinumAsteroidModels, modPath);
                case AsteroidType.Uraninite:
                    return GetRandomModel(UraniniteAsteroidModels, modPath);
                default:
                    Log.Info("Invalid AsteroidType, no model selected.");
                    return string.Empty;
            }
        }

        private string GetRandomModel(string[] models, string modPath)
        {
            if (models.Length == 0)
            {
                Log.Info("Model array is empty");
                return string.Empty;
            }

            int modelIndex = MainSession.I.Rand.Next(models.Length);
            Log.Info($"Selected model index: {modelIndex}");
            return Path.Combine(modPath, models[modelIndex]);
        }

        public void DrawDebugSphere()
        {
            Vector3D asteroidPosition = this.PositionComp.GetPosition();
            float radius = Properties.Radius;
            Color sphereColor = Color.Red;
            Color otherColor = Color.Yellow;

            // Draw the physics radius
            MatrixD worldMatrix = MatrixD.CreateTranslation(asteroidPosition);
            MySimpleObjectDraw.DrawTransparentSphere(ref worldMatrix, radius, ref sphereColor,
                MySimpleObjectRasterizer.Wireframe, 20);

            // Optionally draw the entity's bounding box for comparison
            BoundingBoxD localBox = PositionComp.LocalAABB;
            MatrixD boxWorldMatrix = WorldMatrix;
            MySimpleObjectDraw.DrawTransparentBox(ref boxWorldMatrix, ref localBox,
                ref otherColor, MySimpleObjectRasterizer.Wireframe, 1, 0.1f);
        }


        public void OnDestroy()
        {
            var damageHandler = new AsteroidDamageHandler();
            damageHandler.SplitAsteroid(this);
        }

        public bool DoDamage(float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null,
            long attackerId = 0, long realHitEntityId = 0, bool shouldDetonateAmmo = true,
            MyStringHash? extraInfo = null)
        {
            Log.Info(
                $"DoDamage called with damage: {damage}, damageSource: {damageSource}, " +
                $"integrity before damage: {Properties.CurrentIntegrity}");

            var damageHandler = new AsteroidDamageHandler();
            return damageHandler.DoDamage(this, damage, damageSource, sync, hitInfo, attackerId, realHitEntityId,
                shouldDetonateAmmo, extraInfo);
        }

        private void CreatePhysics()
        {
            try
            {
                PhysicsSettings settings = MyAPIGateway.Physics.CreateSettingsForPhysics(
                    this,
                    this.WorldMatrix,
                    Vector3.Zero,
                    linearDamping: 0f,
                    angularDamping: 0f,
                    rigidBodyFlags: RigidBodyFlag.RBF_DEFAULT,
                    collisionLayer: CollisionLayers.NoVoxelCollisionLayer,
                    isPhantom: false,
                    mass: new ModAPIMass(
                        Properties.Volume,
                        Properties.Mass,
                        Vector3.Zero,
                        Properties.Mass * this.PositionComp.LocalAABB.Height *
                        this.PositionComp.LocalAABB.Height / 6 * Matrix.Identity
                    )
                );

                MyAPIGateway.Physics.CreateSpherePhysics(settings, Properties.Radius);
                this.Physics.Enabled = true;
                this.Physics.Activate();

                Log.Info($"Created physics for asteroid {EntityId}:\n" +
                         $"Radius: {Properties.Radius:F2}m\n" +
                         $"Mass: {Properties.Mass:F2}kg\n" +
                         $"Volume: {Properties.Volume:F2}m³");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity),
                    $"Error creating physics for asteroid {EntityId}");
            }
        }

        private Vector3D RandVector()
        {
            var theta = MainSession.I.Rand.NextDouble() * 2.0 * Math.PI;
            var phi = Math.Acos(2.0 * MainSession.I.Rand.NextDouble() - 1.0);
            var sinPhi = Math.Sin(phi);
            return Math.Pow(MainSession.I.Rand.NextDouble(), 1 / 3d) *
                   new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }

        public void UpdateSizeAndPhysics(float newDiameter)
        {
            try
            {
                Log.Info($"UpdateSizeAndPhysics called - Current: {Properties.Diameter:F2}, New: {newDiameter:F2}");

                if (Math.Abs(newDiameter - Properties.Diameter) < 0.1f)
                {
                    Log.Info("Size change too small, skipping physics update.");
                    return;
                }

                // Store current physics state
                Vector3D linearVelocity = Physics?.LinearVelocity ?? Vector3D.Zero;
                Vector3D angularVelocity = Physics?.AngularVelocity ?? Vector3D.Zero;
                MatrixD currentWorldMatrix = WorldMatrix;

                // Dispose of old physics
                if (Physics != null)
                {
                    Log.Info($"Disposing old physics for asteroid {EntityId}");
                    Physics.Close();
                    Physics = null;
                }

                // Update model scale
                float scale = newDiameter / Properties.Diameter; // Calculate relative scale
                this.PositionComp.Scale = scale;

                // Create new properties with new size
                Properties = new AsteroidPhysicalProperties(newDiameter, Properties.Density, this);

                // Recreate physics
                CreatePhysics();

                // Restore physics state
                if (Physics != null)
                {
                    Physics.LinearVelocity = linearVelocity;
                    Physics.AngularVelocity = angularVelocity;
                    PositionComp.SetWorldMatrix(ref currentWorldMatrix);

                    Log.Info($"Physics updated - New values:\n" +
                             $"Diameter: {Properties.Diameter:F2}m\n" +
                             $"Mass: {Properties.Mass:F2}kg\n" +
                             $"Scale: {scale:F2}\n" +
                             $"Collision radius: {Properties.Radius:F2}m");
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity),
                    $"Error updating size and physics for asteroid {EntityId}");
            }
        }
    }
}