using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VRage;
using VRage.ModAPI;
using VRageMath;

namespace RealGasGiants
{
    public class RealGasGiantsApi
    {
        private bool _apiInit;

        private Func<Vector3D, float, Vector3I, string, float, float, float, MyPlanet> _spawnGasGiant;

        private Func<MyPlanet, string, bool> _setGasGiantConfig_name;
        private Func<MyPlanet, float, Vector3I, string, float, float, float, bool> _setGasGiantConfig_basicInfo;
        private Func<MyPlanet, MyTuple<bool, float, Vector3I, string, float>> _getGasGiantConfig_basicInfo_base;
        private Func<MyPlanet, MyTuple<bool, float, float>> _getGasGiantConfig_basicInfo_gravity;

        private Func<MyPlanet, float, float, float, bool> _setGasGiantConfig_atmoInfo;
        private Func<MyPlanet, MyTuple<bool, float, float, float>> _getGasGiantConfig_atmoInfo;

        private Func<MyPlanet, int, string, Vector3D, Vector3I, float, float, float, float, float, float, bool, bool, bool> _setGasGiantConfig_ringInfo;
        private Func<MyPlanet, int, bool, string, float, bool> _setGasGiantConfig_ringInfo_resource;
        private Func<MyPlanet, int, bool, bool, bool> _setGasGiantConfig_ringInfo_enabled;
        private Func<MyPlanet, int, MyTuple<bool, bool, float, bool>> _getGasGiantConfig_ringInfo_base;
        private Func<MyPlanet, int, MyTuple<bool, string, Vector3I, float, float, bool>> _getGasGiantConfig_ringInfo_visual;
        private Func<MyPlanet, int, MyTuple<bool, Vector3D, float, float, float>> _getGasGiantConfig_ringInfo_size;
        private Func<MyPlanet, int, MyTuple<bool, bool, string, float>> _getGasGiantConfig_ringInfo_resource;
        private Func<MyPlanet, int, MyTuple<bool, bool, bool>> _getGasGiantConfig_ringInfo_enabled;
        private Func<MyPlanet, int, bool> _removeGasGiantRing;

        private Func<MyPlanet, bool, bool, bool, bool> _setGasGiantConfig_interiorInfo;
        private Func<MyPlanet, MyTuple<bool, bool, bool, bool>> _getGasGiantConfig_interiorInfo;

        private Func<MyPlanet, bool, string, float, string, float, bool> _setGasGiantConfig_resourceInfo;
        private Func<MyPlanet, MyTuple<bool, bool, string, float, string, float>> _getGasGiantConfig_resourceInfo_planet;

        private Func<MyPlanet, int, string, Vector3D, Vector3I, float, float, float, float, float, float, bool, bool, bool> _setPlanetConfig_ringInfo;
        private Func<MyPlanet, int, bool, string, float, bool> _setPlanetConfig_ringInfo_resource;
        private Func<MyPlanet, int, bool, bool, bool> _setPlanetConfig_ringInfo_enabled;
        private Func<MyPlanet, int, MyTuple<bool, bool, float, bool>> _getPlanetConfig_ringInfo_base;
        private Func<MyPlanet, int, MyTuple<bool, string, Vector3I, float, float, bool>> _getPlanetConfig_ringInfo_visual;
        private Func<MyPlanet, int, MyTuple<bool, Vector3D, float, float, float>> _getPlanetConfig_ringInfo_size;
        private Func<MyPlanet, int, MyTuple<bool, bool, string, float>> _getPlanetConfig_ringInfo_resource;
        private Func<MyPlanet, int, MyTuple<bool, bool, bool>> _getPlanetConfig_ringInfo_enabled;
        private Func<MyPlanet, int, bool> _removePlanetRing;

