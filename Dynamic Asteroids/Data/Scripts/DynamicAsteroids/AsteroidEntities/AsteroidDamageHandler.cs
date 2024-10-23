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

        public bool DoDamage(AsteroidEntity asteroid, float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null, long attackerId = 0, long realHitEntityId = 0, bool shouldDetonateAmmo = true, MyStringHash? extraInfo = null)
        {
            Log.Info($"DoDamage called with damage: {damage}, asteroid integrity before damage: {asteroid._integrity}, damage source: {damageSource}");

            // For damage types like missiles, bullets, and explosions
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

                Log.Info($"Hit details - Velocity: {impactVelocity}, Normal: {normal}, Position: {hitInfo.Value.Position}, Material: {hitInfo.Value}");
            }

            // Handle mass removal for all types of damage
            float massRemoved = damage / AsteroidSettings.WeaponDamagePerKg;
            massRemoved = Math.Max(massRemoved, 1f); // Ensure at least 1kg is removed
            Log.Info($"Calculated mass to be removed: {massRemoved}kg");

            // Apply mass removal and update the asteroid size
            ReduceMass(asteroid, massRemoved, damageSource, hitInfo);

            return true;
        }

        private void ReduceMass(AsteroidEntity asteroid, float massRemoved, MyStringHash damageSource, MyHitInfo? hitInfo)
        {
            float initialMass = asteroid._integrity;
            Log.Info($"Initial asteroid mass (integrity): {initialMass}");

            asteroid._integrity -= massRemoved;

            if (asteroid._integrity <= 0)
            {
                Log.Info("Asteroid destroyed, mass after damage: 0kg");
                asteroid.OnDestroy();
            }
            else
            {
                float massLost = initialMass - asteroid._integrity;
                float sizeReductionFactor = massLost / initialMass;
                Log.Info($"Mass lost: {massLost}kg, Size reduction factor: {sizeReductionFactor}");

                if (massLost > 0)
                {
                    float newSize = Math.Max(AsteroidSettings.MinSubChunkSize, asteroid.Size * (1 - sizeReductionFactor));
                    Log.Info($"Asteroid size after damage: {newSize}");

                    asteroid.UpdateSizeAndPhysics(newSize);

                    if (hitInfo.HasValue)
                    {
                        // Spawn debris once based on the actual mass lost
                        SpawnDebrisAtImpact(asteroid, hitInfo.Value.Position, massLost);
                    }
                }
            }
        }
         }
}