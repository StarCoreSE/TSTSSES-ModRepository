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

        private void CreateEffects(Vector3D position)
        {
            MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("roidbreakparticle1", position);
            MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("roidbreak", position);
        }

        public void SplitAsteroid(AsteroidEntity asteroid)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            const int splits = 2;
            float totalMass = asteroid.Properties.Mass;

            // Calculate mass distribution
            float massRatio = asteroid.IsUnstable() ? 0.4f : 0.8f;
            float massForNewAsteroids = totalMass * massRatio;
            float massForDebris = totalMass - massForNewAsteroids;

            // Create debris first
            if (massForDebris > 0)
            {
                SpawnDebrisAtImpact(asteroid, asteroid.PositionComp.GetPosition(), massForDebris);
            }

            // Create new asteroids using AsteroidPhysicalProperties
            float massPerAsteroid = massForNewAsteroids / splits;
            var newProperties = AsteroidPhysicalProperties.CreateFromMass(massPerAsteroid);

            Log.Info($"Splitting asteroid {asteroid.EntityId}:");
            Log.Info($"Original mass: {totalMass:F2}, diameter: {asteroid.Properties.Diameter:F2}");
            Log.Info($"Mass ratio: {massRatio:F2} (debris: {massForDebris:F2}, new asteroids: {massForNewAsteroids:F2})");
            Log.Info($"New asteroid diameter: {newProperties.Diameter:F2}");

            if (newProperties.Diameter <= AsteroidSettings.MinSubChunkSize)
            {
                SpawnDebrisAtImpact(asteroid, asteroid.PositionComp.GetPosition(), totalMass);
            }
            else
            {
                for (int i = 0; i < splits; i++)
                {
                    Vector3D offset = RandVector(MainSession.I.Rand) * asteroid.Properties.Diameter * 0.6f;
                    Vector3D newPos = asteroid.PositionComp.GetPosition() + offset;
                    Vector3D newVelocity = asteroid.Physics.LinearVelocity +
                        RandVector(MainSession.I.Rand) * AsteroidSettings.GetRandomSubChunkVelocity(MainSession.I.Rand);
                    Vector3D newAngularVelocity = RandVector(MainSession.I.Rand) *
                        AsteroidSettings.GetRandomSubChunkAngularVelocity(MainSession.I.Rand);
                    Quaternion newRotation = Quaternion.CreateFromYawPitchRoll(
                        (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                        (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                        (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi);

                    var subChunk = AsteroidEntity.CreateAsteroid(newPos, newProperties.Diameter,
                        newVelocity, asteroid.Type, newRotation);
                    if (subChunk != null)
                    {
                        subChunk.Physics.AngularVelocity = newAngularVelocity;
                        MainSession.I._spawner.AddAsteroid(subChunk);

                        var message = new AsteroidNetworkMessage(newPos, newProperties.Diameter,
                            newVelocity, newAngularVelocity, asteroid.Type, true,
                            subChunk.EntityId, false, true, newRotation);
                        var messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                        MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                    }
                }
            }

            // Remove original asteroid
            var finalRemovalMessage = new AsteroidNetworkMessage(
                asteroid.PositionComp.GetPosition(), asteroid.Properties.Diameter,
                Vector3D.Zero, Vector3D.Zero, asteroid.Type, false,
                asteroid.EntityId, true, false, Quaternion.Identity);

            var finalRemovalMessageBytes = MyAPIGateway.Utilities.SerializeToBinary(finalRemovalMessage);
            MyAPIGateway.Multiplayer.SendMessageToOthers(32000, finalRemovalMessageBytes);

            MainSession.I._spawner.TryRemoveAsteroid(asteroid);
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
            if (AsteroidSettings.EnableLogging)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    $"Spawning debris:\n" +
                    $"Mass={massLost:F2}kg\n" +
                    $"Type={asteroid.Type}", 1000);
            }

            Log.Info($"Spawning debris with mass lost: {massLost} at impact position.");
            MyPhysicalItemDefinition itemDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(
                new MyDefinitionId(typeof(MyObjectBuilder_Ore), asteroid.Type.ToString()));
            var newObject = MyObjectBuilderSerializer.CreateNewObject(itemDefinition.Id.TypeId, itemDefinition.Id.SubtypeId.ToString())
                as MyObjectBuilder_PhysicalObject;

            float groupingRadius = 10.0f;
            List<MyFloatingObject> nearbyDebris = GetNearbyDebris(impactPosition, groupingRadius, newObject);

            if (nearbyDebris.Count > 0)
            {
                MyFloatingObject closestDebris = nearbyDebris[0];
                MyFloatingObjects.AddFloatingObjectAmount(closestDebris, (VRage.MyFixedPoint)massLost);
                Log.Info($"Added {massLost} mass to existing debris at {closestDebris.PositionComp.GetPosition()}");
            }
            else
            {
                MyFloatingObjects.Spawn(
                    new MyPhysicalInventoryItem((VRage.MyFixedPoint)massLost, newObject),
                    impactPosition,
                    Vector3D.Forward,
                    Vector3D.Up,
                    asteroid.Physics,
                    entity =>
                    {
                        MyFloatingObject debris = entity as MyFloatingObject;
                        if (debris != null && debris.Physics != null)
                        {
                            debris.Physics.LinearVelocity = asteroid.Physics.LinearVelocity;
                            Vector3D randomVelocity = MyUtils.GetRandomVector3Normalized() * 10;
                            debris.Physics.LinearVelocity += randomVelocity;
                            Vector3D randomAngularVelocity = MyUtils.GetRandomVector3Normalized() * 5;
                            debris.Physics.AngularVelocity = randomAngularVelocity;
                            Log.Info($"Spawned new debris with mass {massLost} at impact position {impactPosition}, initial velocity: {debris.Physics.LinearVelocity}");
                        }
                    }
                );
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

        public bool DoDamage(AsteroidEntity asteroid, float damage, MyStringHash damageSource, bool sync,
            MyHitInfo? hitInfo = null, long attackerId = 0, long realHitEntityId = 0,
            bool shouldDetonateAmmo = true, MyStringHash? extraInfo = null)
        {
            if (hitInfo.HasValue)
            {
                Vector3D impactVelocity = hitInfo.Value.Velocity;
                Vector3 normal = hitInfo.Value.Normal;
                float impactAngle = (float)Math.Acos(Vector3.Dot(normal, -impactVelocity.Normalized()));

                if (AsteroidSettings.EnableLogging)
                {
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Impact detected:\n" +
                        $"Velocity: {impactVelocity.Length():F2}\n" +
                        $"Angle: {MathHelper.ToDegrees(impactAngle):F2}°\n" +
                        $"DamageSource: {damageSource}", 1000);
                }
            }

            Log.Info($"Processing damage for asteroid {asteroid.EntityId}:");
            Log.Info($"- Damage amount: {damage}");
            Log.Info($"- Current integrity: {asteroid.Properties.CurrentIntegrity:F2}/{asteroid.Properties.MaximumIntegrity:F2}");
            Log.Info($"- Current instability: {asteroid.Properties.GetInstabilityPercentage():F1}%");

            // Add instability
            float instabilityIncrease = damage * AsteroidSettings.InstabilityFromDamage;
            asteroid.AddInstability(instabilityIncrease);

            if (asteroid.IsUnstable())
            {
                Log.Info($"Asteroid {asteroid.EntityId} has reached critical instability - initiating destruction");
                asteroid.OnDestroy();
                return true;
            }

            // Calculate and apply integrity damage
            float integrityDamage = damage / AsteroidSettings.WeaponDamagePerKg;
            float previousIntegrity = asteroid.Properties.CurrentIntegrity;
            asteroid.Properties.ReduceIntegrity(integrityDamage);

            if (asteroid.Properties.IsDestroyed())
            {
                Log.Info("Asteroid destroyed due to integrity loss");
                asteroid.OnDestroy();
                return true;
            }

            // Calculate new size based on integrity loss
            float integrityLossRatio = (previousIntegrity - asteroid.Properties.CurrentIntegrity) /
                asteroid.Properties.MaximumIntegrity;
            float newSize = asteroid.Properties.Diameter * (1f - integrityLossRatio);

            if (Math.Abs(newSize - asteroid.Properties.Diameter) > 0.1f)
            {
                asteroid.UpdateSizeAndPhysics(newSize);
            }

            // Spawn debris at impact point
            if (hitInfo.HasValue)
            {
                SpawnDebrisAtImpact(asteroid, hitInfo.Value.Position, integrityDamage);
            }

            return true;
        }

    }
}