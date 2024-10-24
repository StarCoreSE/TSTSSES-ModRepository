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

        public AsteroidPhysicalProperties(float diameter, float density = DEFAULT_DENSITY)
        {
            Diameter = diameter;
            Radius = diameter / 2.0f;
            Density = density;

            // Calculate volume and mass
            Volume = (4.0f / 3.0f) * MathHelper.Pi * (float)Math.Pow(Radius, 3);
            Mass = Volume * Density;

            // With BaseIntegrity=1, integrity should be equal to mass
            MaximumIntegrity = Mass;
            CurrentIntegrity = MaximumIntegrity;

            // Set instability parameters
            MaxInstability = Mass * AsteroidSettings.InstabilityPerMass;
            InstabilityThreshold = MaxInstability * AsteroidSettings.InstabilityThresholdPercent;
            CurrentInstability = 0;
        }

        public void ReduceIntegrity(float amount)
        {
            float previousIntegrity = CurrentIntegrity;
            CurrentIntegrity = Math.Max(0, CurrentIntegrity - amount);

            if (Math.Abs(previousIntegrity - CurrentIntegrity) > 0.01f)
            {
                UpdateSizeFromIntegrityLoss();
            }
        }

        public void AddInstability(float amount)
        {
            CurrentInstability = Math.Min(MaxInstability, CurrentInstability + amount);
        }

        public float GetIntegrityPercentage() => (CurrentIntegrity / MaximumIntegrity) * 100f;
        public float GetInstabilityPercentage() => (CurrentInstability / MaxInstability) * 100f;
        public bool IsDestroyed() => CurrentIntegrity <= 0;
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



        public static AsteroidPhysicalProperties CreateFromMass(float targetMass, float density = DEFAULT_DENSITY)
        {
            float volume = targetMass / density;
            float radius = (float)Math.Pow((3.0f * volume) / (4.0f * MathHelper.Pi), 1.0f / 3.0f);
            return new AsteroidPhysicalProperties(radius * 2.0f, density);
        }

        private void UpdateSizeFromIntegrityLoss()
        {
            float integrityRatio = CurrentIntegrity / MaximumIntegrity;
            float newDiameter = Math.Max(
                AsteroidSettings.MinSubChunkSize,
                Diameter * (float)Math.Pow(integrityRatio, 1.0f / 3.0f)
            );

            // Update all physical properties
            Diameter = newDiameter;
            Radius = Diameter / 2.0f;
            Volume = (4.0f / 3.0f) * MathHelper.Pi * (float)Math.Pow(Radius, 3);
            Mass = Volume * Density;

            Log.Info($"Updated asteroid properties after integrity loss:\n" +
                     $"Integrity Ratio: {integrityRatio:F2}\n" +
                     $"New Diameter: {Diameter:F2}m\n" +
                     $"New Mass: {Mass:F2}kg\n" +
                     $"New Volume: {Volume:F2}m³");
        }
    }

}

