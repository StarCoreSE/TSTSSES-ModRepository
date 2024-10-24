using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRageMath;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities
{
    public class AsteroidPhysicalProperties
    {
        public float Mass { get; private set; }
        public float Volume { get; private set; }
        public float Radius { get; private set; }
        public float Diameter { get; private set; }
        public float Density { get; private set; }
        public float MaximumIntegrity { get; private set; }
        public float CurrentIntegrity { get; private set; }
        public float MaxInstability { get; private set; }
        public float CurrentInstability { get; private set; }
        public float InstabilityThreshold { get; private set; }

        public const float DEFAULT_DENSITY = 917.0f; // kg/m³

        private AsteroidEntity ParentEntity { get; set; }


        public AsteroidPhysicalProperties(float diameter, float density = DEFAULT_DENSITY, AsteroidEntity parentEntity = null)
        {
            ParentEntity = parentEntity;
            Diameter = diameter;
            Radius = diameter / 2.0f;
            Density = density;

            Volume = (4.0f / 3.0f) * MathHelper.Pi * (float)Math.Pow(Radius, 3);
            Mass = Volume * Density;

            MaxInstability = Mass * AsteroidSettings.InstabilityPerMass;
            InstabilityThreshold = MaxInstability * AsteroidSettings.InstabilityThresholdPercent;
            CurrentInstability = 0;
        }

        public void AddInstability(float amount)
        {
            CurrentInstability = Math.Min(MaxInstability, CurrentInstability + amount);
        }

        public float GetIntegrityPercentage() => (CurrentIntegrity / MaximumIntegrity) * 100f;
        public float GetInstabilityPercentage() => (CurrentInstability / MaxInstability) * 100f;
        public bool IsDestroyed() => Mass <= 0;
        public bool IsUnstable() => CurrentInstability >= InstabilityThreshold;

        public void UpdateInstability()
        {
            float previousInstability = CurrentInstability;
            CurrentInstability = Math.Max(0, CurrentInstability -
                                             (AsteroidSettings.InstabilityDecayRate * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS));

            if (Math.Abs(previousInstability - CurrentInstability) > 0.01f)
            {
                Log.Info($"Instability decay: {previousInstability:F2} -> {CurrentInstability:F2} " +
                         $"(-{previousInstability - CurrentInstability:F2})");
            }
        }

        public void ReduceMass(float damageAmount)
        {
            float massToRemove = damageAmount * AsteroidSettings.KgLossPerDamage;
            float previousMass = Mass;
            Mass = Math.Max(0, Mass - massToRemove);

            if (Math.Abs(previousMass - Mass) > 0.01f)
            {
                UpdateSizeFromMassLoss();
            }
        }

        private void UpdateSizeFromMassLoss()
        {
            float newVolume = Mass / Density;
            float newRadius = (float)Math.Pow((3.0f * newVolume) / (4.0f * MathHelper.Pi), 1.0f / 3.0f);
            float newDiameter = Math.Max(AsteroidSettings.MinSubChunkSize, newRadius * 2.0f);

            if (Math.Abs(newDiameter - Diameter) > 0.1f)
            {
                Log.Info($"Size update triggered - Old diameter: {Diameter:F2}, New diameter: {newDiameter:F2}");
                Radius = newDiameter / 2.0f;
                Diameter = newDiameter;
                Volume = (4.0f / 3.0f) * MathHelper.Pi * (float)Math.Pow(Radius, 3);

                Log.Info($"Updated asteroid properties after mass loss:\n" +
                        $"New Mass: {Mass:F2}kg\n" +
                        $"New Diameter: {Diameter:F2}m\n" +
                        $"New Volume: {Volume:F2}m³");

                if (ParentEntity != null)
                {
                    ParentEntity.UpdateSizeAndPhysics(Diameter);
                }
            }
        }

        public static AsteroidPhysicalProperties CreateFromMass(float targetMass, float density = DEFAULT_DENSITY, AsteroidEntity parentEntity = null)
        {
            float volume = targetMass / density;
            float radius = (float)Math.Pow((3.0f * volume) / (4.0f * MathHelper.Pi), 1.0f / 3.0f);
            return new AsteroidPhysicalProperties(radius * 2.0f, density, parentEntity);
        }
    }

}