        private Func<List<string>> _getGasGiantSkinList;
        private Func<List<string>> _getGasGiantRingSkinList;
        private Func<Vector3D, List<MyPlanet>> _getOverlapGasGiantsAtPosition;
        private Func<Vector3D, List<MyPlanet>> _getAtmoGasGiantsAtPosition;
        private Func<float> _getShadowFactorCamera;
        private Func<IMyEntity, float> _getShadowFactor;
        private Func<MyPlanet, Vector3D, float> _getAtmoDensity;
        private Func<Vector3D, float> _getAtmoDensityGlobal;
        private Func<MyPlanet, Vector3D, float> _getRingInfluenceAtPosition;
        private Func<Vector3D, float> _getRingInfluenceAtPositionGlobal;
        private Func<float> _getDayAtmoFactor;

        private Action _forceMainUpdate;

        private const long Channel = 321421229679;

        public bool IsReady { get; private set; }
        public bool Compromised { get; private set; }
        private void HandleMessage(object o)
        {
            if (_apiInit) return;
            var dict = o as IReadOnlyDictionary<string, Delegate>;
            var message = o as string;

            if (message != null && message == "Compromised")
                Compromised = true;

            if (dict == null || dict is ImmutableDictionary<string, Delegate>)
                return;

            var builder = ImmutableDictionary.CreateBuilder<string, Delegate>();
            foreach (var pair in dict)
                builder.Add(pair.Key, pair.Value);

            MyAPIGateway.Utilities.SendModMessage(Channel, builder.ToImmutable());

            ApiLoad(dict);
            IsReady = true;
        }

        private bool _isRegistered;

        public bool Load()
        {
            if (!_isRegistered)
            {
                _isRegistered = true;
                MyAPIGateway.Utilities.RegisterMessageHandler(Channel, HandleMessage);
            }
            if (!IsReady)
                MyAPIGateway.Utilities.SendModMessage(Channel, "ApiEndpointRequest");
            return IsReady;
        }

        public void Unload()
        {
            if (_isRegistered)
            {
                _isRegistered = false;
                MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
            }
            IsReady = false;
        }

