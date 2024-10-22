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
        private static readonly string[] IceAsteroidModels = {
        @"Models\IceAsteroid_1.mwm",
        @"Models\IceAsteroid_2.mwm",
        @"Models\IceAsteroid_3.mwm",
        @"Models\IceAsteroid_4.mwm"
    };

        private static readonly string[] StoneAsteroidModels = {
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

        public float Size;
        public string ModelString = "";

        public static AsteroidEntity CreateAsteroid(Vector3D position, float size, Vector3D initialVelocity, AsteroidType type, Quaternion? rotation = null, long? entityId = null)
        {
            var ent = new AsteroidEntity();
            Log.Info($"Creating AsteroidEntity at Position: {position}, Size: {size}, InitialVelocity: {initialVelocity}, Type: {type}");

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
            Log.Info($"AsteroidEntity.Init called with position: {position}, size: {size}, initialVelocity: {initialVelocity}, type: {type}");

            try
            {
                if (MainSession.I == null || MainSession.I.ModContext == null || string.IsNullOrEmpty(MainSession.I.ModContext.ModPath))
                {
                    Log.Exception(new Exception("MainSession or ModContext not initialized correctly"), typeof(AsteroidEntity), "Initialization failed.");
                    return;
                }

                // Assign asteroid type and model
                Type = type;
                ModelString = SelectModelForAsteroidType(type);
                if (string.IsNullOrEmpty(ModelString))
                {
                    Log.Exception(new Exception("ModelString is null or empty"), typeof(AsteroidEntity), "Failed to initialize asteroid model");
                    return;
                }

                // Calculate volume and mass based on size (assuming size is diameter)
                float radius = size / 2.0f;
                float volume = (4.0f / 3.0f) * MathHelper.Pi * (float)Math.Pow(radius, 3);  // Volume of a sphere
                const float density = 917.0f; // Example density (adjust based on material)
                float mass = density * volume;

                // Set integrity proportional to the mass, scaled by BaseIntegrity (BaseIntegrity = 100 means 1:1 correlation)
                _integrity = (AsteroidSettings.BaseIntegrity / 100.0f) * mass;
                Log.Info($"Calculated Integrity: {_integrity}, based on BaseIntegrity: {AsteroidSettings.BaseIntegrity}, Mass: {mass}");

                // Initialize model, physics, and position
                Size = size;
                Init(null, ModelString, null, Size);
                SetupInitialPositionAndRotation(position, rotation);

                Log.Info("Adding asteroid to MyEntities");
                MyEntities.Add(this);

                // Set up physics
                Log.Info("Creating physics for asteroid");
                CreatePhysics();
                this.Physics.LinearVelocity = initialVelocity + RandVector() * AsteroidSettings.VelocityVariability;
                this.Physics.AngularVelocity = RandVector() * AsteroidSettings.GetRandomAngularVelocity(MainSession.I.Rand);
                Log.Info($"Initial LinearVelocity: {this.Physics.LinearVelocity}, Initial AngularVelocity: {this.Physics.AngularVelocity}");

                // Set sync flag for server
                if (MyAPIGateway.Session.IsServer)
                {
                    this.SyncFlag = true;
                }
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
                this.WorldMatrix = MatrixD.CreateFromQuaternion(rotation.Value) * MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Up);
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
            // Get the current position of the asteroid
            Vector3D asteroidPosition = this.PositionComp.GetPosition();

            // Set the color and radius of the debug sphere
            float radius = this.Size / 2; // Assuming the Size represents the diameter of the asteroid
            Color sphereColor = Color.Red;

            // Draw a transparent debug sphere at the asteroid's position
            MatrixD worldMatrix = MatrixD.CreateTranslation(asteroidPosition);
            MySimpleObjectDraw.DrawTransparentSphere(ref worldMatrix, radius, ref sphereColor, MySimpleObjectRasterizer.Wireframe, 20);
        }


        public void OnDestroy()
        {
            var damageHandler = new AsteroidDamageHandler();
            damageHandler.SplitAsteroid(this);
        }

        // Required property implementation for `IMyDestroyableObject`
        public bool UseDamageSystem => true;

        // Required property implementation for `IMyDestroyableObject`
        public float Integrity => _integrity;

        public float _integrity;

        public bool DoDamage(float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null, long attackerId = 0, long realHitEntityId = 0, bool shouldDetonateAmmo = true, MyStringHash? extraInfo = null)
        {
            Log.Info($"DoDamage called with damage: {damage}, damageSource: {damageSource}, integrity (mass) before damage: {_integrity}");

            // Call the damage handler
            var damageHandler = new AsteroidDamageHandler();

            // Ensure we aren't calling this method twice unnecessarily
            return damageHandler.DoDamage(this, damage, damageSource, sync, hitInfo, attackerId, realHitEntityId, shouldDetonateAmmo, extraInfo);
        }

        private void CreatePhysics()
        {
            try
            {
                float radius = Size / 2;
                float volume = 4.0f / 3.0f * (float)Math.PI * (radius * radius * radius);
                const float density = 917.0f;
                float mass = density * volume;

                PhysicsSettings settings = MyAPIGateway.Physics.CreateSettingsForPhysics(
                    this,
                    this.WorldMatrix,
                    Vector3.Zero,
                    linearDamping: 0f,
                    angularDamping: 0f,
                    rigidBodyFlags: RigidBodyFlag.RBF_DEFAULT,
                    collisionLayer: CollisionLayers.NoVoxelCollisionLayer,
                    isPhantom: false,
                    mass: new ModAPIMass(volume, mass, Vector3.Zero, mass * this.PositionComp.LocalAABB.Height * this.PositionComp.LocalAABB.Height / 6 * Matrix.Identity)
                );

                MyAPIGateway.Physics.CreateSpherePhysics(settings, radius);
                this.Physics.Enabled = true;
                this.Physics.Activate();

                Log.Info($"Created physics for asteroid {EntityId} with radius {radius} and mass {mass}");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), $"Error creating physics for asteroid {EntityId}");
            }
        }

        private Vector3D RandVector()
        {
            var theta = MainSession.I.Rand.NextDouble() * 2.0 * Math.PI;
            var phi = Math.Acos(2.0 * MainSession.I.Rand.NextDouble() - 1.0);
            var sinPhi = Math.Sin(phi);
            return Math.Pow(MainSession.I.Rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }

        public void UpdateSizeAndPhysics(float newSize)
        {
            try
            {
                Log.Info($"Updating asteroid size from {Size} to {newSize}");

                if (Math.Abs(newSize - Size) < 1)
                {
                    Log.Info("New size is the same as the current size, skipping update.");
                    return;
                }

                // Preserve the current velocities and world orientation before destroying the old physics
                Vector3D linearVelocity = Physics?.LinearVelocity ?? Vector3D.Zero;
                Vector3D angularVelocity = Physics?.AngularVelocity ?? Vector3D.Zero;
                MatrixD currentWorldMatrix = WorldMatrix;  // Preserve current position and orientation

                // Dispose of old physics
                if (Physics != null)
                {
                    Log.Info($"Disposing old physics for asteroid {EntityId}");
                    Physics.Close();
                    Physics = null; // Ensure the reference is cleared
                }

                // Update the size of the asteroid
                Size = newSize;

                // Recreate the physics with the new size
                Log.Info($"Creating new physics for asteroid {EntityId} with new size {Size}");
                CreatePhysics();

                // Restore the velocities and world matrix to the newly created physics body
                if (Physics != null)
                {
                    Physics.LinearVelocity = linearVelocity;
                    Physics.AngularVelocity = angularVelocity;

                    // Restore the exact world orientation (position and rotation)
                    PositionComp.SetWorldMatrix(ref currentWorldMatrix);

                    Log.Info($"Restored linear velocity: {Physics.LinearVelocity}, angular velocity: {Physics.AngularVelocity}, and orientation.");
                }

                Log.Info($"Successfully updated size and recreated physics for asteroid {EntityId}");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), $"Error updating size and physics for asteroid {EntityId}");
            }
        }

    }
}
