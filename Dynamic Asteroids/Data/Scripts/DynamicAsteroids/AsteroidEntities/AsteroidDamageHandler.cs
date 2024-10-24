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

            float totalMass = asteroid.Properties.Mass;
            float chunkMass = totalMass * 0.1f; // 10% of mass
            Vector3D baseVelocity = asteroid.Physics?.LinearVelocity ?? Vector3D.Zero;

            SpawnDebrisAtImpact(asteroid, asteroid.PositionComp.GetPosition(), chunkMass, baseVelocity);

            var finalRemovalMessage = new AsteroidNetworkMessage(
                asteroid.PositionComp.GetPosition(),
                asteroid.Properties.Diameter,
                Vector3D.Zero,
                Vector3D.Zero,
                asteroid.Type,
                false,
                asteroid.EntityId,
                true,
                false,
                Quaternion.Identity);

            var finalRemovalMessageBytes = MyAPIGateway.Utilities.SerializeToBinary(finalRemovalMessage);
            MyAPIGateway.Multiplayer.SendMessageToOthers(32000, finalRemovalMessageBytes);

            MainSession.I._spawner.TryRemoveAsteroid(asteroid);
            asteroid.Close();
        }

        public bool DoDamage(AsteroidEntity asteroid, float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null, long attackerId = 0, long realHitEntityId = 0, bool shouldDetonateAmmo = true, MyStringHash? extraInfo = null)
        {
            float massToRemove = damage * AsteroidSettings.KgLossPerDamage;
            float instabilityIncrease = damage * AsteroidSettings.InstabilityPerDamage;
            asteroid.AddInstability(instabilityIncrease);

            if (asteroid.Properties.ShouldSpawnChunk())
            {
                float chunkMass = asteroid.Properties.Mass * AsteroidSettings.ChunkMassPercent;
                Vector3D chunkVelocity = asteroid.Physics.LinearVelocity;

                if (hitInfo.HasValue)
                {
                    Vector3D ejectionDir = Vector3D.Normalize(hitInfo.Value.Position - asteroid.PositionComp.GetPosition());
                    chunkVelocity += ejectionDir * (AsteroidSettings.ChunkEjectionVelocity +
                                                    (float)MainSession.I.Rand.NextDouble() * AsteroidSettings.ChunkVelocityRandomization);
                }

                SpawnDebrisAtImpact(asteroid, hitInfo?.Position ?? asteroid.PositionComp.GetPosition(),
                    chunkMass, chunkVelocity);

                asteroid.Properties.ReduceMass(chunkMass);
                asteroid.Properties.ResetInstability();
            }

            if (hitInfo.HasValue)
            {
                asteroid.Properties.ReduceMass(massToRemove);
                SpawnDebrisAtImpact(asteroid, hitInfo.Value.Position, massToRemove,
                    asteroid.Physics.LinearVelocity);
            }

            if (asteroid.Properties.IsDestroyed())
            {
                asteroid.OnDestroy();
                return true;
            }

            return true;
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

        public void SpawnDebrisAtImpact(AsteroidEntity asteroid, Vector3D impactPosition, float massLost,
            Vector3D chunkVelocity)
        {
            if (AsteroidSettings.EnableLogging)
            {
                MyAPIGateway.Utilities.ShowNotification($"Spawning debris:\n" +
                                                        $"Mass={massLost:F2}kg\n" +
                                                        $"Type={asteroid.Type}", 1000);
            }

            MyPhysicalItemDefinition itemDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(
                new MyDefinitionId(typeof(MyObjectBuilder_Ore), asteroid.Type.ToString()));

            var newObject = MyObjectBuilderSerializer.CreateNewObject(
                itemDefinition.Id.TypeId,
                itemDefinition.Id.SubtypeId.ToString()) as MyObjectBuilder_PhysicalObject;

            float groupingRadius = 10.0f;
            List<MyFloatingObject> nearbyDebris = GetNearbyDebris(impactPosition, groupingRadius, newObject);

            Vector3D debrisVelocity = asteroid?.Physics?.LinearVelocity ?? Vector3D.Zero;
            Vector3D randomVelocity = MyUtils.GetRandomVector3Normalized() * 10;
            debrisVelocity += randomVelocity;

            if (nearbyDebris.Count > 0)
            {
                MyFloatingObject closestDebris = nearbyDebris[0];
                MyFloatingObjects.AddFloatingObjectAmount(closestDebris, (VRage.MyFixedPoint)massLost);
            }
            else
            {
                MyFloatingObjects.Spawn(
                    new MyPhysicalInventoryItem((VRage.MyFixedPoint)massLost, newObject),
                    impactPosition,
                    Vector3D.Forward,
                    Vector3D.Up,
                    asteroid?.Physics,
                    entity => {
                        MyFloatingObject debris = entity as MyFloatingObject;
                        if (debris?.Physics != null)
                        {
                            debris.Physics.LinearVelocity = debrisVelocity;
                            debris.Physics.AngularVelocity = MyUtils.GetRandomVector3Normalized() * 5;
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
                // Only group with same type of floating objects
                if (floatingObj != null && floatingObj.Item.Content.GetType() == itemType.GetType()
                    && floatingObj.Item.Content.SubtypeName == itemType.SubtypeName)
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
            return Math.Pow(rand.NextDouble(), 1 / 3d) *
                   new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }

    }
}