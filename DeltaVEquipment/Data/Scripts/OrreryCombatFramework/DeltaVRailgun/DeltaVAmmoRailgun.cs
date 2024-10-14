using DeltaVEquipment.ProjectileBases;

namespace DeltaVEquipment
{
    partial class HeartDefinitions
    {
        ProjectileDefinitionBase DeltaVAmmoRailgun => new ProjectileDefinitionBase
        {
            Name = "ExampleAmmoProjectile",
            Ungrouped = new Ungrouped
            {
                ReloadPowerUsage = 10,
                Recoil = 5000,
                Impulse = 5000,
                ShotsPerMagazine = 100,
                MagazineItemToConsume = "",
            },
            Networking = new Networking
            {
                NetworkingMode = Networking.NetworkingModeEnum.FireEvent,
                DoConstantSync = false,
                NetworkPriority = 0,
            },
            Damage = new Damage
            {
                SlimBlockDamageMod = 1,
                FatBlockDamageMod = 1,
                BaseDamage = 5000,
                AreaDamage = 0,
                AreaRadius = 0,
                MaxImpacts = 1,
                DamageToProjectiles = 0.4f,
                DamageToProjectilesRadius = 0.2f,
            },
            PhysicalProjectile = new PhysicalProjectile
            {
                Velocity = 800,
                VelocityVariance = 0,
                Acceleration = 0,
                Health = 0,
                MaxTrajectory = 4000,
                MaxLifetime = -1,
                IsHitscan = false,
                GravityInfluenceMultiplier = 0.01f,
                ProjectileSize = 0.5f,
            },
            Visual = new Visual
            {
                //Model = "Models\\Weapons\\Projectile_Missile.mwm",
                //TrailTexture = MyStringId.GetOrCompute("WeaponLaser"),
                //TrailFadeTime = 0f,
                //TrailLength = 8,
                //TrailWidth = 0.5f,
                //TrailColor = new Vector4(61, 24, 24, 200),
                AttachedParticle = "DeltaV_RailgunParticle",
                ImpactParticle = "MaterialHit_Metal",
                VisibleChance = 1f,
            },
            Audio = new Audio
            {
                TravelSound = "",
                TravelVolume = 100,
                TravelMaxDistance = 1000,
                ImpactSound = "WepSmallWarheadExpl",
                SoundChance = 0.1f,
            },
            Guidance = new Guidance[]
            {
            },
            LiveMethods = new LiveMethods
            {
               // OnImpact = (projectileInfo, hitPosition, hitNormal, hitEntity) =>
               // {
               //     if (hitEntity == null)
               //         return;
               //     HeartApi.SpawnProjectilesInCone(HeartApi.GetProjectileDefinitionId(ExampleAmmoMissile.Name), hitPosition - hitNormal * 50, hitNormal, 10, 0.1f);
               // }
            }
        };
    }
}
