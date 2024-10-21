using System;
using System.IO;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Private;
using VRage.Utils;
using VRageMath;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

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

        public int AblationStage { get; private set; } = 0;  // Tracks the current ablation stage
        public const int MaxAblationStages = 3;  // Maximum number of ablation stages
        private float[] ablationMultipliers = new float[] { 1.0f, 0.75f, 0.5f };  // Multiplier for each ablation stage


        private void CreateEffects(Vector3D position)
        {
            MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("roidbreakparticle1", position);
            MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("roidbreak", position);
        }

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

        public float Size;
        public string ModelString = "";
        public AsteroidType Type;

        public void SplitAsteroid()
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            int splits = MainSession.I.Rand.Next(2, 5);

            if (splits > Size)
                splits = (int)Math.Ceiling(Size);

            float newSize = Size / splits;

            CreateEffects(PositionComp.GetPosition());

            if (newSize <= AsteroidSettings.MinSubChunkSize)
            {
                MyPhysicalItemDefinition item = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), Type.ToString()));
                var newObject = MyObjectBuilderSerializer.CreateNewObject(item.Id.TypeId, item.Id.SubtypeId.ToString()) as MyObjectBuilder_PhysicalObject;
                for (int i = 0; i < splits; i++)
                {
                    int dropAmount = GetRandomDropAmount(Type);
                    MyFloatingObjects.Spawn(new MyPhysicalInventoryItem(dropAmount, newObject), PositionComp.GetPosition() + RandVector() * Size, Vector3D.Forward, Vector3D.Up, Physics);
                }

                var removalMessage = new AsteroidNetworkMessage(PositionComp.GetPosition(), Size, Vector3D.Zero, Vector3D.Zero, Type, false, EntityId, true, false, Quaternion.Identity);
                var removalMessageBytes = MyAPIGateway.Utilities.SerializeToBinary(removalMessage);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, removalMessageBytes);

                MainSession.I._spawner.TryRemoveAsteroid(this); // Use the TryRemoveAsteroid method
                Close();
                return;
            }

            for (int i = 0; i < splits; i++)
            {
                Vector3D newPos = PositionComp.GetPosition() + RandVector() * Size;
                Vector3D newVelocity = RandVector() * AsteroidSettings.GetRandomSubChunkVelocity(MainSession.I.Rand);
                Vector3D newAngularVelocity = RandVector() * AsteroidSettings.GetRandomSubChunkAngularVelocity(MainSession.I.Rand);
                Quaternion newRotation = Quaternion.CreateFromYawPitchRoll(
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi);

                var subChunk = CreateAsteroid(newPos, newSize, newVelocity, Type, newRotation);
                subChunk.Physics.AngularVelocity = newAngularVelocity;

                MainSession.I._spawner.AddAsteroid(subChunk); // Use the AddAsteroid method

                var message = new AsteroidNetworkMessage(newPos, newSize, newVelocity, newAngularVelocity, Type, true, subChunk.EntityId, false, true, newRotation);
                var messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
            }

            var finalRemovalMessage = new AsteroidNetworkMessage(PositionComp.GetPosition(), Size, Vector3D.Zero, Vector3D.Zero, Type, false, EntityId, true, false, Quaternion.Identity);
            var finalRemovalMessageBytes = MyAPIGateway.Utilities.SerializeToBinary(finalRemovalMessage);
            MyAPIGateway.Multiplayer.SendMessageToOthers(32000, finalRemovalMessageBytes);

            MainSession.I._spawner.TryRemoveAsteroid(this); // Use the TryRemoveAsteroid method
            Close();
        }
        private int GetRandomDropAmount(AsteroidType type)
        {
            switch (type)
            {
                case AsteroidType.Ice:
                    return MainSession.I.Rand.Next(AsteroidSettings.IceDropRange[0], AsteroidSettings.IceDropRange[1]);
                case AsteroidType.Stone:
                    return MainSession.I.Rand.Next(AsteroidSettings.StoneDropRange[0], AsteroidSettings.StoneDropRange[1]);
                case AsteroidType.Iron:
                    return MainSession.I.Rand.Next(AsteroidSettings.IronDropRange[0], AsteroidSettings.IronDropRange[1]);
                case AsteroidType.Nickel:
                    return MainSession.I.Rand.Next(AsteroidSettings.NickelDropRange[0], AsteroidSettings.NickelDropRange[1]);
                case AsteroidType.Cobalt:
                    return MainSession.I.Rand.Next(AsteroidSettings.CobaltDropRange[0], AsteroidSettings.CobaltDropRange[1]);
                case AsteroidType.Magnesium:
                    return MainSession.I.Rand.Next(AsteroidSettings.MagnesiumDropRange[0], AsteroidSettings.MagnesiumDropRange[1]);
                case AsteroidType.Silicon:
                    return MainSession.I.Rand.Next(AsteroidSettings.SiliconDropRange[0], AsteroidSettings.SiliconDropRange[1]);
                case AsteroidType.Silver:
                    return MainSession.I.Rand.Next(AsteroidSettings.SilverDropRange[0], AsteroidSettings.SilverDropRange[1]);
                case AsteroidType.Gold:
                    return MainSession.I.Rand.Next(AsteroidSettings.GoldDropRange[0], AsteroidSettings.GoldDropRange[1]);
                case AsteroidType.Platinum:
                    return MainSession.I.Rand.Next(AsteroidSettings.PlatinumDropRange[0], AsteroidSettings.PlatinumDropRange[1]);
                case AsteroidType.Uraninite:
                    return MainSession.I.Rand.Next(AsteroidSettings.UraniniteDropRange[0], AsteroidSettings.UraniniteDropRange[1]);
                default:
                    return 0;
            }
        }

        public void OnDestroy()
        {
            try
            {
                SplitAsteroid();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), "Exception in OnDestroy:");
                throw; // Rethrow the exception for the debugger
            }
        }

        // Required property implementation for `IMyDestroyableObject`
        public bool UseDamageSystem => true;

        // Required property implementation for `IMyDestroyableObject`
        public float Integrity => _integrity;

        public float _integrity;

        public bool DoDamage(float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null, long attackerId = 0, long realHitEntityId = 0, bool shouldDetonateAmmo = true, MyStringHash? extraInfo = null)
        {
            try
            {
                // Pass the damageSource and hitInfo to ReduceIntegrity
                ReduceIntegrity(damage, damageSource, hitInfo);

                if (_integrity <= 0)
                {
                    OnDestroy();  // Call destruction logic when integrity reaches zero
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), "Exception in DoDamage");
                return false;
            }
        }

        public void ReduceIntegrity(float damage, MyStringHash damageSource, MyHitInfo? hitInfo)
        {
            float finalDamage = damage;
            float initialIntegrity = _integrity;

            if (damageSource.String == "Explosion")
            {
                finalDamage *= 10.0f;
                Log.Info($"Explosion detected! Applying 10x damage multiplier. Original Damage: {damage}, Final Damage: {finalDamage}");

                _integrity -= finalDamage;
                if (_integrity <= 0)
                {
                    OnDestroy();  // Split the asteroid on explosive damage
                }
            }
            else if (damageSource.String == "Bullet")
            {
                Log.Info($"Bullet damage detected. Original Damage: {damage}");

                // Apply damage
                _integrity -= finalDamage;

                if (_integrity <= 0)
                {
                    // If it's not the final ablation stage, ablate and spawn debris at impact location
                    if (AblationStage < MaxAblationStages - 1)
                    {
                        AblateAsteroid(hitInfo);  // Perform ablation at impact site
                    }
                    else
                    {
                        Log.Info("Asteroid fully ablated and destroyed.");
                        OnDestroy();  // Final destruction after all stages
                    }
                }
                else
                {
                    if (hitInfo.HasValue)
                    {
                        // Calculate the percentage of health lost and spawn debris accordingly
                        float healthLostRatio = finalDamage / initialIntegrity;
                        SpawnDebrisAtImpact(hitInfo.Value.Position, healthLostRatio);
                    }
                }
            }
            else
            {
                _integrity -= finalDamage;
                if (_integrity <= 0)
                {
                    OnDestroy();
                }
            }
        }

        private void AblateAsteroid(MyHitInfo? hitInfo = null)
        {
            AblationStage++;  // Move to the next ablation stage

            // Apply size reduction based on the current ablation stage
            Size *= ablationMultipliers[AblationStage];

            if (Size < AsteroidSettings.MinSubChunkSize)
            {
                Log.Info("Asteroid too small after ablation, removing it.");
                OnDestroy();
                return;
            }

            // Reset the integrity using the base integrity scaled by the new size and multiplier
            _integrity = AsteroidSettings.BaseIntegrity * Size * ablationMultipliers[AblationStage];

            Log.Info($"Asteroid ablated to stage {AblationStage}, new size: {Size}, new integrity: {_integrity}");

            // Spawn some debris at the bullet impact location, if available
            if (hitInfo.HasValue)
            {
                // Provide a default healthLostRatio for ablation (e.g., 1 for full ablation)
                SpawnDebrisAtImpact(hitInfo.Value.Position, 1.0f);  // Full drop for ablation
            }
        }

        private void SpawnDebrisAtImpact(Vector3D impactPosition, float healthLostRatio)
        {
            // Define the drop range based on asteroid type
            int[] dropRange = GetDropRange(Type);
            if (dropRange == null)
            {
                Log.Warning("Invalid asteroid type or drop range not defined.");
                return;
            }

            // Calculate the base drop amount proportional to health lost
            int minDrop = dropRange[0];
            int maxDrop = dropRange[1];

            // Apply additional scaling for weak weapons to limit debris from small hits
            // Weak hits result in almost no debris unless a significant amount of health is lost
            float scalingFactor = 0.5f; // Adjust this as needed to fine-tune how much weak weapons contribute
            int dropAmount = (int)((minDrop + (maxDrop - minDrop) * healthLostRatio) * scalingFactor);

            // Ensure that very small drops (from weak hits) are handled
            if (dropAmount < minDrop * 0.1f)
            {
                dropAmount = 1;  // Smallest possible drop, trace amount
            }

            Log.Info($"Spawning {dropAmount} debris at impact location due to {healthLostRatio:P} health lost.");

            // Create the floating debris
            MyPhysicalItemDefinition item = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), Type.ToString()));
            var newObject = MyObjectBuilderSerializer.CreateNewObject(item.Id.TypeId, item.Id.SubtypeId.ToString()) as MyObjectBuilder_PhysicalObject;

            // Spawn the items at the impact site
            MyFloatingObjects.Spawn(new MyPhysicalInventoryItem(dropAmount, newObject), impactPosition, Vector3D.Forward, Vector3D.Up, Physics);
        }

        private int[] GetDropRange(AsteroidType type)
        {
            switch (type)
            {
                case AsteroidType.Ice:
                    return AsteroidSettings.IceDropRange;
                case AsteroidType.Stone:
                    return AsteroidSettings.StoneDropRange;
                case AsteroidType.Iron:
                    return AsteroidSettings.IronDropRange;
                case AsteroidType.Nickel:
                    return AsteroidSettings.NickelDropRange;
                case AsteroidType.Cobalt:
                    return AsteroidSettings.CobaltDropRange;
                case AsteroidType.Magnesium:
                    return AsteroidSettings.MagnesiumDropRange;
                case AsteroidType.Silicon:
                    return AsteroidSettings.SiliconDropRange;
                case AsteroidType.Silver:
                    return AsteroidSettings.SilverDropRange;
                case AsteroidType.Gold:
                    return AsteroidSettings.GoldDropRange;
                case AsteroidType.Platinum:
                    return AsteroidSettings.PlatinumDropRange;
                case AsteroidType.Uraninite:
                    return AsteroidSettings.UraniniteDropRange;
                default:
                    return null;
            }
        }

        private void CreatePhysics()
        {
            float radius = Size / 2; // Assuming Size represents the diameter
            float volume = 4.0f / 3.0f * (float)Math.PI * (radius * radius * radius);
            const float density = 917.0f;// Density of ice in kg/m³
            float mass = density * volume;

            PhysicsSettings settings = MyAPIGateway.Physics.CreateSettingsForPhysics(
                this, this.WorldMatrix,
                Vector3.Zero,
                linearDamping: 0f,
                angularDamping: 0f,
                rigidBodyFlags: RigidBodyFlag.RBF_DEFAULT,
                collisionLayer: CollisionLayers.NoVoxelCollisionLayer,
                isPhantom: false,
                mass: new ModAPIMass(volume, mass, Vector3.Zero, mass * this.PositionComp.LocalAABB.Height * this.PositionComp.LocalAABB.Height / 6 * Matrix.Identity));

            MyAPIGateway.Physics.CreateSpherePhysics(settings, radius);
            this.Physics.Enabled = true;
            this.Physics.Activate();
        }

        private Vector3D RandVector()
        {
            var theta = MainSession.I.Rand.NextDouble() * 2.0 * Math.PI;
            var phi = Math.Acos(2.0 * MainSession.I.Rand.NextDouble() - 1.0);
            var sinPhi = Math.Sin(phi);
            return Math.Pow(MainSession.I.Rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }

    }
}
