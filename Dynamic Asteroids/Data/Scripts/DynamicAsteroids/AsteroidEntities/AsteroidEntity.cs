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
                if (MainSession.I == null)
                {
                    Log.Exception(new Exception("MainSession.I is null"), typeof(AsteroidEntity), "MainSession.I is not initialized.");
                    return;
                }
                Log.Info("MainSession.I is initialized.");

                if (MainSession.I.ModContext == null)
                {
                    Log.Exception(new Exception("MainSession.I.ModContext is null"), typeof(AsteroidEntity), "MainSession.I.ModContext is not initialized.");
                    return;
                }
                Log.Info("MainSession.I.ModContext is initialized.");

                string modPath = MainSession.I.ModContext.ModPath;
                if (string.IsNullOrEmpty(modPath))
                {
                    Log.Exception(new Exception("MainSession.I.ModContext.ModPath is null or empty"), typeof(AsteroidEntity), "MainSession.I.ModContext.ModPath is not initialized.");
                    return;
                }
                Log.Info($"ModPath: {modPath}");

                if (MainSession.I.Rand == null)
                {
                    Log.Exception(new Exception("MainSession.I.Rand is null"), typeof(AsteroidEntity), "Random number generator is not initialized.");
                    return;
                }

                Type = type;
                Log.Info($"Asteroid Type: {type}");

                switch (type)
                {
                    case AsteroidType.Ice:
                        if (IceAsteroidModels.Length == 0)
                        {
                            Log.Info("IceAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(IceAsteroidModels.Length);
                            Log.Info($"Selected model index for Ice: {modelIndex}");
                            ModelString = Path.Combine(modPath, IceAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Stone:
                        if (StoneAsteroidModels.Length == 0)
                        {
                            Log.Info("StoneAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(StoneAsteroidModels.Length);
                            Log.Info($"Selected model index for Stone: {modelIndex}");
                            ModelString = Path.Combine(modPath, StoneAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Iron:
                        if (IronAsteroidModels.Length == 0)
                        {
                            Log.Info("IronAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(IronAsteroidModels.Length);
                            Log.Info($"Selected model index for Iron: {modelIndex}");
                            ModelString = Path.Combine(modPath, IronAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Nickel:
                        if (NickelAsteroidModels.Length == 0)
                        {
                            Log.Info("NickelAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(NickelAsteroidModels.Length);
                            Log.Info($"Selected model index for Nickel: {modelIndex}");
                            ModelString = Path.Combine(modPath, NickelAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Cobalt:
                        if (CobaltAsteroidModels.Length == 0)
                        {
                            Log.Info("CobaltAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(CobaltAsteroidModels.Length);
                            Log.Info($"Selected model index for Cobalt: {modelIndex}");
                            ModelString = Path.Combine(modPath, CobaltAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Magnesium:
                        if (MagnesiumAsteroidModels.Length == 0)
                        {
                            Log.Info("MagnesiumAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(MagnesiumAsteroidModels.Length);
                            Log.Info($"Selected model index for Magnesium: {modelIndex}");
                            ModelString = Path.Combine(modPath, MagnesiumAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Silicon:
                        if (SiliconAsteroidModels.Length == 0)
                        {
                            Log.Info("SiliconAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(SiliconAsteroidModels.Length);
                            Log.Info($"Selected model index for Silicon: {modelIndex}");
                            ModelString = Path.Combine(modPath, SiliconAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Silver:
                        if (SilverAsteroidModels.Length == 0)
                        {
                            Log.Info("SilverAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(SilverAsteroidModels.Length);
                            Log.Info($"Selected model index for Silver: {modelIndex}");
                            ModelString = Path.Combine(modPath, SilverAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Gold:
                        if (GoldAsteroidModels.Length == 0)
                        {
                            Log.Info("GoldAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(GoldAsteroidModels.Length);
                            Log.Info($"Selected model index for Gold: {modelIndex}");
                            ModelString = Path.Combine(modPath, GoldAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Platinum:
                        if (PlatinumAsteroidModels.Length == 0)
                        {
                            Log.Info("PlatinumAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(PlatinumAsteroidModels.Length);
                            Log.Info($"Selected model index for Platinum: {modelIndex}");
                            ModelString = Path.Combine(modPath, PlatinumAsteroidModels[modelIndex]);
                        }
                        break;
                    case AsteroidType.Uraninite:
                        if (UraniniteAsteroidModels.Length == 0)
                        {
                            Log.Info("UraniniteAsteroidModels array is empty");
                        }
                        else
                        {
                            int modelIndex = MainSession.I.Rand.Next(UraniniteAsteroidModels.Length);
                            Log.Info($"Selected model index for Uraninite: {modelIndex}");
                            ModelString = Path.Combine(modPath, UraniniteAsteroidModels[modelIndex]);
                        }
                        break;
                    default:
                        Log.Info("Invalid AsteroidType, setting ModelString to empty.");
                        ModelString = "";
                        break;
                }
                Log.Info($"ModelString: {ModelString}");

                if (string.IsNullOrEmpty(ModelString))
                {
                    Log.Exception(new Exception("ModelString is null or empty"), typeof(AsteroidEntity), "Failed to initialize asteroid entity");
                    return; // Early exit if ModelString is not set
                }

                Size = size;
                _integrity = AsteroidSettings.BaseIntegrity * Size;
                Log.Info($"Base Integrity: {AsteroidSettings.BaseIntegrity}, Size: {Size}, Total Integrity: {_integrity}");

                Log.Info($"Attempting to load model: {ModelString}");

                Init(null, ModelString, null, Size);

                this.Save = false;
                this.NeedsWorldMatrix = true;   //this might be related to hitbox desyncing

                Log.Info("Setting WorldMatrix");
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
                Log.Info($"WorldMatrix: {this.WorldMatrix}");

                Log.Info("Adding entity to MyEntities");
                MyEntities.Add(this);
                Log.Info($"{(MyAPIGateway.Session.IsServer ? "Server" : "Client")}: Added asteroid entity with ID {this.EntityId} to MyEntities");

                Log.Info("Creating physics");
                CreatePhysics();
                this.Physics.LinearVelocity = initialVelocity + RandVector() * AsteroidSettings.VelocityVariability;
                this.Physics.AngularVelocity = RandVector() * AsteroidSettings.GetRandomAngularVelocity(MainSession.I.Rand);
                Log.Info($"Initial LinearVelocity: {this.Physics.LinearVelocity}, Initial AngularVelocity: {this.Physics.AngularVelocity}");

                Log.Info($"Asteroid model {ModelString} loaded successfully with initial angular velocity: {this.Physics.AngularVelocity}");

                if (MyAPIGateway.Session.IsServer)
                {
                    this.SyncFlag = true;
                }
            }
            catch (Exception ex)
            {
                Log.Info($"Exception Type: {ex.GetType()}");
                Log.Info($"Exception Message: {ex.Message}");
                Log.Info($"Exception Stack Trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Log.Info($"Inner Exception Type: {ex.InnerException.GetType()}");
                    Log.Info($"Inner Exception Message: {ex.InnerException.Message}");
                    Log.Info($"Inner Exception Stack Trace: {ex.InnerException.StackTrace}");
                }
                Log.Exception(ex, typeof(AsteroidEntity), $"Failed to load model: {ModelString}");
                this.Flags &= ~EntityFlags.Visible;
            }
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
            // Delegate to damage handler
            var damageHandler = new AsteroidDamageHandler();
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

                if (newSize == Size)
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
