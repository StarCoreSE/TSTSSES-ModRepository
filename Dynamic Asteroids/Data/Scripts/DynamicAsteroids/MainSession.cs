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
using System.Linq;
using VRage.Utils;


namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class MainSession : MySessionComponentBase {
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

            
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(32000, OnSecureMessageReceived);
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

                // Cleanup asteroids
                if (_spawner != null)
                {
                    // Remove all asteroid entities
                    if (MyAPIGateway.Session.IsServer)
                    {
                        // Save state before closing
                        if (AsteroidSettings.EnablePersistence)
                        {
                            _spawner.SaveAsteroidState();
                        }

                        // Remove all asteroids from the world
                        var asteroidsToRemove = _spawner.GetAsteroids().ToList();
                        foreach (var asteroid in asteroidsToRemove)
                        {
                            try
                            {
                                // Ensure entity is removed from the world
                                MyEntities.Remove(asteroid);
                                asteroid.Close();
                            }
                            catch (Exception removeEx)
                            {
                                Log.Exception(removeEx, typeof(MainSession), "Error removing asteroid during unload");
                            }
                        }

                        // Close the spawner
                        _spawner.Close();
                        _spawner = null;
                    }
                }

                // Unregister message handlers
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(32000, OnSecureMessageReceived);
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

                // Cleanup Real Gas Giants API
                if (RealGasGiantsApi != null)
                {
                    RealGasGiantsApi.Unload();
                    RealGasGiantsApi = null;
                }

                // Save settings
                AsteroidSettings.SaveSettings();

                // Close logging
                Log.Close();

                // Clear static references
                I = null;
            }
            catch (Exception ex)
            {
                // Log any unloading errors
                MyLog.Default.WriteLine($"Error in UnloadData: {ex}");

                // Ensure logging happens even if other cleanup fails
                try
                {
                    Log.Exception(ex, typeof(MainSession), "Error in UnloadData");
                }
                catch { }
            }
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            IMyPlayer player = MyAPIGateway.Session.Player;
            if (player == null || !IsPlayerAdmin(player)) return;

            if (!messageText.StartsWith("/dynamicasteroids") && !messageText.StartsWith("/dn")) return;
            var args = messageText.Split(' ');
            if (args.Length <= 1) return;
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

        private bool IsPlayerAdmin(IMyPlayer player)
        {
            return MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Session.IsUserAdmin(player.SteamUserId);
        }

        private void CreateSpawnArea(double radius)
        {
            IMyPlayer player = MyAPIGateway.Session.Player;
            if (player == null) return;

            Vector3D position = player.GetPosition();
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

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (MyAPIGateway.Session.IsServer)
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

                // Modified this section to use GetAsteroids()
                if (MyAPIGateway.Session?.Player?.Character != null && _spawner != null && _spawner.GetAsteroids().Any())
                {
                    Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                    AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);
                    if (nearestAsteroid != null)
                    {
                        Vector3D angularVelocity = nearestAsteroid.Physics.AngularVelocity;
                        string rotationString = $"({angularVelocity.X:F2}, {angularVelocity.Y:F2}, {angularVelocity.Z:F2})";
                        string message = $"Nearest Asteroid: {nearestAsteroid.EntityId} ({nearestAsteroid.Type})\nRotation: {rotationString}";
                        if (AsteroidSettings.EnableLogging) MyAPIGateway.Utilities.ShowNotification(message, 1000 / 60);
                    }
                }

                if (AsteroidSettings.EnableMiddleMouseAsteroidSpawn && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.MiddleButton))
                {
                    if (MyAPIGateway.Session != null)
                    {
                        Vector3D position = MyAPIGateway.Session.Player?.GetPosition() ?? Vector3D.Zero;
                        Vector3 velocity = MyAPIGateway.Session.Player?.Character?.Physics?.LinearVelocity ?? Vector3D.Zero;
                        AsteroidType type = DetermineAsteroidType();
                        AsteroidEntity.CreateAsteroid(position, Rand.Next(50), velocity, type);
                        Log.Info($"Asteroid created at {position} with velocity {velocity}");
                    }
                }

                if (++_testTimer < 240) return;
                _testTimer = 0;
                TestNearestGasGiant();
                Log.Update();
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

            if (AsteroidSettings.EnableLogging)
                MyAPIGateway.Utilities.ShowNotification(message, 4000, "White");
        }

        private MyPlanet FindNearestGasGiant(Vector3D position)
        {
            const double searchRadius = 1000000000;// 1 million km in meters
            MyPlanet nearestGasGiant = null;
            double nearestDistance = double.MaxValue;

            // Get all gas giants within the larger search sphere
            var gasGiants = RealGasGiantsApi.GetAtmoGasGiantsAtPosition(position);

            foreach (MyPlanet gasGiant in gasGiants)
            {
                var basicInfo = RealGasGiantsApi.GetGasGiantConfig_BasicInfo_Base(gasGiant);
                if (!basicInfo.Item1) continue;// Skip if we couldn't get the info

                float gasGiantRadius = basicInfo.Item2;
                Vector3D gasGiantCenter = gasGiant.PositionComp.GetPosition();

                // Calculate distance from player to the surface of the gas giant
                double distance = Vector3D.Distance(position, gasGiantCenter) - gasGiantRadius;

                if (!(distance < nearestDistance) || !(distance <= searchRadius)) continue;
                nearestDistance = distance;
                nearestGasGiant = gasGiant;
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

        private void OnSecureMessageReceived(ushort handlerId, byte[] message, ulong steamId, bool isFromServer)
        {
            try
            {
                if (message == null || message.Length == 0)
                {
                    Log.Info("Received empty or null message, skipping processing.");
                    return;
                }

                Log.Info($"Client: Received message of {message.Length} bytes from {steamId}");
                AsteroidNetworkMessage asteroidMessage = MyAPIGateway.Utilities.SerializeFromBinary<AsteroidNetworkMessage>(message);

                if (asteroidMessage.IsRemoval)
                {
                    AsteroidEntity asteroid = MyEntities.GetEntityById(asteroidMessage.EntityId) as AsteroidEntity;
                    if (asteroid != null)
                    {
                        MyEntities.Remove(asteroid);
                        asteroid.Close();
                    }
                    else
                    {
                        Log.Info($"Client: Failed to find asteroid with ID {asteroidMessage.EntityId} for removal");
                    }
                }
                else
                {
                    AsteroidEntity existingAsteroid = MyEntities.GetEntityById(asteroidMessage.EntityId) as AsteroidEntity;
                    if (existingAsteroid != null)
                    {
                        Log.Info($"Client: Asteroid with ID {asteroidMessage.EntityId} already exists, skipping creation");
                    }
                    else
                    {
                        AsteroidEntity asteroid = AsteroidEntity.CreateAsteroid(
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
            if (_spawner == null || !_spawner.GetAsteroids().Any()) return null;

            AsteroidEntity nearestAsteroid = null;
            double minDistance = double.MaxValue;
            foreach (AsteroidEntity asteroid in _spawner.GetAsteroids())
            {
                double distance = Vector3D.DistanceSquared(characterPosition, asteroid.PositionComp.GetPosition());
                if (!(distance < minDistance)) continue;
                minDistance = distance;
                nearestAsteroid = asteroid;
            }
            return nearestAsteroid;
        }

        public override void Draw()
        {
            try
            {
                if (MyAPIGateway.Session?.Player?.Character != null && _spawner != null)
                {
                    Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                    AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);

                    if (nearestAsteroid != null)
                    {
                        nearestAsteroid.DrawDebugSphere();  // Draw the sphere every frame
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error in Draw: ");
            }
        }
           
        private AsteroidType DetermineAsteroidType()
        {
            int randValue = Rand.Next(0, 2);
            return (AsteroidType)randValue;
        }
    }
}