        public void ApiLoad(IReadOnlyDictionary<string, Delegate> delegates)
        {
            _apiInit = true;

            _spawnGasGiant = (Func<Vector3D, float, Vector3I, string, float, float, float, MyPlanet>)delegates["SpawnGasGiant"];

            _setGasGiantConfig_name = (Func<MyPlanet, string, bool>)delegates["SetGasGiantConfig_Name"];
            _setGasGiantConfig_basicInfo = (Func<MyPlanet, float, Vector3I, string, float, float, float, bool>)delegates["SetGasGiantConfig_BasicInfo"];
            _getGasGiantConfig_basicInfo_base = (Func<MyPlanet, MyTuple<bool, float, Vector3I, string, float>>)delegates["GetGasGiantConfig_BasicInfo_Base"];
            _getGasGiantConfig_basicInfo_gravity = (Func<MyPlanet, MyTuple<bool, float, float>>)delegates["GetGasGiantConfig_BasicInfo_Gravity"];

            _setGasGiantConfig_atmoInfo = (Func<MyPlanet, float, float, float, bool>)delegates["SetGasGiantConfig_AtmoInfo"];
            _getGasGiantConfig_atmoInfo = (Func<MyPlanet, MyTuple<bool, float, float, float>>)delegates["GetGasGiantConfig_AtmoInfo"];

            _setGasGiantConfig_ringInfo = (Func<MyPlanet, int, string, Vector3D, Vector3I, float, float, float, float, float, float, bool, bool, bool>)delegates["SetGasGiantConfig_RingInfo"];
            _setGasGiantConfig_ringInfo_resource = (Func<MyPlanet, int, bool, string, float, bool>)delegates["SetGasGiantConfig_RingInfo_Resource"];
            _setGasGiantConfig_ringInfo_enabled = (Func<MyPlanet, int, bool, bool, bool>)delegates["SetGasGiantConfig_RingInfo_Enabled"];
            _getGasGiantConfig_ringInfo_base = (Func<MyPlanet, int, MyTuple<bool, bool, float, bool>>)delegates["GetGasGiantConfig_RingInfo_Base"];
            _getGasGiantConfig_ringInfo_visual = (Func<MyPlanet, int, MyTuple<bool, string, Vector3I, float, float, bool>>)delegates["GetGasGiantConfig_RingInfo_Visual"];
            _getGasGiantConfig_ringInfo_size = (Func<MyPlanet, int, MyTuple<bool, Vector3D, float, float, float>>)delegates["GetGasGiantConfig_RingInfo_Size"];
            _getGasGiantConfig_ringInfo_resource = (Func<MyPlanet, int, MyTuple<bool, bool, string, float>>)delegates["GetGasGiantConfig_RingInfo_Resource"];
            _getGasGiantConfig_ringInfo_enabled = (Func<MyPlanet, int, MyTuple<bool, bool, bool>>)delegates["GetGasGiantConfig_RingInfo_Enabled"];
            _removeGasGiantRing = (Func<MyPlanet, int, bool>)delegates["RemoveGasGiantRing"];

            _setGasGiantConfig_interiorInfo = (Func<MyPlanet, bool, bool, bool, bool>)delegates["SetGasGiantConfig_InteriorInfo"];
            _getGasGiantConfig_interiorInfo = (Func<MyPlanet, MyTuple<bool, bool, bool, bool>>)delegates["GetGasGiantConfig_InteriorInfo"];

            _setGasGiantConfig_resourceInfo = (Func<MyPlanet, bool, string, float, string, float, bool>)delegates["SetGasGiantConfig_ResourceInfo"];
            _getGasGiantConfig_resourceInfo_planet = (Func<MyPlanet, MyTuple<bool, bool, string, float, string, float>>)delegates["GetGasGiantConfig_ResourceInfo_Planet"];

            _setPlanetConfig_ringInfo = (Func<MyPlanet, int, string, Vector3D, Vector3I, float, float, float, float, float, float, bool, bool, bool>)delegates["SetPlanetConfig_RingInfo"];
            _setPlanetConfig_ringInfo_resource = (Func<MyPlanet, int, bool, string, float, bool>)delegates["SetPlanetConfig_RingInfo_Resource"];
            _setPlanetConfig_ringInfo_enabled = (Func<MyPlanet, int, bool, bool, bool>)delegates["SetPlanetConfig_RingInfo_Enabled"];
            _getPlanetConfig_ringInfo_base = (Func<MyPlanet, int, MyTuple<bool, bool, float, bool>>)delegates["GetPlanetConfig_RingInfo_Base"];
            _getPlanetConfig_ringInfo_visual = (Func<MyPlanet, int, MyTuple<bool, string, Vector3I, float, float, bool>>)delegates["GetPlanetConfig_RingInfo_Visual"];
            _getPlanetConfig_ringInfo_size = (Func<MyPlanet, int, MyTuple<bool, Vector3D, float, float, float>>)delegates["GetPlanetConfig_RingInfo_Size"];
            _getPlanetConfig_ringInfo_resource = (Func<MyPlanet, int, MyTuple<bool, bool, string, float>>)delegates["GetPlanetConfig_RingInfo_Resource"];
            _getPlanetConfig_ringInfo_enabled = (Func<MyPlanet, int, MyTuple<bool, bool, bool>>)delegates["GetPlanetConfig_RingInfo_Enabled"];
            _removePlanetRing = (Func<MyPlanet, int, bool>)delegates["RemovePlanetRing"];

            _getGasGiantSkinList = (Func<List<string>>)delegates["GetGasGiantSkinList"];
            _getGasGiantRingSkinList = (Func<List<string>>)delegates["GetGasGiantRingSkinList"];
            _getOverlapGasGiantsAtPosition = (Func<Vector3D, List<MyPlanet>>)delegates["GetOverlapGasGiantsAtPosition"];
            _getAtmoGasGiantsAtPosition = (Func<Vector3D, List<MyPlanet>>)delegates["GetAtmoGasGiantsAtPosition"];
            _getShadowFactorCamera = (Func<float>)delegates["GetShadowFactorCamera"];
            _getShadowFactor = (Func<IMyEntity, float>)delegates["GetShadowFactor"];
            _getAtmoDensity = (Func<MyPlanet, Vector3D, float>)delegates["GetAtmoDensity"];
            _getAtmoDensityGlobal = (Func<Vector3D, float>)delegates["GetAtmoDensityGlobal"];
            _getRingInfluenceAtPosition = (Func<MyPlanet, Vector3D, float>)delegates["GetRingInfluenceAtPosition"];
            _getRingInfluenceAtPositionGlobal = (Func<Vector3D, float>)delegates["GetRingInfluenceAtPositionGlobal"];
            _getDayAtmoFactor = (Func<float>)delegates["GetDayAtmoFactor"];

            _forceMainUpdate = (Action)delegates["ForceMainUpdate"];
        }

