﻿using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using RealGasGiants;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;


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
        private KeenRicochetMissileBSWorkaroundHandler _missileHandler;
        private Dictionary<long, Vector3D> _serverPositions = new Dictionary<long, Vector3D>();
        private Dictionary<long, Quaternion> _serverRotations = new Dictionary<long, Quaternion>();


        public override void LoadData()
        {
            I = this;
            Log.Init();
            Log.Info("Log initialized in LoadData method.");
            AsteroidSettings.LoadSettings();
            seed = AsteroidSettings.Seed;
            Rand = new Random(seed);

            // Load RealGasGiants API
            RealGasGiantsApi = new RealGasGiantsApi();
            RealGasGiantsApi.Load();
            Log.Info("RealGasGiants API loaded in LoadData");

            // Initialize damage handler
            AsteroidDamageHandler damageHandler = new AsteroidDamageHandler();
            _missileHandler = new KeenRicochetMissileBSWorkaroundHandler(damageHandler);

            // Server-only initialization
            if (MyAPIGateway.Session.IsServer)
            {
                _spawner = new AsteroidSpawner(RealGasGiantsApi);
                _spawner.Init(seed);
            }

            // Register network handlers for both client and server
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
                if (_spawner != null)
                {
                    if (MyAPIGateway.Session.IsServer)
                    {
                        // Remove SaveAsteroidState call
                        var asteroidsToRemove = _spawner.GetAsteroids().ToList();
                        foreach (var asteroid in asteroidsToRemove)
                        {
                            try
                            {
                                MyEntities.Remove(asteroid);
                                asteroid.Close();
                            }
                            catch (Exception removeEx)
                            {
                                Log.Exception(removeEx, typeof(MainSession), "Error removing asteroid during unload");
                            }
                        }
                        _spawner.Close();
                        _spawner = null;
                    }
                }

                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(32000, OnSecureMessageReceived);
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

                if (RealGasGiantsApi != null)
                {
                    RealGasGiantsApi.Unload();
                    RealGasGiantsApi = null;
                }

                _missileHandler.Unload();
                AsteroidSettings.SaveSettings();
                Log.Close();
                I = null;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"Error in UnloadData: {ex}");
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
                // Server-only updates
                if (MyAPIGateway.Session.IsServer)
                {
                    _spawner?.UpdateTick();

                    List<IMyPlayer> players = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(players);

                    foreach (IMyPlayer player in players)
                    {
                        Vector3D playerPosition = player.GetPosition();
                        AsteroidEntity nearestAsteroid = FindNearestAsteroid(playerPosition);

                        if (nearestAsteroid != null)
                        {
                            //Log.Info($"Server: Nearest asteroid to player at {playerPosition}: Asteroid ID: {nearestAsteroid.EntityId}, Position: {nearestAsteroid.PositionComp.GetPosition()}");
                        }
                    }

                    if (_saveStateTimer > 0)
                        _saveStateTimer--;

                    if (_networkMessageTimer > 0)
                        _networkMessageTimer--;
                    else
                    {
                        _spawner?.SendNetworkMessages();
                        _networkMessageTimer = AsteroidSettings.NetworkMessageInterval;
                        Log.Info("Server: Sending network messages to clients.");
                    }
                }

                // Middle mouse spawn (handle for both client and server)
                if (AsteroidSettings.EnableMiddleMouseAsteroidSpawn &&
                    MyAPIGateway.Input.IsNewKeyPressed(MyKeys.MiddleButton) &&
                    MyAPIGateway.Session?.Player != null)
                {
                    Vector3D position = MyAPIGateway.Session.Player.GetPosition();
                    Vector3D velocity = MyAPIGateway.Session.Player.Character?.Physics?.LinearVelocity ?? Vector3D.Zero;
                    AsteroidType type = DetermineAsteroidType();

                    if (MyAPIGateway.Session.IsServer)
                    {
                        // Server creates the asteroid directly
                        var asteroid = AsteroidEntity.CreateAsteroid(position, Rand.Next(50), velocity, type);
                        if (asteroid != null && _spawner != null)
                        {
                            _spawner.AddAsteroid(asteroid);

                            // Send to clients
                            var message = new AsteroidNetworkMessage(
                                position,
                                asteroid.Properties.Diameter,
                                velocity,
                                asteroid.Physics.AngularVelocity,
                                type,
                                false,
                                asteroid.EntityId,
                                false,
                                true,
                                Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix)
                            );

                            byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                            MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                        }
                    }
                    else
                    {
                        // Client sends request to server
                        var request = new AsteroidNetworkMessage(
                            position,
                            50, // default size
                            velocity,
                            Vector3D.Zero,
                            type,
                            false,
                            0, // server will assign real ID
                            false,
                            true,
                            Quaternion.Identity
                        );

                        byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(request);
                        MyAPIGateway.Multiplayer.SendMessageToServer(32000, messageBytes);
                    }

                    Log.Info($"Asteroid spawn requested at {position} with velocity {velocity}");
                }

                // Client-only updates (debug visualization)
                if (MyAPIGateway.Session?.Player?.Character != null)
                {
                    Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                    AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);

                    if (nearestAsteroid != null && AsteroidSettings.EnableLogging)
                    {
                        Vector3D angularVelocity = nearestAsteroid.Physics.AngularVelocity;
                        string rotationString = $"({angularVelocity.X:F2}, {angularVelocity.Y:F2}, {angularVelocity.Z:F2})";
                        string message = $"Nearest Asteroid: {nearestAsteroid.EntityId} ({nearestAsteroid.Type})\nRotation: {rotationString}";
                        MyAPIGateway.Utilities.ShowNotification(message, 1000 / 60);
                        nearestAsteroid.DrawDebugSphere(); // Debug visualization
                    }
                }

                // Shared updates
                if (++_testTimer >= 240)
                {
                    _testTimer = 0;
                    TestNearestGasGiant();
                }

                Log.Update();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error in UpdateAfterSimulation: ");
            }
        }

        public void DebugMissiles()
        {
            if (!AsteroidSettings.EnableLogging)
                return;

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            int missileCount = 0;
            foreach (var entity in entities)
            {
                IMyMissile missile = entity as IMyMissile;
                if (missile != null)
                {
                    var ammoDef = missile.AmmoDefinition as MyMissileAmmoDefinition;
                    if (ammoDef != null)
                    {
                        MyAPIGateway.Utilities.ShowNotification(
                            $"Missile detected:\n" +
                            $"Type: {ammoDef.Id.SubtypeName}\n", 1000 / 60);
                    }
                }
            }

            if (missileCount == 0)
            {
                MyAPIGateway.Utilities.ShowNotification("No missiles found in world", 1000 / 60);
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

                // Try to deserialize as container first
                try
                {
                    var container = MyAPIGateway.Utilities.SerializeFromBinary<AsteroidNetworkMessageContainer>(message);
                    if (container != null && container.Messages != null)
                    {
                        Log.Info($"Received batch update with {container.Messages.Length} asteroids");
                        foreach (var asteroidMessage in container.Messages)
                        {
                            ProcessClientMessage(asteroidMessage);
                        }
                        return;
                    }
                }
                catch
                {
                    // If container deserialization fails, try individual message
                    var asteroidMessage = MyAPIGateway.Utilities.SerializeFromBinary<AsteroidNetworkMessage>(message);
                    if (!MyAPIGateway.Session.IsServer)
                    {
                        ProcessClientMessage(asteroidMessage);
                    }
                    else
                    {
                        ProcessServerMessage(asteroidMessage, steamId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), $"Error processing received message");
            }
        }

        public class NetworkMessageVerification
        {
            public static bool ValidateMessage(AsteroidNetworkMessage message)
            {
                // In C# 6, we need to do explicit null check
                if (ReferenceEquals(message, null))
                    return false;

                if (message.EntityId == 0 && !message.IsInitialCreation)
                    return false;

                if (double.IsNaN(message.PosX) || double.IsNaN(message.PosY) || double.IsNaN(message.PosZ))
                    return false;

                return true;
            }
        }


        private void ProcessServerMessage(AsteroidNetworkMessage message, ulong steamId)
        {
            if (!NetworkMessageVerification.ValidateMessage(message))
            {
                Log.Warning($"Server received invalid message from client {steamId}");
                return;
            }

            if (message.IsInitialCreation && message.EntityId == 0)
            {
                // Handle client request to create new asteroid
                var asteroid = AsteroidEntity.CreateAsteroid(
                    message.GetPosition(),
                    message.Size,
                    message.GetVelocity(),
                    message.GetType()
                );

                if (asteroid != null && _spawner != null)
                {
                    _spawner.AddAsteroid(asteroid);
                    var response = new AsteroidNetworkMessage(
                        asteroid.PositionComp.GetPosition(),
                        asteroid.Properties.Diameter,
                        asteroid.Physics.LinearVelocity,
                        asteroid.Physics.AngularVelocity,
                        asteroid.Type,
                        false,
                        asteroid.EntityId,
                        false,
                        true,
                        Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix)
                    );

                    byte[] responseBytes = MyAPIGateway.Utilities.SerializeToBinary(response);
                    MyAPIGateway.Multiplayer.SendMessageToOthers(32000, responseBytes);
                }
            }
        }

        private void RemoveAsteroidOnClient(long entityId)
        {
            Log.Info($"Client: Removing asteroid with ID {entityId}");

            AsteroidEntity asteroid = MyEntities.GetEntityById(entityId) as AsteroidEntity;
            if (asteroid != null)
            {
                try
                {
                    MyEntities.Remove(asteroid);
                    asteroid.Close();
                    Log.Info($"Client: Successfully removed asteroid {entityId}");
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, typeof(MainSession), $"Error removing asteroid {entityId} on client");
                }
            }
            else
            {
                Log.Warning($"Client: Could not find asteroid with ID {entityId} to remove");
            }
        }

        private void CreateNewAsteroidOnClient(AsteroidNetworkMessage message)
        {
            Log.Info($"Client: Creating new asteroid at position {message.GetPosition()}");
            try
            {
                Vector3D position = message.GetPosition();
                Quaternion rotation = message.GetRotation();

                // Create the initial world matrix properly
                MatrixD worldMatrix = MatrixD.CreateFromQuaternion(rotation);
                worldMatrix.Translation = position;

                // Create the asteroid with proper initial position
                var asteroid = AsteroidEntity.CreateAsteroid(
                    position,
                    message.Size,
                    message.GetVelocity(),
                    message.GetType(),
                    rotation,
                    message.EntityId
                );

                if (asteroid != null)
                {
                    // Ensure everything is set correctly after creation
                    asteroid.WorldMatrix = worldMatrix;
                    asteroid.PositionComp.SetPosition(position);
                    asteroid.Physics.LinearVelocity = message.GetVelocity();
                    asteroid.Physics.AngularVelocity = message.GetAngularVelocity();

                    // Verify position
                    Vector3D finalPosition = asteroid.PositionComp.GetPosition();
                    Log.Info($"Client: Created asteroid {message.EntityId}:" +
                             $"\nRequested Position: {position}" +
                             $"\nFinal Position: {finalPosition}" +
                             $"\nMatrix Translation: {asteroid.WorldMatrix.Translation}");

                    _serverPositions[message.EntityId] = position;
                    _serverRotations[message.EntityId] = rotation;
                }
                else
                {
                    Log.Warning($"Client: Failed to create asteroid {message.EntityId}");
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), $"Error creating new asteroid {message.EntityId} on client");
            }
        }

        private void ProcessClientMessage(AsteroidNetworkMessage message)
        {
            try
            {
                Log.Info($"Client processing message - ID: {message.EntityId}, IsInitial: {message.IsInitialCreation}, Position: {message.GetPosition()}");

                // If this is a removal message, handle it first
                if (message.IsRemoval)
                {
                    RemoveAsteroidOnClient(message.EntityId);
                    _serverPositions.Remove(message.EntityId);
                    return;
                }

                // Get existing asteroid if any
                AsteroidEntity asteroid = MyEntities.GetEntityById(message.EntityId) as AsteroidEntity;

                // Handle initial creation
                if (message.IsInitialCreation)
                {
                    if (asteroid != null)
                    {
                        Log.Warning($"Received creation message for existing asteroid {message.EntityId}");
                        return;
                    }

                    // Create new asteroid with correct initial position
                    MatrixD worldMatrix = MatrixD.CreateFromQuaternion(message.GetRotation());
                    worldMatrix.Translation = message.GetPosition();

                    asteroid = AsteroidEntity.CreateAsteroid(
                        message.GetPosition(),
                        message.Size,
                        message.GetVelocity(),
                        message.GetType(),
                        message.GetRotation(),
                        message.EntityId
                    );

                    if (asteroid != null)
                    {
                        // Ensure proper initialization
                        asteroid.WorldMatrix = worldMatrix;
                        asteroid.Physics.LinearVelocity = message.GetVelocity();
                        asteroid.Physics.AngularVelocity = message.GetAngularVelocity();
                        _serverPositions[message.EntityId] = message.GetPosition();

                        Log.Info($"Created new asteroid {message.EntityId} at {asteroid.PositionComp.GetPosition()}");
                    }
                }
                // Handle position updates only for existing asteroids
                else if (asteroid != null)
                {
                    _serverPositions[message.EntityId] = message.GetPosition();
                    UpdateExistingAsteroidOnClient(asteroid, message);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), $"Error processing client message for asteroid {message.EntityId}");
            }
        }

        private float GetQuaternionAngleDifference(Quaternion a, Quaternion b)
        {
            // Get the dot product between the quaternions
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

            // Clamp to handle floating point imprecision
            dot = MathHelper.Clamp(dot, -1f, 1f);

            // Calculate the angle
            return 2f * (float)Math.Acos(Math.Abs(dot));
        }


        private void UpdateServerPosition(long entityId, Vector3D position)
        {
            _serverPositions[entityId] = position;
        }

        private void UpdateExistingAsteroidOnClient(AsteroidEntity asteroid, AsteroidNetworkMessage message)
        {
            try
            {
                Vector3D newPosition = message.GetPosition();
                Vector3D newVelocity = message.GetVelocity();
                Quaternion newRotation = message.GetRotation();
                Vector3D newAngularVelocity = message.GetAngularVelocity();

                // Store server state for visualization
                _serverPositions[asteroid.EntityId] = newPosition;
                _serverRotations[asteroid.EntityId] = newRotation;

                // Create a proper world matrix that combines both position and rotation
                MatrixD newWorldMatrix = MatrixD.CreateFromQuaternion(newRotation);
                newWorldMatrix.Translation = newPosition; // Set the position after creating rotation matrix

                // Set everything in the correct order
                asteroid.WorldMatrix = newWorldMatrix;
                asteroid.PositionComp.SetPosition(newPosition); // Ensure position is set after WorldMatrix
                asteroid.Physics.LinearVelocity = newVelocity;
                asteroid.Physics.AngularVelocity = newAngularVelocity;

                // Log the actual position after update
                Vector3D finalPosition = asteroid.PositionComp.GetPosition();
                Log.Info($"Client asteroid {asteroid.EntityId} update:" +
                         $"\nRequested Position: {newPosition}" +
                         $"\nFinal Position: {finalPosition}" +
                         $"\nMatrix Translation: {asteroid.WorldMatrix.Translation}" +
                         $"\nAngular Velocity: {newAngularVelocity.Length():F3} rad/s");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), $"Error updating client asteroid {asteroid.EntityId}");
            }
        }

        private AsteroidEntity FindNearestAsteroid(Vector3D characterPosition)
        {
            if (characterPosition == null)
                return null;

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            AsteroidEntity nearestAsteroid = null;
            double minDistance = double.MaxValue;

            foreach (var entity in entities)
            {
                AsteroidEntity asteroid = entity as AsteroidEntity;
                if (asteroid != null)
                {
                    double distance = Vector3D.DistanceSquared(characterPosition, asteroid.PositionComp.GetPosition());
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestAsteroid = asteroid;
                    }
                }
            }

            return nearestAsteroid;
        }

        public void RegisterExistingAsteroids()
        {
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            foreach (var entity in entities)
            {
                AsteroidEntity asteroid = entity as AsteroidEntity;
                if (asteroid != null)
                {
                    _spawner.AddAsteroid(asteroid);
                }
            }
        }

        public override void Draw()
        {
            try
            {
                if (!AsteroidSettings.EnableLogging || MyAPIGateway.Session?.Player?.Character == null)
                    return;

                Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);

                if (nearestAsteroid != null)
                {
                    // Position visualization
                    Vector3D clientPosition = nearestAsteroid.PositionComp.GetPosition();
                    MatrixD clientWorldMatrix = MatrixD.CreateTranslation(clientPosition);
                    Color clientColor = Color.Red;
                    MySimpleObjectDraw.DrawTransparentSphere(ref clientWorldMatrix, nearestAsteroid.Properties.Radius,
                        ref clientColor, MySimpleObjectRasterizer.Wireframe, 20);

                    Vector3D serverPosition;
                    Quaternion serverRotation;
                    if (_serverPositions.TryGetValue(nearestAsteroid.EntityId, out serverPosition) &&
                        _serverRotations.TryGetValue(nearestAsteroid.EntityId, out serverRotation))
                    {
                        MatrixD serverWorldMatrix = MatrixD.CreateTranslation(serverPosition);
                        Color serverColor = Color.Blue;
                        MySimpleObjectDraw.DrawTransparentSphere(ref serverWorldMatrix, nearestAsteroid.Properties.Radius,
                            ref serverColor, MySimpleObjectRasterizer.Wireframe, 20);

                        // Draw connection line
                        Vector4 lineColor = Color.Yellow.ToVector4();
                        MySimpleObjectDraw.DrawLine(clientPosition, serverPosition,
                            MyStringId.GetOrCompute("Square"), ref lineColor, 0.1f);

                        // Calculate rotation difference
                        MatrixD clientMatrix = nearestAsteroid.WorldMatrix;
                        Quaternion clientRotation = Quaternion.CreateFromRotationMatrix(clientMatrix);

                        float angleDifference = GetQuaternionAngleDifference(clientRotation, serverRotation);
                        Vector3D clientForward = clientMatrix.Forward;
                        Vector3D serverForward = MatrixD.CreateFromQuaternion(serverRotation).Forward;

                        // Draw forward vectors
                        Vector4 clientDirColor = Color.Red.ToVector4();
                        Vector4 serverDirColor = Color.Blue.ToVector4();
                        MySimpleObjectDraw.DrawLine(clientPosition,
                            clientPosition + clientForward * nearestAsteroid.Properties.Radius * 2,
                            MyStringId.GetOrCompute("Square"), ref clientDirColor, 0.1f);
                        MySimpleObjectDraw.DrawLine(serverPosition,
                            serverPosition + serverForward * nearestAsteroid.Properties.Radius * 2,
                            MyStringId.GetOrCompute("Square"), ref serverDirColor, 0.1f);

                        MyAPIGateway.Utilities.ShowNotification(
                            $"Asteroid {nearestAsteroid.EntityId}:\n" +
                            $"Position diff: {Vector3D.Distance(clientPosition, serverPosition):F2}m\n" +
                            $"Rotation diff: {MathHelper.ToDegrees(angleDifference):F1}°",
                            16);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error in Draw");
            }
        }

        private AsteroidType DetermineAsteroidType()
        {
            int randValue = Rand.Next(0, 2);
            return (AsteroidType)randValue;
        }

    }
}