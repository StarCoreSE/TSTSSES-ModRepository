using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

            // Set integrity based on mass
            MaximumIntegrity = Mass * (AsteroidSettings.BaseIntegrity / 100.0f);
            CurrentIntegrity = MaximumIntegrity;

            // Set instability parameters
            MaxInstability = Mass * AsteroidSettings.InstabilityPerMass;
            InstabilityThreshold = MaxInstability * AsteroidSettings.InstabilityThresholdPercent;
            CurrentInstability = 0;
        }

        public void ReduceIntegrity(float amount)
        {
            CurrentIntegrity = Math.Max(0, CurrentIntegrity - amount);
        }

        public void AddInstability(float amount)
        {
            CurrentInstability = Math.Min(MaxInstability, CurrentInstability + amount);
        }

        public float GetIntegrityPercentage()
        {
            return (CurrentIntegrity / MaximumIntegrity) * 100f;
        }

        public float GetInstabilityPercentage()
        {
            return (CurrentInstability / MaxInstability) * 100f;
        }

        public bool IsDestroyed()
        {
            return CurrentIntegrity <= 0;
        }

        public bool IsUnstable()
        {
            return CurrentInstability >= InstabilityThreshold;
        }

        public static AsteroidPhysicalProperties CreateFromMass(float targetMass, float density = DEFAULT_DENSITY)
        {
            float volume = targetMass / density;
            float radius = (float)Math.Pow((3.0f * volume) / (4.0f * MathHelper.Pi), 1.0f / 3.0f);
            return new AsteroidPhysicalProperties(radius * 2.0f, density);
        }
    }
}
