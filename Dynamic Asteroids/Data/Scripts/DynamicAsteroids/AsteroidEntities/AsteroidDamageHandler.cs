using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
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
                    int dropAmount = GetDropAmount(asteroid); // Pass the entire asteroid entity, not just its type
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

        private int GetDropAmount(AsteroidEntity asteroid)
        {
            // Calculate debris amount based on asteroid mass
            float mass = asteroid.Physics.Mass;

            // Adjust this divisor to control the number of debris pieces
            // Use Math.Round to avoid strange fractional drop amounts
            int debrisCount = (int)Math.Round(mass / 500.0f);

            // Ensure at least one debris is spawned, even if the calculation rounds down to 0
            return debrisCount > 0 ? debrisCount : 1;
        }

        public void SpawnDebrisAtImpact(AsteroidEntity asteroid, Vector3D impactPosition, float massLost)
        {
            // Log debris spawning based on mass lost
            Log.Info($"Spawning debris with mass lost: {massLost} at impact position.");

            // Create the floating debris item
            MyPhysicalItemDefinition itemDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), asteroid.Type.ToString()));
            var newObject = MyObjectBuilderSerializer.CreateNewObject(itemDefinition.Id.TypeId, itemDefinition.Id.SubtypeId.ToString()) as MyObjectBuilder_PhysicalObject;

            // Group debris in a nearby radius (adjust the distance for debris grouping)
            float groupingRadius = 10.0f; // 10 meters radius for grouping debris

            // Find nearby debris to consolidate the mass into
            List<MyFloatingObject> nearbyDebris = GetNearbyDebris(impactPosition, groupingRadius, newObject);

            // Calculate how much volume to add based on mass (assume 500 mass units per item)
            float massPerItem = 500f;
            float newAmount = massLost / massPerItem;

            // Ensure at least 1 unit of debris is spawned
            newAmount = Math.Max(newAmount, 1);

            if (nearbyDebris.Count > 0)
            {
                // Add the debris mass to the closest existing debris
                MyFloatingObject closestDebris = nearbyDebris[0];

                // Use the game's method to add the amount properly
                MyFloatingObjects.AddFloatingObjectAmount(closestDebris, (VRage.MyFixedPoint)newAmount);

                // Log mass added to the existing debris
                Log.Info($"Added {massLost} mass to existing debris at {closestDebris.PositionComp.GetPosition()}");
            }
            else
            {
                // No nearby debris found, create a new one at the impact position
                MyFloatingObjects.Spawn(new MyPhysicalInventoryItem((VRage.MyFixedPoint)newAmount, newObject),
                    impactPosition, Vector3D.Forward, Vector3D.Up, asteroid.Physics);

                // Log that new debris was spawned
                Log.Info($"Spawned new debris with mass {massLost} at impact position {impactPosition}");
            }
        }

        private List<MyFloatingObject> GetNearbyDebris(Vector3D position, float radius, MyObjectBuilder_PhysicalObject itemType)
        {
            List<MyFloatingObject> nearbyDebris = new List<MyFloatingObject>();
            BoundingSphereD boundingSphereD = new BoundingSphereD(position, radius);

            foreach (var entity in MyAPIGateway.Entities.GetEntitiesInSphere(ref boundingSphereD))
            {
                MyFloatingObject floatingObj = entity as MyFloatingObject;
                if (floatingObj != null && floatingObj.Item.Content.GetType() == itemType.GetType())
                {
                    nearbyDebris.Add(floatingObj);
                }
            }
            return nearbyDebris;
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
            Log.Info($"DoDamage called with damage: {damage}, damageSource: {damageSource}, integrity (mass) before damage: {asteroid._integrity}");

            // Apply the BaseIntegrity multiplier to make the asteroid harder or easier to damage
            float adjustedDamage = damage * (100.0f / AsteroidSettings.BaseIntegrity);
            Log.Info($"Adjusted damage after applying BaseIntegrity: {adjustedDamage}");

            // Directly reduce mass instead of health ratio
            ReduceMass(asteroid, adjustedDamage, damageSource, hitInfo);

            if (asteroid._integrity <= 0)
            {
                Log.Info("Asteroid integrity reached 0, calling OnDestroy.");
                asteroid.OnDestroy();
            }
            else
            {
                Log.Info($"Asteroid integrity after damage: {asteroid._integrity}");
            }

            return true;
        }

        private void ReduceMass(AsteroidEntity asteroid, float damage, MyStringHash damageSource, MyHitInfo? hitInfo)
        {
            float initialMass = asteroid._integrity;  // Integrity represents the scaled mass.
            float finalDamage = damage;

            Log.Info($"ReduceMass called with damage: {damage}, damageSource: {damageSource}, initial mass: {initialMass}");

            // Apply a multiplier if needed (explosions, etc.)
            if (damageSource.String == "Explosion")
            {
                finalDamage *= 10.0f; // Explosions do 10x damage
            }

            // Subtract the damage from the integrity
            asteroid._integrity -= finalDamage;

            // Ensure mass and integrity are linked, reducing size based on lost mass
            Log.Info($"Damage applied, new integrity (mass): {asteroid._integrity}");

            if (asteroid._integrity <= 0)
            {
                asteroid.OnDestroy();
            }
            else
            {
                float massLost = initialMass - asteroid._integrity;
                float sizeReductionFactor = massLost / initialMass;

                // Update the size and spawn debris based on mass lost
                float newSize = Math.Max(AsteroidSettings.MinSubChunkSize, asteroid.Size * (1 - sizeReductionFactor));
                asteroid.UpdateSizeAndPhysics(newSize);

                if (hitInfo.HasValue)
                {
                    SpawnDebrisAtImpact(asteroid, hitInfo.Value.Position, massLost);
                }
            }
        }

    }
}