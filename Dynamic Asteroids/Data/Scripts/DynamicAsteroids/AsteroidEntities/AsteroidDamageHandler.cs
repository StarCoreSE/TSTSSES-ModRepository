using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities
{
    public class AsteroidDamageHandler
    {
        private int AblationStage { get; set; } = 0;  // Tracks the current ablation stage
        private const int MaxAblationStages = 3;  // Maximum number of ablation stages
        private readonly float[] ablationMultipliers = new float[] { 1.0f, 0.75f, 0.5f };  // Multiplier for each ablation stage

        private void CreateEffects(Vector3D position)
        {
            MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("roidbreakparticle1", position);
            MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("roidbreak", position);
        }

        public void SplitAsteroid(AsteroidEntity asteroid)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            int splits = MainSession.I.Rand.Next(2, 5);

            if (splits > asteroid.Size)
                splits = (int)Math.Ceiling(asteroid.Size);

            float newSize = asteroid.Size / splits;

            CreateEffects(asteroid.PositionComp.GetPosition());

            if (newSize <= AsteroidSettings.MinSubChunkSize)
            {
                MyPhysicalItemDefinition item = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), asteroid.Type.ToString()));
                var newObject = MyObjectBuilderSerializer.CreateNewObject(item.Id.TypeId, item.Id.SubtypeId.ToString()) as MyObjectBuilder_PhysicalObject;
                for (int i = 0; i < splits; i++)
                {
                    int dropAmount = GetRandomDropAmount(asteroid.Type);
                    MyFloatingObjects.Spawn(new MyPhysicalInventoryItem(dropAmount, newObject), asteroid.PositionComp.GetPosition() + RandVector(MainSession.I.Rand) * asteroid.Size, Vector3D.Forward, Vector3D.Up, asteroid.Physics);
                }

                var removalMessage = new AsteroidNetworkMessage(asteroid.PositionComp.GetPosition(), asteroid.Size, Vector3D.Zero, Vector3D.Zero, asteroid.Type, false, asteroid.EntityId, true, false, Quaternion.Identity);
                var removalMessageBytes = MyAPIGateway.Utilities.SerializeToBinary(removalMessage);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, removalMessageBytes);

                MainSession.I._spawner.TryRemoveAsteroid(asteroid); // Use the TryRemoveAsteroid method
                asteroid.Close();
                return;
            }

            for (int i = 0; i < splits; i++)
            {
                Vector3D newPos = asteroid.PositionComp.GetPosition() + RandVector(MainSession.I.Rand) * asteroid.Size;
                Vector3D newVelocity = RandVector(MainSession.I.Rand) * AsteroidSettings.GetRandomSubChunkVelocity(MainSession.I.Rand);
                Vector3D newAngularVelocity = RandVector(MainSession.I.Rand) * AsteroidSettings.GetRandomSubChunkAngularVelocity(MainSession.I.Rand);
                Quaternion newRotation = Quaternion.CreateFromYawPitchRoll(
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                    (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi);

                var subChunk = AsteroidEntity.CreateAsteroid(newPos, newSize, newVelocity, asteroid.Type, newRotation);
                subChunk.Physics.AngularVelocity = newAngularVelocity;

                MainSession.I._spawner.AddAsteroid(subChunk); // Use the AddAsteroid method

                var message = new AsteroidNetworkMessage(newPos, newSize, newVelocity, newAngularVelocity, asteroid.Type, true, subChunk.EntityId, false, true, newRotation);
                var messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
            }

            var finalRemovalMessage = new AsteroidNetworkMessage(asteroid.PositionComp.GetPosition(), asteroid.Size, Vector3D.Zero, Vector3D.Zero, asteroid.Type, false, asteroid.EntityId, true, false, Quaternion.Identity);
            var finalRemovalMessageBytes = MyAPIGateway.Utilities.SerializeToBinary(finalRemovalMessage);
            MyAPIGateway.Multiplayer.SendMessageToOthers(32000, finalRemovalMessageBytes);

            MainSession.I._spawner.TryRemoveAsteroid(asteroid); // Use the TryRemoveAsteroid method
            asteroid.Close();
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

        public void SpawnDebrisAtImpact(AsteroidEntity asteroid, Vector3D impactPosition, float healthLostRatio)
        {
            // Define the drop range based on asteroid type
            int[] dropRange = GetDropRange(asteroid.Type);
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
            MyPhysicalItemDefinition item = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), asteroid.Type.ToString()));
            var newObject = MyObjectBuilderSerializer.CreateNewObject(item.Id.TypeId, item.Id.SubtypeId.ToString()) as MyObjectBuilder_PhysicalObject;

            // Spawn the items at the impact site
            MyFloatingObjects.Spawn(new MyPhysicalInventoryItem(dropAmount, newObject), impactPosition, Vector3D.Forward, Vector3D.Up, asteroid.Physics);
        }

        private Vector3D RandVector(Random rand)
        {
            var theta = rand.NextDouble() * 2.0 * Math.PI;
            var phi = Math.Acos(2.0 * rand.NextDouble() - 1.0);
            var sinPhi = Math.Sin(phi);
            return Math.Pow(rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }

        public bool DoDamage(AsteroidEntity asteroid, float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null, long attackerId = 0, long realHitEntityId = 0, bool shouldDetonateAmmo = true, MyStringHash? extraInfo = null)
        {
            try
            {
                Log.Info($"DoDamage called with damage: {damage}, damageSource: {damageSource}, integrity before damage: {asteroid._integrity}");

                // Pass the damageSource and hitInfo to ReduceIntegrity
                ReduceIntegrity(asteroid, damage, damageSource, hitInfo);

                if (asteroid._integrity <= 0)
                {
                    Log.Info("Asteroid integrity reached 0, calling OnDestroy.");
                    asteroid.OnDestroy();  // Call destruction logic when integrity reaches zero
                }
                else
                {
                    Log.Info($"Asteroid integrity after damage: {asteroid._integrity}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidEntity), "Exception in DoDamage");
                return false;
            }
        }

        private void ReduceIntegrity(AsteroidEntity asteroid, float damage, MyStringHash damageSource, MyHitInfo? hitInfo)
        {
            float initialIntegrity = asteroid._integrity;
            float finalDamage = damage;

            Log.Info($"ReduceIntegrity called with damage: {damage}, damageSource: {damageSource}, initial integrity: {initialIntegrity}");

            asteroid._integrity -= finalDamage;
            Log.Info($"Damage applied, new integrity: {asteroid._integrity}");

            // Calculate the percentage of integrity lost
            float healthLostRatio = 1 - (asteroid._integrity / initialIntegrity);
            Log.Info($"Health lost ratio: {healthLostRatio}");

            if (damageSource.String == "Bullet")
            {
                // Size reduction proportional to health lost
                float sizeReductionFactor = healthLostRatio * 0.1f; // Reduce more gradually
                float newSize = Math.Max(AsteroidSettings.MinSubChunkSize, asteroid.Size * (1 - sizeReductionFactor));

                Log.Info($"Size reduction factor: {sizeReductionFactor}, new size: {newSize}");

                if (newSize < asteroid.Size)
                {
                    // Update size and physics based on new size
                    asteroid.UpdateSizeAndPhysics(newSize);

                    // Spawn debris on significant hit
                    if (hitInfo.HasValue)
                    {
                        Log.Info($"Spawning debris at impact with health lost ratio: {healthLostRatio}");
                        SpawnDebrisAtImpact(asteroid, hitInfo.Value.Position, healthLostRatio);
                    }
                }
            }
        }


    }
}