        public MyPlanet SpawnGasGiant(Vector3D position, float radius, Vector3I planetColor, string planetSkin, float gravityStrength, float gravityFalloff, float dayLength) => _spawnGasGiant?.Invoke(position, radius, planetColor, planetSkin, gravityStrength, gravityFalloff, dayLength) ?? null;
        public bool SetGasGiantConfig_Name(MyPlanet planet, string name) => _setGasGiantConfig_name?.Invoke(planet, name) ?? false;
        public bool SetGasGiantConfig_BasicInfo(MyPlanet planet, float radius, Vector3I planetColor, string planetSkin, float gravityStrength, float gravityFalloff, float dayLength) => _setGasGiantConfig_basicInfo?.Invoke(planet, radius, planetColor, planetSkin, gravityStrength, gravityFalloff, dayLength) ?? false;
        public MyTuple<bool, float, Vector3I, string, float> GetGasGiantConfig_BasicInfo_Base(MyPlanet planet) => _getGasGiantConfig_basicInfo_base?.Invoke(planet) ?? new MyTuple<bool, float, Vector3I, string, float>();
        public MyTuple<bool, float, float> GetGasGiantConfig_BasicInfo_Gravity(MyPlanet planet) => _getGasGiantConfig_basicInfo_gravity?.Invoke(planet) ?? new MyTuple<bool, float, float>();
        public bool SetGasGiantConfig_AtmoInfo(MyPlanet planet, float airDensity, float oxygenDensity, float windSpeed) => _setGasGiantConfig_atmoInfo?.Invoke(planet, airDensity, oxygenDensity, windSpeed) ?? false;
        public MyTuple<bool, float, float, float> GetGasGiantConfig_AtmoInfo(MyPlanet planet) => _getGasGiantConfig_atmoInfo?.Invoke(planet) ?? new MyTuple<bool, float, float, float>();
        
