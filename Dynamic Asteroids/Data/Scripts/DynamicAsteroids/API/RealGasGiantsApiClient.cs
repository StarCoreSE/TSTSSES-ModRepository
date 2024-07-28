using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VRageMath;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids.API
{
    public class RealGasGiantsApi
    {
        private bool _apiInit;

        private Func<Vector3D, List<MyPlanet>> _getOverlapGasGiantsAtPosition;
        private Func<Vector3D, List<MyPlanet>> _getAtmoGasGiantsAtPosition;
        private Func<Vector3D, float> _getShadowFactor;
        private Func<MyPlanet, Vector3D, float> _getAtmoDensity;
        private Func<Vector3D, float> _getAtmoDensityGlobal;

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

            _getOverlapGasGiantsAtPosition = (Func<Vector3D, List<MyPlanet>>)delegates["GetOverlapGasGiantsAtPosition"];
            _getAtmoGasGiantsAtPosition = (Func<Vector3D, List<MyPlanet>>)delegates["GetAtmoGasGiantsAtPosition"];
            _getShadowFactor = (Func<Vector3D, float>)delegates["GetShadowFactor"];
            _getAtmoDensity = (Func<MyPlanet, Vector3D, float>)delegates["GetAtmoDensity"];
            _getAtmoDensityGlobal = (Func<Vector3D, float>)delegates["GetAtmoDensityGlobal"];
        }


        public List<MyPlanet> GetOverlapGasGiantsAtPosition(Vector3D position) => _getOverlapGasGiantsAtPosition?.Invoke(position) ?? new List<MyPlanet>();
        public List<MyPlanet> GetAtmoGasGiantsAtPosition(Vector3D position) => _getAtmoGasGiantsAtPosition?.Invoke(position) ?? new List<MyPlanet>();
        public float GetShadowFactor(Vector3D position) => _getShadowFactor?.Invoke(position) ?? 0f;
        public float GetAtmoDensity(MyPlanet planet, Vector3D position) => _getAtmoDensity?.Invoke(planet, position) ?? 0f;
        public float GetAtmoDensityGlobal(Vector3D position) => _getAtmoDensityGlobal?.Invoke(position) ?? 0f;
    }
}
