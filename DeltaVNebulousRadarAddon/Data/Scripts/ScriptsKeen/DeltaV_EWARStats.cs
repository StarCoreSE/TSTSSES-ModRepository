using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using NerdRadar.Definitions;
using Sandbox.ModAPI;
using VRageMath;

namespace NerdRadar.DeltaVAddon
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class Example_EWARStats : MySessionComponentBase
    {
        BlockConfig cfg => new BlockConfig()
        {
            // Feel free to change the Example_EWARStats to something else, along with the namespace (NerdRadar.ExampleMod)
            // Aside from those, do not change anything above this line

            // Stats for all the radar blocks. How stats interact can be found here http://nebfltcom.wikidot.com/mechanics:radar
            // RCS is calculated via (LG blockcount * 0.25)^2*2.5+(SG blockcount * 0.25)^2*0.01+(Generated power in MW) * 0.5
            RadarStats = new Dictionary<string, RadarStat>()
            {
                ["LargeGrid_Search_Radar"] = new RadarStat()
                {
                    MaxRadiatedPower = 30000, // 30 MW
                    Gain = 50,
                    Sensitivity = -40,
                    MaxSearchRange = 10000000, // 10,000 km

                    ApertureSize = 300,
                    NoiseFilter = 2,
                    SignalToNoiseRatio = 1,

                    PositionError = 800,
                    VelocityError = 5,

                    CanTargetLock = false,
                    LOSCheckIncludesParentGrid = false,

                    StealthMultiplier = 1,

                    CanDetectAllJumps = true,
                    CanDetectLockedJumps = false,
                },

                ["LargeGrid_Target_Locking_Radar"] = new RadarStat()
                {
                    MaxRadiatedPower = 5000, // 5 MW
                    Gain = 55,
                    Sensitivity = -45,
                    MaxSearchRange = 250000, // 250 km

                    ApertureSize = 50,
                    NoiseFilter = 1,
                    SignalToNoiseRatio = 1,

                    PositionError = 20,
                    VelocityError = 1,

                    CanTargetLock = true,
                    LOSCheckIncludesParentGrid = true,

                    StealthMultiplier = 1,

                    CanDetectAllJumps = true,
                    CanDetectLockedJumps = true,
                },

                ["SmallGrid_Search_Radar"] = new RadarStat()
                {
                    MaxRadiatedPower = 1500, // 1.5 MW
                    Gain = 45,
                    Sensitivity = -38,
                    MaxSearchRange = 500000, // 500 km

                    ApertureSize = 15,
                    NoiseFilter = 2,
                    SignalToNoiseRatio = 1,

                    PositionError = 400,
                    VelocityError = 5,

                    CanTargetLock = false,
                    LOSCheckIncludesParentGrid = false,

                    StealthMultiplier = 1,

                    CanDetectAllJumps = true,
                    CanDetectLockedJumps = false,
                },

                ["SmallGrid_Target_Locking_Radar"] = new RadarStat()
                {
                    MaxRadiatedPower = 250, // 250 kW
                    Gain = 50,
                    Sensitivity = -40,
                    MaxSearchRange = 50000, // 50 km

                    ApertureSize = 5,
                    NoiseFilter = 1,
                    SignalToNoiseRatio = 1,

                    PositionError = 50,
                    VelocityError = 1,

                    CanTargetLock = true,
                    LOSCheckIncludesParentGrid = true,

                    StealthMultiplier = 1,

                    CanDetectAllJumps = true,
                    CanDetectLockedJumps = true,
                },

            },
            // stats for all the jammer blocks
            // stat interactions can be found here http://nebfltcom.wikidot.com/mechanics:electronic-warfare
            JammerStats = new Dictionary<string, JammerStat>()
            {
                ["LargeGrid_Jammer"] = new JammerStat()
                {
                    MaxRadiatedPower = 15000, // 15 MW
                    Gain = 15,
                    MaxSearchRange = 500000, // 500 km

                    AreaEffectRatio = 0.4f, // Larger area of effect
                    AngleRadians = MathHelperD.ToRadians(25), // 30-degree cone

                    LOSCheckIncludesParentGrid = true, // Determines whether the jammer can jam through its own grid

                    MaxHeat = 90 * 60, // Maximum heat before shutdown (75 minutes)
                    HeatDrainPerTick = 1.5f, // Higher heat dissipation rate
                },

            },

            UpgradeBlockStats = new Dictionary<string, UpgradeBlockStat>()
            {
                ["Example_UpgradeBlockStats"] = new UpgradeBlockStat()
                {
                    // these two are incompatible with eachother.
                    ApplyOnlyWhenFiring = false, // WEAPON BLOCKS ONLY. Makes all of the addons/multipliers apply only when the weapon is firing.
                    ApplyOnlyWhenOn = false, // FUNCTIONAL BLOCKS ONLY. Makes all of the addons/multipliers apply only if the block is functional.

                    PositionalErrorMultiplier = 1, // Positional error multiplier for ALL radars on the grid this is mounted on. Multipliers are calculated BEFORE addons.
                    PositionalErrorAddon = 0, // Positional error addon for ALL radars on the grid this is mounted on. Addons are calculated AFTER multipliers.

                    VelocityErrorMultiplier = 1, // Velocity error multiplier for ALL radars on the grid this is mounted on. Multipliers are calculated BEFORE addons.
                    VelocityErrorAddon = 0, // Velocity error addon for ALL radars on the grid this is mounted on. Addons are calculated AFTER multipliers.

                    NoiseFilterMultiplier = 1, // Noise filter multiplier for ALL radars on the grid this is mounted on. Multipliers are calculated BEFORE addons.
                    NoiseFilterAddon = 0, // Noise filter addon for ALL radars on the grid this is mounted on. Addons are calculated AFTER multipliers.

                    RCSMultiplier = 1, // RCS multiplier for the grid this is mounted on. Multipliers are calculated BEFORE addons.
                    RCSAddon = 0, //  RCS addon for the grid this is mounted on. Addons are calculated AFTER multipliers.

                    SensitivityMultiplier = 1, // Sensitivity multiplier for ALL radars on the grid this is mounted on. Multipliers are calculated BEFORE addons.
                    SensitivityAddon = 0, // Sensitivity addon for ALL radars on the grid this is mounted on. Addons are calculated AFTER multipliers.
                }
            },

            // IFF beacons are vanilla beacons which when placed on a grid, will replace the ship name given on its radar track with whatever its HUD name is set to.
            IFFBlockStats = new Dictionary<string, IFFBlockStat>()
            {
                ["LargeBlockIFFBeacon"] = new IFFBlockStat()
                {
                    MaxCharacters = 0, // maximum characters the IFF beacon will use
                    ShowClass = false, // whether or not the IFF beacon name change will completely replace (false) or only add its name after the class name (true)
                },
            }
        };
        // Do not touch below here
        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(cfg);
            MyAPIGateway.Utilities.SendModMessage(DefConstants.MessageHandlerId, data);
        }
    }
}