        public bool SetGasGiantConfig_RingInfo(MyPlanet planet, int index, string ringSkin, Vector3D ringNormal, Vector3I ringColor, float ringLightMult, float ringShadowMult, float ringInnerScale, float ringOuterScale, float ringLayerSpacingScale, float ringRotationPeriod, bool constrainNearbyAsteroidsToRing, bool shadowOnRingEnabled) => _setGasGiantConfig_ringInfo?.Invoke(planet, index, ringSkin, ringNormal, ringColor, ringLightMult, ringShadowMult, ringInnerScale, ringOuterScale, ringLayerSpacingScale, ringRotationPeriod, constrainNearbyAsteroidsToRing, shadowOnRingEnabled) ?? false;
        public bool SetGasGiantConfig_RingInfo_Resource(MyPlanet planet, int index, bool collectRingResources, string collectResourceRingSubtypeId, float collectResourceRingAmount) => _setGasGiantConfig_ringInfo_resource?.Invoke(planet, index, collectRingResources, collectResourceRingSubtypeId, collectResourceRingAmount) ?? false;
        public bool SetGasGiantConfig_RingInfo_Enabled(MyPlanet planet, int index, bool enabledDraw, bool enabledParticle) => _setGasGiantConfig_ringInfo_enabled?.Invoke(planet, index, enabledDraw, enabledParticle) ?? false;
        public MyTuple<bool, bool, float, bool> GetGasGiantConfig_RingInfo_Base(MyPlanet planet, int index) => _getGasGiantConfig_ringInfo_base?.Invoke(planet, index) ?? new MyTuple<bool, bool, float, bool>();
        public MyTuple<bool, string, Vector3I, float, float, bool> GetGasGiantConfig_RingInfo_Visual(MyPlanet planet, int index) => _getGasGiantConfig_ringInfo_visual?.Invoke(planet, index) ?? new MyTuple<bool, string, Vector3I, float, float, bool>();
        public MyTuple<bool, Vector3D, float, float, float> GetGasGiantConfig_RingInfo_Size(MyPlanet planet, int index) => _getGasGiantConfig_ringInfo_size?.Invoke(planet, index) ?? new MyTuple<bool, Vector3D, float, float, float>();
        public MyTuple<bool, bool, string, float> GetGasGiantConfig_RingInfo_Resource(MyPlanet planet, int index) => _getGasGiantConfig_ringInfo_resource?.Invoke(planet, index) ?? new MyTuple<bool, bool, string, float>();
        public MyTuple<bool, bool, bool> GetGasGiantConfig_RingInfo_Enabled(MyPlanet planet, int index) => _getGasGiantConfig_ringInfo_enabled?.Invoke(planet, index) ?? new MyTuple<bool, bool, bool>();
        public bool RemoveGasGiantRing(MyPlanet planet, int index) => _removeGasGiantRing?.Invoke(planet, index) ?? false;

        public bool SetGasGiantConfig_InteriorInfo(MyPlanet planet, bool asteroidRemoval, bool pressureDamagePlayers, bool pressureDamageGrids) => _setGasGiantConfig_interiorInfo?.Invoke(planet, asteroidRemoval, pressureDamagePlayers, pressureDamageGrids) ?? false;
        public MyTuple<bool, bool, bool, bool> GetGasGiantConfig_InteriorInfo(MyPlanet planet) => _getGasGiantConfig_interiorInfo?.Invoke(planet) ?? new MyTuple<bool, bool, bool, bool>();
        public bool SetGasGiantConfig_ResourceInfo(MyPlanet planet, bool collectPlanetResources, string collectResourceUpperSubtypeId, float collectResourceUpperAmount, string collectResourceLowerSubtypeId, float collectResourceLowerAmount) => _setGasGiantConfig_resourceInfo?.Invoke(planet, collectPlanetResources, collectResourceUpperSubtypeId, collectResourceUpperAmount, collectResourceLowerSubtypeId, collectResourceLowerAmount) ?? false;
        public MyTuple<bool, bool, string, float, string, float> GetGasGiantConfig_ResourceInfo_Planet(MyPlanet planet) => _getGasGiantConfig_resourceInfo_planet?.Invoke(planet) ?? new MyTuple<bool, bool, string, float, string, float>();

