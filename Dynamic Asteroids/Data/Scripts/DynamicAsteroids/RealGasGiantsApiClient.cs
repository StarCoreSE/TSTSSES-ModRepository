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

        private Func<Vector3D, List<MyPlanet>> _getAtmoGasGiantsAtPosition;
        private Func<MyPlanet, MyTuple<bool, float, Vector3I, string, float>> _getGasGiantConfig_basicInfo_base;
        private Func<MyPlanet, int, MyTuple<bool, Vector3D, float, float, float>> _getGasGiantConfig_ringInfo_size;
        private Func<Vector3D, float> _getRingInfluenceAtPositionGlobal;

        private const long Channel = 321421229679;

        public bool IsReady { get; private set; }

        private void HandleMessage(object o)
        {
            if (_apiInit) return;
            var dict = o as IReadOnlyDictionary<string, Delegate>;
            var message = o as string;

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
            if (!_isRegistered) return;
            _isRegistered = false;
            MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
            IsReady = false;
        }

        public void ApiLoad(IReadOnlyDictionary<string, Delegate> delegates)
        {
            _apiInit = true;
            _getAtmoGasGiantsAtPosition = (Func<Vector3D, List<MyPlanet>>)delegates["GetAtmoGasGiantsAtPosition"];
            _getGasGiantConfig_basicInfo_base = (Func<MyPlanet, MyTuple<bool, float, Vector3I, string, float>>)delegates["GetGasGiantConfig_BasicInfo_Base"];
            _getGasGiantConfig_ringInfo_size = (Func<MyPlanet, int, MyTuple<bool, Vector3D, float, float, float>>)delegates["GetGasGiantConfig_RingInfo_Size"];
            _getRingInfluenceAtPositionGlobal = (Func<Vector3D, float>)delegates["GetRingInfluenceAtPositionGlobal"];
        }

        public List<MyPlanet> GetAtmoGasGiantsAtPosition(Vector3D position) => _getAtmoGasGiantsAtPosition?.Invoke(position) ?? new List<MyPlanet>();
        public MyTuple<bool, float, Vector3I, string, float> GetGasGiantConfig_BasicInfo_Base(MyPlanet planet) => _getGasGiantConfig_basicInfo_base?.Invoke(planet) ?? new MyTuple<bool, float, Vector3I, string, float>();
        public MyTuple<bool, Vector3D, float, float, float> GetGasGiantConfig_RingInfo_Size(MyPlanet planet, int ringId = 0) => _getGasGiantConfig_ringInfo_size?.Invoke(planet, ringId) ?? new MyTuple<bool, Vector3D, float, float, float>();
        public float GetRingInfluenceAtPositionGlobal(Vector3D position) => _getRingInfluenceAtPositionGlobal?.Invoke(position) ?? 0f;
    }
}
