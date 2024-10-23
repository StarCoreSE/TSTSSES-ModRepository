using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;
using System;
using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox.Definitions;
using VRage.Game.ModAPI;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities
{
    public class KeenRicochetMissileBSWorkaroundHandler
    {
        private static IMyMissiles _missileAPI;
        private static bool _isInitialized = false;
        private static AsteroidDamageHandler _damageHandler;

        public KeenRicochetMissileBSWorkaroundHandler(AsteroidDamageHandler damageHandler)
        {
            _damageHandler = damageHandler;
            InitializeMissileAPI();
        }

        private void InitializeMissileAPI()
        {
            if (!_isInitialized)
            {
                _missileAPI = MyAPIGateway.Missiles;
                if (_missileAPI != null)
                {
                    _missileAPI.OnMissileCollided += OnMissileCollided;
                    _isInitialized = true;
                    Log.Info("Initialized Keen missile ricochet workaround.");
                }
                else
                {
                    Log.Warning("Failed to initialize Keen missile ricochet workaround - API not available.");
                }
            }
        }

        private void OnMissileCollided(IMyMissile missile)
        {
            try
            {
                if (missile?.CollidedEntity == null) return;

                var asteroid = missile.CollidedEntity as AsteroidEntity;
                if (asteroid == null) return;

                float damage = CalculateMissileDamage(missile);
                if (damage <= 0) return;

                var hitInfo = new MyHitInfo
                {
                    Position = missile.CollisionPoint ?? missile.PositionComp.GetPosition(),
                    Normal = missile.CollisionNormal,
                    Velocity = missile.LinearVelocity
                };

                _damageHandler.DoDamage(asteroid, damage, MyStringHash.GetOrCompute("Missile"), true, hitInfo, missile.Owner);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(KeenRicochetMissileBSWorkaroundHandler),
                    "Error in Keen missile ricochet workaround");
            }
        }

        private float CalculateMissileDamage(IMyMissile missile)
        {
            var missileDefinition = missile.AmmoDefinition as MyMissileAmmoDefinition;
            if (missileDefinition == null) return 0;

            // Calculate ricochet angle
            bool isRicochetAngle = false;
            float impactAngle = 0;

            if (missile.CollisionNormal != Vector3.Zero)
            {
                impactAngle = (float)Math.Acos(Vector3.Dot(missile.CollisionNormal,
                    -Vector3.Normalize(missile.LinearVelocity)));
                impactAngle = MathHelper.ToDegrees(impactAngle);

                // Check for ricochet based on the missile's ricochet angle properties
                if (missileDefinition.MissileMinRicochetAngle <= impactAngle &&
                    impactAngle <= missileDefinition.MissileMaxRicochetAngle)
                {
                    isRicochetAngle = true;
                }
            }

            // Use ricochet damage if applicable
            if (isRicochetAngle && missileDefinition.MissileRicochetDamage > 0)
            {
                Log.Info($"Ricochet hit detected - Angle: {impactAngle:F2}°, Damage: {missileDefinition.MissileRicochetDamage}");
                return missileDefinition.MissileRicochetDamage;
            }
            // Otherwise use regular explosion damage or health pool
            else if (missile.HealthPool > 0)
            {
                return missile.HealthPool;
            }
            else if (missileDefinition.MissileExplosionDamage > 0)
            {
                return missileDefinition.MissileExplosionDamage;
            }

            return 0;
        }

        public void Unload()
        {
            if (_isInitialized && _missileAPI != null)
            {
                _missileAPI.OnMissileCollided -= OnMissileCollided;
                _isInitialized = false;
                Log.Info("Cleaned up Keen missile ricochet workaround.");
            }
        }
    }
}