        public bool SetPlanetConfig_RingInfo(MyPlanet planet, int index, string ringSkin, Vector3D ringNormal, Vector3I ringColor, float ringLightMult, float ringShadowMult, float ringInnerScale, float ringOuterScale, float ringLayerSpacingScale, float ringRotationPeriod, bool constrainNearbyAsteroidsToRing, bool shadowOnRingEnabled) => _setPlanetConfig_ringInfo?.Invoke(planet, index, ringSkin, ringNormal, ringColor, ringLightMult, ringShadowMult, ringInnerScale, ringOuterScale, ringLayerSpacingScale, ringRotationPeriod, constrainNearbyAsteroidsToRing, shadowOnRingEnabled) ?? false;
        public bool SetPlanetConfig_RingInfo_Resource(MyPlanet planet, int index, bool collectRingResources, string collectResourceRingSubtypeId, float collectResourceRingAmount) => _setPlanetConfig_ringInfo_resource?.Invoke(planet, index, collectRingResources, collectResourceRingSubtypeId, collectResourceRingAmount) ?? false;
        public bool SetPlanetConfig_RingInfo_Enabled(MyPlanet planet, int index, bool enabledDraw, bool enabledParticle) => _setPlanetConfig_ringInfo_enabled?.Invoke(planet, index, enabledDraw, enabledParticle) ?? false;
        public MyTuple<bool, bool, float, bool> GetPlanetConfig_RingInfo_Base(MyPlanet planet, int index) => _getPlanetConfig_ringInfo_base?.Invoke(planet, index) ?? new MyTuple<bool, bool, float, bool>();
        public MyTuple<bool, string, Vector3I, float, float, bool> GetPlanetConfig_RingInfo_Visual(MyPlanet planet, int index) => _getPlanetConfig_ringInfo_visual?.Invoke(planet, index) ?? new MyTuple<bool, string, Vector3I, float, float, bool>();
        public MyTuple<bool, Vector3D, float, float, float> GetPlanetConfig_RingInfo_Size(MyPlanet planet, int index) => _getPlanetConfig_ringInfo_size?.Invoke(planet, index) ?? new MyTuple<bool, Vector3D, float, float, float>();
        public MyTuple<bool, bool, string, float> GetPlanetConfig_RingInfo_Resource(MyPlanet planet, int index) => _getPlanetConfig_ringInfo_resource?.Invoke(planet, index) ?? new MyTuple<bool, bool, string, float>();
        public MyTuple<bool, bool, bool> GetPlanetConfig_RingInfo_Enabled(MyPlanet planet, int index) => _getPlanetConfig_ringInfo_enabled?.Invoke(planet, index) ?? new MyTuple<bool, bool, bool>();
        public bool RemovePlanetRing(MyPlanet planet, int index) => _removePlanetRing?.Invoke(planet, index) ?? false;

        public List<string> GetGasGiantSkinList() => _getGasGiantSkinList?.Invoke() ?? new List<string>();
        public List<string> GetGasGiantRingSkinList() => _getGasGiantRingSkinList?.Invoke() ?? new List<string>();
        public List<MyPlanet> GetOverlapGasGiantsAtPosition(Vector3D position) => _getOverlapGasGiantsAtPosition?.Invoke(position) ?? new List<MyPlanet>();
        public List<MyPlanet> GetAtmoGasGiantsAtPosition(Vector3D position) => _getAtmoGasGiantsAtPosition?.Invoke(position) ?? new List<MyPlanet>();
        public float GetShadowFactorCamera() => _getShadowFactorCamera?.Invoke() ?? 0f;
        public float GetShadowFactor(IMyEntity entity) => _getShadowFactor?.Invoke(entity) ?? 0f;
        public float GetAtmoDensity(MyPlanet planet, Vector3D position) => _getAtmoDensity?.Invoke(planet, position) ?? 0f;
        public float GetAtmoDensityGlobal(Vector3D position) => _getAtmoDensityGlobal?.Invoke(position) ?? 0f;
        public float GetRingInfluenceAtPosition(MyPlanet planet, Vector3D position) => _getRingInfluenceAtPosition?.Invoke(planet, position) ?? 0f;
        public float GetRingInfluenceAtPositionGlobal(Vector3D position) => _getRingInfluenceAtPositionGlobal?.Invoke(position) ?? 0f;
        public float GetDayAtmoFactor() => _getDayAtmoFactor?.Invoke() ?? 0f;
        public void ForceMainUpdate() => _forceMainUpdate?.Invoke();
    }
}
