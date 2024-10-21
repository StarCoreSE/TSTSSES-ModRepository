using Sandbox.ModAPI;
using System;
using VRage.Game.Components;
using VRage.Input;
using VRageMath;
using ProtoBuf;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Game;
using System.Collections.Generic;
using VRage.ModAPI;
using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using RealGasGiants;
using VRage.Utils;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class MainSession : MySessionComponentBase
    {
        public static MainSession I;
        public Random Rand;
        private int seed;
        public AsteroidSpawner _spawner;
        private int _saveStateTimer;
        private int _networkMessageTimer;
        public RealGasGiantsApi RealGasGiantsApi { get; private set; }
        private int _testTimer = 0;

        public override void LoadData()
        {
            I = this;
            Log.Init();
            Log.Info("Log initialized in LoadData method.");

            AsteroidSettings.LoadSettings();

            seed = AsteroidSettings.Seed;
            Rand = new Random(seed);

            // Initialize RealGasGiantsApi
            RealGasGiantsApi = new RealGasGiantsApi();
            RealGasGiantsApi.Load();
            Log.Info("RealGasGiants API loaded in LoadData");

            if (MyAPIGateway.Session.IsServer)
            {
                _spawner = new AsteroidSpawner(RealGasGiantsApi);
                _spawner.Init(seed);
                if (AsteroidSettings.EnablePersistence)
                {
                    _spawner.LoadAsteroidState();
                }
            }

            MyAPIGateway.Multiplayer.RegisterMessageHandler(32000, OnMessageReceived);
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }

        public override void BeforeStart()
        {
            // Simple IsReady check
            Log.Info($"RealGasGiants API IsReady: {RealGasGiantsApi.IsReady}");
            //MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1000, DamageHandler);

        }

        //private void DamageHandler(object target, ref MyDamageInformation info)
        //{
        //    // Apply damage if the target is an AsteroidEntity
        //    var asteroid = target as AsteroidEntity;
        //    if (asteroid != null)
        //    {
        //        // Check if this asteroid is managed by the current session's spawner (important to avoid unintended damage)
        //        if (_spawner._asteroids.Contains(asteroid))
        //        {
        //            Log.Info($"Applying {info.Amount} damage to Asteroid ID {asteroid.EntityId}");
        //
        //            // Apply the damage by reducing integrity
        //            asteroid.ReduceIntegrity(info.Amount);
        //        }
        //    }
        //}

        protected override void UnloadData()
        {
            try
            {
                Log.Info("Unloading data in MainSession");
                if (MyAPIGateway.Session.IsServer)
                {
                    if (AsteroidSettings.EnablePersistence)
                    {
                        _spawner.SaveAsteroidState();
                    }
                    _spawner.Close();
                }

                AsteroidSettings.SaveSettings();

                MyAPIGateway.Multiplayer.UnregisterMessageHandler(32000, OnMessageReceived);
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

                RealGasGiantsApi?.Unload();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error in UnloadData: ");
            }

            Log.Close();
            I = null;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null || !IsPlayerAdmin(player)) return;

            if (messageText.StartsWith("/dynamicasteroids") || messageText.StartsWith("/dn"))
            {
                var args = messageText.Split(' ');
                if (args.Length > 1)
                {
                    switch (args[1].ToLower())
                    {
                        case "createspawnarea":
                            double radius;
                            if (args.Length == 3 && double.TryParse(args[2], out radius))
                            {
                                CreateSpawnArea(radius);
                                sendToOthers = false;
                            }
                            break;

                        case "removespawnarea":
                            if (args.Length == 3)
                            {
                                RemoveSpawnArea(args[2]);
                                sendToOthers = false;
                            }
                            break;
                    }
                }
            }
        }

        private bool IsPlayerAdmin(IMyPlayer player)
        {
            return MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Session.IsUserAdmin(player.SteamUserId);
        }

        private void CreateSpawnArea(double radius)
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return;

            var position = player.GetPosition();
            var name = $"Area_{position.GetHashCode()}";

            BoundingBoxD boundingBox = new BoundingBoxD(position - new Vector3D(radius), position + new Vector3D(radius));
            MyPlanet closestPlanet = MyGamePruningStructure.GetClosestPlanet(ref boundingBox);

            if (closestPlanet != null)
            {
                Log.Info($"Cannot create spawn area '{name}' at {position} with radius {radius}: Intersects with a planet.");
                MyAPIGateway.Utilities.ShowMessage("DynamicAsteroids", $"Cannot create spawn area '{name}' at {position} with radius {radius}: Intersects with a planet.");
                return;
            }

            AsteroidSettings.AddSpawnableArea(name, position, radius);
            Log.Info($"Created spawn area '{name}' at {position} with radius {radius}");
            MyAPIGateway.Utilities.ShowMessage("DynamicAsteroids", $"Created spawn area '{name}' at {position} with radius {radius}");
        }



        private void RemoveSpawnArea(string name)
        {
            AsteroidSettings.RemoveSpawnableArea(name);
            Log.Info($"Removed spawn area '{name}'");
            MyAPIGateway.Utilities.ShowMessage("DynamicAsteroids", $"Removed spawn area '{name}'");
        }

        private int _logInterval = 0;

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (MyAPIGateway.Session.IsServer)
                {
                    if (_spawner != null)
                    {
                        _spawner.UpdateTick();
                        if (_saveStateTimer > 0)
                        {
                            _saveStateTimer--;
                        }
                        else
                        {
                            _spawner.SaveAsteroidState();
                            _saveStateTimer = AsteroidSettings.SaveStateInterval;
                        }

                        if (_networkMessageTimer > 0)
                        {
                            _networkMessageTimer--;
                        }
                        else
                        {
                            _spawner.SendNetworkMessages();
                            _networkMessageTimer = AsteroidSettings.NetworkMessageInterval;
                        }
                    }
                }

                if (MyAPIGateway.Session?.Player?.Character != null && _spawner != null)
                {
                    if (_spawner._asteroids != null)
                    {
                        Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                        AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);
                        if (nearestAsteroid != null)
                        {
                            Vector3D angularVelocity = nearestAsteroid.Physics.AngularVelocity;
                            string rotationString = $"({angularVelocity.X:F2}, {angularVelocity.Y:F2}, {angularVelocity.Z:F2})";
                            string message = $"Nearest Asteroid: {nearestAsteroid.EntityId} ({nearestAsteroid.Type})\nRotation: {rotationString}";
                            if (AsteroidSettings.EnableLogging && _logInterval % 300 == 0)
                            {
                                MyAPIGateway.Utilities.ShowNotification(message, 1000 / 60);
                                Log.Info(message);
                            }
                        }
                    }
                }

                if (AsteroidSettings.EnableMiddleMouseAsteroidSpawn && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.MiddleButton))
                {
                    if (MyAPIGateway.Session != null)
                    {
                        var position = MyAPIGateway.Session.Player?.GetPosition() ?? Vector3D.Zero;
                        var velocity = MyAPIGateway.Session.Player?.Character?.Physics?.LinearVelocity ?? Vector3D.Zero;
                        AsteroidType type = DetermineAsteroidType();
                        AsteroidEntity.CreateAsteroid(position, Rand.Next(50), velocity, type);
                        if (_logInterval % 300 == 0)
                        {
                            Log.Info($"Asteroid created at {position} with velocity {velocity}");
                        }
                    }
                }

                if (++_testTimer >= 240)
                {
                    _testTimer = 0;
                    TestNearestGasGiant();
                }

                //if (++_logInterval >= 300)
                //{
                //    _logInterval = 0;
                //    if (_spawner != null)
                //    {
                //        Log.Info($"Client: _spawner._asteroids is {(_spawner._asteroids == null ? "null" : $"not null (count: {_spawner._asteroids.Count})")}");
                //    }
                //    else
                //    {
                //        Log.Info("Client: _spawner is null");
                //    }
                //    Log.Info($"Client: MyAPIGateway.Session?.Player?.Character is {(MyAPIGateway.Session?.Player?.Character == null ? "null" : "not null")}");
                //}
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error in UpdateAfterSimulation: ");
            }
        }

        private void TestNearestGasGiant()
        {
            if (RealGasGiantsApi == null || !RealGasGiantsApi.IsReady || MyAPIGateway.Session?.Player == null)
                return;

            if (!AsteroidSettings.EnableLogging)
                return;


            Vector3D playerPosition = MyAPIGateway.Session.Player.GetPosition();
            MyPlanet nearestGasGiant = FindNearestGasGiant(playerPosition);

            // Get the global ring influence at the player's position
            float ringInfluence = RealGasGiantsApi.GetRingInfluenceAtPositionGlobal(playerPosition);

            string message;

            if (nearestGasGiant != null)
            {
                var basicInfo = RealGasGiantsApi.GetGasGiantConfig_BasicInfo_Base(nearestGasGiant);
                if (basicInfo.Item1) // If operation was successful
                {
                    double distance = Vector3D.Distance(playerPosition, nearestGasGiant.PositionComp.GetPosition()) - basicInfo.Item2;
                    message = $"Nearest Gas Giant:\n" +
                              $"Distance: {distance:N0}m\n" +
                              $"Radius: {basicInfo.Item2:N0}m\n" +
                              $"Color: {basicInfo.Item3}\n" +
                              $"Skin: {basicInfo.Item4}\n" +
                              $"Day Length: {basicInfo.Item5:F2}s\n" +
                              $"Current Ring Influence: {ringInfluence:F3}";
                }
                else
                {
                    message = "Failed to get gas giant info";
                }
            }
            else
            {
                message = $"Current Ring Influence: {ringInfluence:F3}";
            }

            MyAPIGateway.Utilities.ShowNotification(message, 4000, "White");
            //Log.Info(message); // Also log the message for easier debugging
        }



        private MyPlanet FindNearestGasGiant(Vector3D position)
        {
            const double searchRadius = 1000000000; // 1 million km in meters
            MyPlanet nearestGasGiant = null;
            double nearestDistance = double.MaxValue;

            // Get all gas giants within the larger search sphere
            var gasGiants = RealGasGiantsApi.GetAtmoGasGiantsAtPosition(position);

            foreach (var gasGiant in gasGiants)
            {
                var basicInfo = RealGasGiantsApi.GetGasGiantConfig_BasicInfo_Base(gasGiant);
                if (!basicInfo.Item1) continue; // Skip if we couldn't get the info

                float gasGiantRadius = basicInfo.Item2;
                Vector3D gasGiantCenter = gasGiant.PositionComp.GetPosition();

                // Calculate distance from player to the surface of the gas giant
                double distance = Vector3D.Distance(position, gasGiantCenter) - gasGiantRadius;

                if (distance < nearestDistance && distance <= searchRadius)
                {
                    nearestDistance = distance;
                    nearestGasGiant = gasGiant;
                }
            }

            if (nearestGasGiant != null)
            {
                //Log.Info($"Found nearest gas giant at distance: {nearestDistance:N0} meters");
            }
            else
            {
                //Log.Info("No gas giants found within 1 million km");
            }

            return nearestGasGiant;
        }

        private void OnMessageReceived(byte[] message)
        {
            try
            {
                if (message == null || message.Length == 0)
                {
                    Log.Info("Received empty or null message, skipping processing.");
                    return;
                }

                Log.Info($"Client: Received message of {message.Length} bytes");
                var asteroidMessage = MyAPIGateway.Utilities.SerializeFromBinary<AsteroidNetworkMessage>(message);

                if (asteroidMessage.IsRemoval)
                {
                    var asteroid = MyEntities.GetEntityById(asteroidMessage.EntityId) as AsteroidEntity;
                    if (asteroid != null)
                    {
                        //Log.Info($"Client: Removing asteroid with ID {asteroidMessage.EntityId}");
                        MyEntities.Remove(asteroid);
                        asteroid.Close();
                        //Log.Info($"Client: Removed asteroid with ID {asteroidMessage.EntityId}");
                    }
                    else
                    {
                        Log.Info($"Client: Failed to find asteroid with ID {asteroidMessage.EntityId} for removal");
                    }
                }
                else
                {
                    var existingAsteroid = MyEntities.GetEntityById(asteroidMessage.EntityId) as AsteroidEntity;
                    if (existingAsteroid != null)
                    {
                        Log.Info($"Client: Asteroid with ID {asteroidMessage.EntityId} already exists, skipping creation");
                    }
                    else
                    {
                        //Log.Info($"Client: Creating asteroid with provided details");
                        var asteroid = AsteroidEntity.CreateAsteroid(
                            asteroidMessage.GetPosition(),
                            asteroidMessage.Size,
                            asteroidMessage.GetVelocity(),
                            asteroidMessage.GetType(),
                            asteroidMessage.GetRotation(),
                            asteroidMessage.EntityId);

                        if (asteroid != null)
                        {
                            asteroid.Physics.AngularVelocity = asteroidMessage.GetAngularVelocity();
                            MyEntities.Add(asteroid);
                            //Log.Info($"Client: Created asteroid with ID {asteroid.EntityId}");
                        }
                        else
                        {
                            Log.Info($"Client: Failed to create asteroid with ID {asteroidMessage.EntityId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error processing received message: ");
            }
        }

        private AsteroidEntity FindNearestAsteroid(Vector3D characterPosition)
        {
            if (_spawner._asteroids == null) return null;

            AsteroidEntity nearestAsteroid = null;
            double minDistance = double.MaxValue;
            foreach (var asteroid in _spawner._asteroids)
            {
                double distance = Vector3D.DistanceSquared(characterPosition, asteroid.PositionComp.GetPosition());
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestAsteroid = asteroid;
                }
            }
            return nearestAsteroid;
        }

        private AsteroidType DetermineAsteroidType()
        {
            int randValue = Rand.Next(0, 2);
            return (AsteroidType)randValue;
        }
    }
}
