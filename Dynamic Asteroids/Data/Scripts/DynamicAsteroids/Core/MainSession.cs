﻿using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using DynamicAsteroids;


namespace DynamicAsteroids
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public partial class MainSession : MySessionComponentBase {
        public static MainSession I;
        public Random Rand;
        private int seed;
        public AsteroidSpawner _spawner;
        private int _saveStateTimer;
        private int _networkMessageTimer;
        private bool _isProcessingMessage = false;
        public RealGasGiantsApi RealGasGiantsApi { get; private set; }
        private int _testTimer = 0;
        private KeenRicochetMissileBSWorkaroundHandler _missileHandler;
        private Dictionary<long, Vector3D> _serverPositions = new Dictionary<long, Vector3D>();
        private Dictionary<long, Quaternion> _serverRotations = new Dictionary<long, Quaternion>();
        private Dictionary<long, AsteroidZone> _clientZones = new Dictionary<long, AsteroidZone>();


        public override void LoadData() {
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
            if (MyAPIGateway.Session.IsServer) {
                _spawner = new AsteroidSpawner(RealGasGiantsApi);
                _spawner.Init(seed);
            }

            if (!MyAPIGateway.Session.IsServer) {
                // Wait a few frames before starting
                MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                    MyAPIGateway.Utilities.InvokeOnGameThread(CleanupClientState);
                });
            }

            // Register network handlers for both client and server
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(32000, OnSecureMessageReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(32001, OnSecureMessageReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(32002, OnSettingsSyncReceived);
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }

        public override void BeforeStart() {
           
            MyVisualScriptLogicProvider.PlayerConnected += OnPlayerConnected;
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

        protected override void UnloadData() {
            try {
                Log.Info("Unloading data in MainSession");

                if (_spawner != null) {
                    if (MyAPIGateway.Session.IsServer) {
                        var asteroidsToRemove = _spawner.GetAsteroids().ToList();
                        foreach (var asteroid in asteroidsToRemove) {
                            try {
                                MyEntities.Remove(asteroid);
                                asteroid.Close();
                            }
                            catch (Exception removeEx) {
                                Log.Exception(removeEx, typeof(MainSession), "Error removing asteroid during unload");
                            }
                        }
                        _spawner.Close();
                        _spawner = null;
                    }
                }


                if (RealGasGiantsApi != null) {
                    RealGasGiantsApi.Unload();
                    RealGasGiantsApi = null;
                }

                if (!MyAPIGateway.Session.IsServer) {
                    Log.Info("Client session detected, performing final cleanup");
                    CleanupClientState();
                }

                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(32000, OnSecureMessageReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(32001, OnSecureMessageReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(32002, OnSettingsSyncReceived);
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                MyVisualScriptLogicProvider.PlayerConnected -= OnPlayerConnected;

                _missileHandler.Unload();
                AsteroidSettings.SaveSettings();
                Log.Close();
                I = null;

            }
            catch (Exception ex) {
                MyLog.Default.WriteLine($"Error in UnloadData: {ex}");
                try {
                    Log.Exception(ex, typeof(MainSession), "Error in UnloadData");
                }
                catch { }
            }
        }
        private void OnMessageEntered(string messageText, ref bool sendToOthers) {
            IMyPlayer player = MyAPIGateway.Session.Player;
            if (player == null || !IsPlayerAdmin(player)) return;

            if (!messageText.StartsWith("/dynamicasteroids") && !messageText.StartsWith("/dn")) return;
            var args = messageText.Split(' ');
            if (args.Length <= 1) return;
            switch (args[1].ToLower()) {
                case "createspawnarea":
                    double radius;
                    if (args.Length == 3 && double.TryParse(args[2], out radius)) {
                        CreateSpawnArea(radius);
                        sendToOthers = false;
                    }

                    break;

                case "removespawnarea":
                    if (args.Length == 3) {
                        RemoveSpawnArea(args[2]);
                        sendToOthers = false;
                    }

                    break;
            }
        }

        private bool IsPlayerAdmin(IMyPlayer player) {
            return MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE ||
                   MyAPIGateway.Session.IsUserAdmin(player.SteamUserId);
        }

        private void CreateSpawnArea(double radius) {
            IMyPlayer player = MyAPIGateway.Session.Player;
            if (player == null) return;

            Vector3D position = player.GetPosition();
            var name = $"Area_{position.GetHashCode()}";

            BoundingBoxD boundingBox =
                new BoundingBoxD(position - new Vector3D(radius), position + new Vector3D(radius));
            MyPlanet closestPlanet = MyGamePruningStructure.GetClosestPlanet(ref boundingBox);

            if (closestPlanet != null) {
                Log.Info(
                    $"Cannot create spawn area '{name}' at {position} with radius {radius}: Intersects with a planet.");
                MyAPIGateway.Utilities.ShowMessage("DynamicAsteroids",
                    $"Cannot create spawn area '{name}' at {position} with radius {radius}: Intersects with a planet.");
                return;
            }

            AsteroidSettings.AddSpawnableArea(name, position, radius);
            Log.Info($"Created spawn area '{name}' at {position} with radius {radius}");
            MyAPIGateway.Utilities.ShowMessage("DynamicAsteroids",
                $"Created spawn area '{name}' at {position} with radius {radius}");
        }

        private void RemoveSpawnArea(string name) {
            AsteroidSettings.RemoveSpawnableArea(name);
            Log.Info($"Removed spawn area '{name}'");
            MyAPIGateway.Utilities.ShowMessage("DynamicAsteroids", $"Removed spawn area '{name}'");
        }

        public override void UpdateAfterSimulation() {
            try {
                // Server-only updates
                if (MyAPIGateway.Session.IsServer) {
                    _spawner?.UpdateTick();

                    List<IMyPlayer> players = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(players);

                    foreach (IMyPlayer player in players) {
                        Vector3D playerPosition = player.GetPosition();
                        AsteroidEntity nearestAsteroid = FindNearestAsteroid(playerPosition);

                        if (nearestAsteroid != null) {
                            //Log.Info($"Server: Nearest asteroid to player at {playerPosition}: Asteroid ID: {nearestAsteroid.EntityId}, Position: {nearestAsteroid.PositionComp.GetPosition()}");
                        }
                    }

                    if (_saveStateTimer > 0)
                        _saveStateTimer--;

                    if (_networkMessageTimer > 0)
                        _networkMessageTimer--;
                    else {
                        _spawner?.SendNetworkMessages();
                        _networkMessageTimer = AsteroidSettings.NetworkMessageInterval;
                        Log.Info("Server: Sending network messages to clients.");
                    }
                }

                // Middle mouse spawn (handle for both client and server)
                if (AsteroidSettings.EnableMiddleMouseAsteroidSpawn &&
                    MyAPIGateway.Input.IsNewKeyPressed(MyKeys.MiddleButton) &&
                    MyAPIGateway.Session?.Player != null) {
                    Vector3D position = MyAPIGateway.Session.Player.GetPosition();
                    Vector3D velocity = MyAPIGateway.Session.Player.Character?.Physics?.LinearVelocity ?? Vector3D.Zero;
                    AsteroidType type = DetermineAsteroidType();

                    if (MyAPIGateway.Session.IsServer) {
                        // Server creates the asteroid directly
                        var asteroid = AsteroidEntity.CreateAsteroid(position, Rand.Next(50), velocity, type);
                        if (asteroid != null && _spawner != null) {
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
                    else {
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
                if (MyAPIGateway.Session?.Player?.Character != null) {
                    Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                    AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);

                    if (nearestAsteroid != null && AsteroidSettings.EnableLogging) {
                        Vector3D angularVelocity = nearestAsteroid.Physics.AngularVelocity;
                        string rotationString =
                            $"({angularVelocity.X:F2}, {angularVelocity.Y:F2}, {angularVelocity.Z:F2})";
                        string message =
                            $"Nearest Asteroid: {nearestAsteroid.EntityId} ({nearestAsteroid.Type})\nRotation: {rotationString}";
                        MyAPIGateway.Utilities.ShowNotification(message, 1000 / 60);
                        nearestAsteroid.DrawDebugSphere();
                    }

                    // Log the number of active asteroids (for debugging purposes)
                    if (AsteroidSettings.EnableLogging) {
                        if (!MyAPIGateway.Session.IsServer) {
                            var entities = new HashSet<IMyEntity>();
                            MyAPIGateway.Entities.GetEntities(entities);
                            int localAsteroidCount = entities.Count(e => e is AsteroidEntity);

                            if (AsteroidSettings.EnableLogging) {
                                MyAPIGateway.Utilities.ShowNotification($"Client Asteroids: {localAsteroidCount}",
                                    1000 / 60);
                            }
                        }
                    }

                    // Update orphaned asteroids list periodically
                    if (++_orphanCheckTimer >= ORPHAN_CHECK_INTERVAL) {
                        _orphanCheckTimer = 0;
                        UpdateOrphanedAsteroidsList();
                    }
                }

                // Shared updates
                if (++_testTimer >= 240) {
                    _testTimer = 0;
                    TestNearestGasGiant();
                }

                Log.Update();
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error in UpdateAfterSimulation: ");
            }
        }

        public void DebugMissiles() {
            if (!AsteroidSettings.EnableLogging)
                return;

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            int missileCount = 0;
            foreach (var entity in entities) {
                IMyMissile missile = entity as IMyMissile;
                if (missile != null) {
                    var ammoDef = missile.AmmoDefinition as MyMissileAmmoDefinition;
                    if (ammoDef != null) {
                        MyAPIGateway.Utilities.ShowNotification(
                            $"Missile detected:\n" +
                            $"Type: {ammoDef.Id.SubtypeName}\n", 1000 / 60);
                    }
                }
            }

            if (missileCount == 0) {
                MyAPIGateway.Utilities.ShowNotification("No missiles found in world", 1000 / 60);
            }
        }


        private void TestNearestGasGiant() {
            if (RealGasGiantsApi == null || !RealGasGiantsApi.IsReady || MyAPIGateway.Session?.Player == null)
                return;

            if (!AsteroidSettings.EnableLogging)
                return;

            Vector3D playerPosition = MyAPIGateway.Session.Player.GetPosition();
            MyPlanet nearestGasGiant = FindNearestGasGiant(playerPosition);

            // Get the global ring influence at the player's position
            float ringInfluence = RealGasGiantsApi.GetRingInfluenceAtPositionGlobal(playerPosition);

            string message;

            if (nearestGasGiant != null) {
                var basicInfo = RealGasGiantsApi.GetGasGiantConfig_BasicInfo_Base(nearestGasGiant);
                if (basicInfo.Item1) // If operation was successful
                {
                    double distance = Vector3D.Distance(playerPosition, nearestGasGiant.PositionComp.GetPosition()) -
                                      basicInfo.Item2;
                    message = $"Nearest Gas Giant:\n" +
                              $"Distance: {distance:N0}m\n" +
                              $"Radius: {basicInfo.Item2:N0}m\n" +
                              $"Color: {basicInfo.Item3}\n" +
                              $"Skin: {basicInfo.Item4}\n" +
                              $"Day Length: {basicInfo.Item5:F2}s\n" +
                              $"Current Ring Influence: {ringInfluence:F3}";
                }
                else {
                    message = "Failed to get gas giant info";
                }
            }
            else {
                message = $"Current Ring Influence: {ringInfluence:F3}";
            }

            if (AsteroidSettings.EnableLogging)
                MyAPIGateway.Utilities.ShowNotification(message, 4000, "White");
        }

        private MyPlanet FindNearestGasGiant(Vector3D position) {
            const double searchRadius = 1000000000; // 1 million km in meters
            MyPlanet nearestGasGiant = null;
            double nearestDistance = double.MaxValue;

            // Get all gas giants within the larger search sphere
            var gasGiants = RealGasGiantsApi.GetAtmoGasGiantsAtPosition(position);

            foreach (MyPlanet gasGiant in gasGiants) {
                var basicInfo = RealGasGiantsApi.GetGasGiantConfig_BasicInfo_Base(gasGiant);
                if (!basicInfo.Item1) continue; // Skip if we couldn't get the info

                float gasGiantRadius = basicInfo.Item2;
                Vector3D gasGiantCenter = gasGiant.PositionComp.GetPosition();

                // Calculate distance from player to the surface of the gas giant
                double distance = Vector3D.Distance(position, gasGiantCenter) - gasGiantRadius;

                if (!(distance < nearestDistance) || !(distance <= searchRadius)) continue;
                nearestDistance = distance;
                nearestGasGiant = gasGiant;
            }

            if (nearestGasGiant != null) {
                //Log.Info($"Found nearest gas giant at distance: {nearestDistance:N0} meters");
            }
            else {
                //Log.Info("No gas giants found within 1 million km");
            }

            return nearestGasGiant;
        }

        private void OnSecureMessageReceived(ushort handlerId, byte[] message, ulong steamId, bool isFromServer) {
            try {
                if (message == null || message.Length == 0) {
                    Log.Info("Received empty or null message, skipping processing.");
                    return;
                }

                // Handle zone updates
                if (handlerId == 32001) {
                    ProcessZoneMessage(message);
                    return;
                }

                if (handlerId == 32000) {
                    try {
                        // Try to process as batch update first
                        var batchPacket = MyAPIGateway.Utilities.SerializeFromBinary<AsteroidBatchUpdatePacket>(message);
                        if (batchPacket != null) {
                            ProcessBatchMessage(batchPacket);
                            return;
                        }
                    }
                    catch {
                        // If batch deserialization fails, try single message
                        var singleMessage = MyAPIGateway.Utilities.SerializeFromBinary<AsteroidNetworkMessage>(message);
                        if (singleMessage != null) {
                            if (!MyAPIGateway.Session.IsServer) {
                                ProcessClientMessage(singleMessage);
                            }
                            else {
                                ProcessServerMessage(singleMessage, steamId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), $"Error processing received message");
            }
        }

        public class NetworkMessageVerification {
            public static bool ValidateMessage(AsteroidNetworkMessage message) {
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


        private void ProcessServerMessage(AsteroidNetworkMessage message, ulong steamId) {
            if (!NetworkMessageVerification.ValidateMessage(message)) {
                Log.Warning($"Server received invalid message from client {steamId}");
                return;
            }

            if (message.IsInitialCreation && message.EntityId == 0) {
                // Handle client request to create new asteroid
                var asteroid = AsteroidEntity.CreateAsteroid(
                    message.GetPosition(),
                    message.Size,
                    message.GetVelocity(),
                    message.GetType()
                );

                if (asteroid != null && _spawner != null) {
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

        private void RemoveAsteroidOnClient(long entityId) {
            try {
                Log.Info($"Client: Removing asteroid with ID {entityId}");

                // Remove from tracking first
                _knownAsteroidIds.Remove(entityId);
                _serverPositions.Remove(entityId);
                _serverRotations.Remove(entityId);

                // Then remove the entity
                var asteroid = MyEntities.GetEntityById(entityId) as AsteroidEntity;
                if (asteroid != null) {
                    try {
                        MyEntities.Remove(asteroid);
                        asteroid.Close();
                        Log.Info($"Client: Successfully removed asteroid {entityId}");
                    }
                    catch (Exception ex) {
                        Log.Warning($"Error removing asteroid {entityId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), $"Error removing asteroid {entityId} on client");
            }
        }

        private void CreateNewAsteroidOnClient(AsteroidNetworkMessage message) {
            // Always run asteroid creation on the game thread
            MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                try {
                    Log.Info($"Attempting to create asteroid {message.EntityId}");

                    // Double check for existing entity
                    var existingEntity = MyEntities.GetEntityById(message.EntityId);
                    if (existingEntity != null) {
                        Log.Warning($"Found existing entity {message.EntityId}, removing it first");
                        existingEntity.Close();
                        MyEntities.Remove(existingEntity);

                        // Force a frame update
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                            var asteroid = AsteroidEntity.CreateAsteroid(
                                message.GetPosition(),
                                message.Size,
                                message.GetVelocity(),
                                message.GetType(),
                                message.GetRotation(),
                                message.EntityId
                            );

                            if (asteroid != null) {
                                Log.Info($"Successfully created asteroid {message.EntityId}");
                                _knownAsteroidIds.Add(message.EntityId);
                            }
                        });
                    }
                    else {
                        var asteroid = AsteroidEntity.CreateAsteroid(
                            message.GetPosition(),
                            message.Size,
                            message.GetVelocity(),
                            message.GetType(),
                            message.GetRotation(),
                            message.EntityId
                        );

                        if (asteroid != null) {
                            Log.Info($"Successfully created asteroid {message.EntityId}");
                            _knownAsteroidIds.Add(message.EntityId);
                        }
                    }
                }
                catch (Exception ex) {
                    Log.Warning($"Failed to create asteroid {message.EntityId}: {ex.Message}");
                }
            });
        }

        private HashSet<long> _knownAsteroidIds = new HashSet<long>();

        private void ProcessClientMessage(AsteroidNetworkMessage message) {
            if (_isProcessingMessage) {
                Log.Warning($"Skipping message for {message.EntityId} - already processing a message");
                return;
            }

            try {
                _isProcessingMessage = true;
                Log.Info($"Processing client message for asteroid {message.EntityId} (IsRemoval: {message.IsRemoval}, IsInitialCreation: {message.IsInitialCreation})");

                if (!NetworkMessageVerification.ValidateMessage(message)) {
                    Log.Warning($"Client received invalid message - ID: {message.EntityId}");
                    return;
                }

                if (message.IsRemoval) {
                    RemoveAsteroidOnClient(message.EntityId);
                    _knownAsteroidIds.Remove(message.EntityId);
                    return;
                }

                bool isKnown = _knownAsteroidIds.Contains(message.EntityId);
                AsteroidEntity existingAsteroid = MyEntities.GetEntityById(message.EntityId) as AsteroidEntity;

                // For initial creation or if we detect a duplicate ID
                if (message.IsInitialCreation || existingAsteroid != null) {
                    Log.Info($"Handling {(message.IsInitialCreation ? "initial creation" : "duplicate")} for asteroid {message.EntityId}");

                    if (existingAsteroid != null) {
                        Log.Info($"Removing existing asteroid {message.EntityId} before recreation");
                        RemoveAsteroidOnClient(message.EntityId);
                    }

                    try {
                        CreateNewAsteroidOnClient(message);
                        _knownAsteroidIds.Add(message.EntityId);
                        Log.Info($"Successfully created asteroid {message.EntityId}");
                    }
                    catch (Exception ex) {
                        Log.Warning($"Failed to create asteroid {message.EntityId}: {ex.Message}");
                        // Ensure cleanup in case of failure
                        RemoveAsteroidOnClient(message.EntityId);
                        _knownAsteroidIds.Remove(message.EntityId);
                    }
                }
                // For regular updates to known asteroids
                else if (isKnown) {
                    existingAsteroid = MyEntities.GetEntityById(message.EntityId) as AsteroidEntity;
                    if (existingAsteroid != null) {
                        UpdateExistingAsteroidOnClient(existingAsteroid, message);
                    }
                    else {
                        // Known asteroid but entity missing - recreate it
                        Log.Info($"Recreating missing known asteroid {message.EntityId}");
                        try {
                            CreateNewAsteroidOnClient(new AsteroidNetworkMessage(
                                message.GetPosition(), message.Size, message.GetVelocity(),
                                message.GetAngularVelocity(), message.GetType(), false,
                                message.EntityId, false, true, message.GetRotation()));
                        }
                        catch (Exception ex) {
                            Log.Warning($"Failed to recreate known asteroid {message.EntityId}: {ex.Message}");
                            _knownAsteroidIds.Remove(message.EntityId);
                        }
                    }
                }
                // For unknown asteroids that aren't marked as initial creation
                else {
                    Log.Info($"Received update for unknown asteroid {message.EntityId}, treating as new");
                    try {
                        // Force IsInitialCreation to true for unknown asteroids
                        var newMessage = new AsteroidNetworkMessage(
                            message.GetPosition(), message.Size, message.GetVelocity(),
                            message.GetAngularVelocity(), message.GetType(), false,
                            message.EntityId, false, true, message.GetRotation());

                        CreateNewAsteroidOnClient(newMessage);
                        _knownAsteroidIds.Add(message.EntityId);
                    }
                    catch (Exception ex) {
                        Log.Warning($"Failed to create unknown asteroid {message.EntityId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), $"Error processing client message");
            }
            finally {
                _isProcessingMessage = false;
            }
        }
        private void ProcessBatchMessage(AsteroidBatchUpdatePacket packet) {
            try {
                Log.Info($"Processing batch update with {packet.Removals?.Count ?? 0} removals, " +
                         $"{packet.Updates?.Count ?? 0} updates, " +
                         $"{packet.Spawns?.Count ?? 0} spawns");

                // Handle removals first
                if (packet.Removals != null && packet.Removals.Count > 0) {
                    foreach (long entityId in packet.Removals) {
                        RemoveAsteroidOnClient(entityId);
                        _knownAsteroidIds.Remove(entityId);
                    }
                }

                // Handle state updates
                if (packet.Updates != null && packet.Updates.Count > 0) {
                    foreach (var state in packet.Updates) {
                        var asteroid = MyEntities.GetEntityById(state.EntityId) as AsteroidEntity;
                        if (asteroid != null) {
                            // Update existing asteroid
                            try {
                                MatrixD worldMatrix = MatrixD.CreateFromQuaternion(state.Rotation);
                                worldMatrix.Translation = state.Position;
                                asteroid.WorldMatrix = worldMatrix;

                                if (asteroid.Physics != null) {
                                    asteroid.Physics.LinearVelocity = state.Velocity;
                                    asteroid.Physics.AngularVelocity = state.AngularVelocity;
                                }
                            }
                            catch (Exception ex) {
                                Log.Warning($"Failed to update asteroid {state.EntityId}: {ex.Message}");
                            }
                        }
                        else if (!_knownAsteroidIds.Contains(state.EntityId)) {
                            // Create missing asteroid
                            Log.Info($"Creating missing asteroid from batch update: {state.EntityId}");
                            try {
                                var message = new AsteroidNetworkMessage(
                                    state.Position,
                                    state.Size,
                                    state.Velocity,
                                    state.AngularVelocity,
                                    state.Type,
                                    false,
                                    state.EntityId,
                                    false,
                                    true,  // Treat as initial creation
                                    state.Rotation
                                );
                                CreateNewAsteroidOnClient(message);
                                _knownAsteroidIds.Add(state.EntityId);
                            }
                            catch (Exception ex) {
                                Log.Warning($"Failed to create missing asteroid {state.EntityId}: {ex.Message}");
                            }
                        }
                    }
                }

                // Handle new spawns
                if (packet.Spawns != null && packet.Spawns.Count > 0) {
                    foreach (var spawn in packet.Spawns) {
                        try {
                            if (MyEntities.GetEntityById(spawn.EntityId) != null) {
                                Log.Warning($"Received spawn packet for existing asteroid {spawn.EntityId}, removing first");
                                RemoveAsteroidOnClient(spawn.EntityId);
                            }

                            var message = new AsteroidNetworkMessage(
                                spawn.Position,
                                spawn.Size,
                                spawn.Velocity,
                                spawn.AngularVelocity,
                                spawn.Type,
                                false,
                                spawn.EntityId,
                                false,
                                true,
                                spawn.Rotation
                            );
                            CreateNewAsteroidOnClient(message);
                            _knownAsteroidIds.Add(spawn.EntityId);
                            Log.Info($"Created new asteroid from spawn packet: {spawn.EntityId}");
                        }
                        catch (Exception ex) {
                            Log.Warning($"Failed to process spawn packet for asteroid {spawn.EntityId}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error processing batch update");
            }
        }
        private float GetQuaternionAngleDifference(Quaternion a, Quaternion b) {
            // Get the dot product between the quaternions
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

            // Clamp to handle floating point imprecision
            dot = MathHelper.Clamp(dot, -1f, 1f);

            // Calculate the angle
            return 2f * (float)Math.Acos(Math.Abs(dot));
        }


        private void UpdateServerPosition(long entityId, Vector3D position) {
            _serverPositions[entityId] = position;
        }

        private const double DRIFT_TOLERANCE = 0.1; // meters
        private Dictionary<long, DateTime> _lastPhysicsResetTime = new Dictionary<long, DateTime>();
        private const double PHYSICS_RESET_COOLDOWN = 1.0; // seconds

        private void UpdateExistingAsteroidOnClient(AsteroidEntity asteroid, AsteroidNetworkMessage message) {
            try {
                Vector3D newPosition = message.GetPosition();
                Vector3D newVelocity = message.GetVelocity();
                Quaternion newRotation = message.GetRotation();
                Vector3D newAngularVelocity = message.GetAngularVelocity();

                // Store previous state for change detection
                Vector3D oldPosition = asteroid.PositionComp.GetPosition();
                Vector3D oldVelocity = asteroid.Physics?.LinearVelocity ?? Vector3D.Zero;
                Vector3D oldAngularVelocity = asteroid.Physics?.AngularVelocity ?? Vector3D.Zero;

                // Calculate drift
                double positionDrift = Vector3D.Distance(oldPosition, newPosition);
                bool hasDrift = positionDrift > DRIFT_TOLERANCE;

                // Check movement state
                bool wasMoving = oldVelocity.LengthSquared() > 0.01 || oldAngularVelocity.LengthSquared() > 0.01;
                bool isMoving = newVelocity.LengthSquared() > 0.01 || newAngularVelocity.LengthSquared() > 0.01;
                bool stateChanged = wasMoving != isMoving;

                _serverPositions[asteroid.EntityId] = newPosition;
                _serverRotations[asteroid.EntityId] = newRotation;

                if (stateChanged && asteroid.Physics != null) {
                    // Full physics reset on state change
                    var oldPhysics = asteroid.Physics;
                    asteroid.Physics = null;

                    MatrixD newWorldMatrix = MatrixD.CreateFromQuaternion(newRotation);
                    newWorldMatrix.Translation = newPosition;
                    asteroid.WorldMatrix = newWorldMatrix;
                    asteroid.PositionComp.SetPosition(newPosition);

                    asteroid.Physics = oldPhysics;
                    asteroid.Physics.Clear();
                    asteroid.Physics.LinearVelocity = newVelocity;
                    asteroid.Physics.AngularVelocity = newAngularVelocity;

                    Log.Info($"Physics reset for asteroid {asteroid.EntityId} due to state change");
                }
                else {
                    // Normal update
                    MatrixD newWorldMatrix = MatrixD.CreateFromQuaternion(newRotation);
                    newWorldMatrix.Translation = newPosition;
                    asteroid.WorldMatrix = newWorldMatrix;
                    asteroid.PositionComp.SetPosition(newPosition);

                    if (asteroid.Physics != null) {
                        asteroid.Physics.LinearVelocity = newVelocity;
                        asteroid.Physics.AngularVelocity = newAngularVelocity;
                    }
                }

                if (hasDrift || stateChanged) {
                    Log.Info($"Client asteroid {asteroid.EntityId} update:" +
                            $"\nDrift: {positionDrift:F2}m" +
                            $"\nState: {(isMoving ? "Moving" : "Stopped")}" +
                            $"\nVelocity: {newVelocity.Length():F2}m/s" +
                            $"\nAngular velocity: {newAngularVelocity.Length():F3}rad/s");
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), $"Error updating client asteroid {asteroid.EntityId}");
            }
        }
        private AsteroidEntity FindNearestAsteroid(Vector3D characterPosition) {
            if (characterPosition == null)
                return null;

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            AsteroidEntity nearestAsteroid = null;
            double minDistance = double.MaxValue;

            foreach (var entity in entities) {
                AsteroidEntity asteroid = entity as AsteroidEntity;
                if (asteroid != null) {
                    double distance = Vector3D.DistanceSquared(characterPosition, asteroid.PositionComp.GetPosition());
                    if (distance < minDistance) {
                        minDistance = distance;
                        nearestAsteroid = asteroid;
                    }
                }
            }

            return nearestAsteroid;
        }

        private AsteroidType DetermineAsteroidType() {
            int randValue = Rand.Next(0, 2);
            return (AsteroidType)randValue;
        }

        public void UpdateClientZones(Dictionary<long, AsteroidZone> serverZones) {
            if (!MyAPIGateway.Utilities.IsDedicated) {
                _clientZones.Clear();
                foreach (var kvp in serverZones) {
                    _clientZones[kvp.Key] = kvp.Value;
                }
            }
        }
        private Dictionary<long, Vector3D> _lastProcessedZonePositions = new Dictionary<long, Vector3D>();
        private void ProcessZoneMessage(byte[] message) {
            try {
                var zonePacket = MyAPIGateway.Utilities.SerializeFromBinary<ZoneUpdatePacket>(message);
                if (zonePacket?.Zones == null || zonePacket.Zones.Count == 0) return;

                // Clear all existing known asteroid IDs when zones change significantly
                bool significantZoneChange = false;
                foreach (var zoneData in zonePacket.Zones) {
                    Vector3D lastPos;
                    if (_lastProcessedZonePositions.TryGetValue(zoneData.PlayerId, out lastPos)) {
                        if (Vector3D.DistanceSquared(lastPos, zoneData.Center) > AsteroidSettings.ZoneRadius * AsteroidSettings.ZoneRadius) {
                            significantZoneChange = true;
                            break;
                        }
                    }
                    else {
                        significantZoneChange = true;
                    }
                }

                if (significantZoneChange) {
                    Log.Info("Significant zone change detected, clearing client state");
                    _knownAsteroidIds.Clear();
                    _serverPositions.Clear();
                    _serverRotations.Clear();
                }

                if (!MyAPIGateway.Session.IsServer) {
                    _clientZones.Clear();
                    foreach (var zoneData in zonePacket.Zones) {
                        var newZone = new AsteroidZone(zoneData.Center, zoneData.Radius) {
                            IsMarkedForRemoval = !zoneData.IsActive,
                            IsMerged = zoneData.IsMerged,
                            CurrentSpeed = zoneData.CurrentSpeed,
                            LastActiveTime = DateTime.UtcNow
                        };
                        _clientZones[zoneData.PlayerId] = newZone;
                        _lastProcessedZonePositions[zoneData.PlayerId] = zoneData.Center;
                    }

                    // Clean up asteroids that are no longer in any zone
                    var entities = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(entities);
                    foreach (var entity in entities) {
                        var asteroid = entity as AsteroidEntity;
                        if (asteroid == null) continue;

                        bool inAnyZone = false;
                        foreach (var zone in _clientZones.Values) {
                            if (zone.IsPointInZone(asteroid.PositionComp.GetPosition())) {
                                inAnyZone = true;
                                break;
                            }
                        }

                        if (!inAnyZone) {
                            RemoveAsteroidOnClient(asteroid.EntityId);
                        }
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error processing zone packet");
            }
        }

        private void OnSettingsSyncReceived(ushort handlerId, byte[] data, ulong steamId, bool isFromServer) {
            if (!isFromServer) return;

            try {
                var settings = MyAPIGateway.Utilities.SerializeFromBinary<SettingsSyncMessage>(data);
                if (settings != null) {
                    AsteroidSettings.EnableLogging = settings.EnableLogging;
                    Log.Info($"Received settings from server. Debug logging: {settings.EnableLogging}");
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error processing settings sync");
            }
        }

        private void OnPlayerConnected(long identityId) {
            MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                var player = players.FirstOrDefault(p => p.IdentityId == identityId);

                if (player != null) {
                    Log.Info($"Syncing settings to player {player.DisplayName}");
                    SendSettingsToClient(player.SteamUserId);
                }
            }, "SyncSettings");
        }

        private void SendSettingsToClient(ulong steamId) {
            try {
                var settings = new SettingsSyncMessage {
                    EnableLogging = AsteroidSettings.EnableLogging
                };

                byte[] data = MyAPIGateway.Utilities.SerializeToBinary(settings);
                MyAPIGateway.Multiplayer.SendMessageTo(32002, data, steamId);
                Log.Info($"Sent settings to client {steamId}");
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error sending settings to client");
            }
        }

        private void CleanupClientState() {
            Log.Info("Starting client state cleanup...");

            // First, clear all our tracking
            _knownAsteroidIds.Clear();
            _serverPositions.Clear();
            _serverRotations.Clear();
            _clientZones.Clear();

            // Force a game engine update to ensure entity lists are current
            MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                try {
                    Log.Info("Performing entity cleanup on game thread");
                    var entities = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(entities);

                    // First pass: Close all asteroids
                    foreach (var entity in entities)
                    {
                        AsteroidEntity asteroid = entity as AsteroidEntity;
                        if (asteroid != null) {
                            try {
                                Log.Info($"Closing asteroid {asteroid.EntityId}");
                                asteroid.Close();
                            }
                            catch (Exception ex) {
                                Log.Warning($"Error closing asteroid {asteroid.EntityId}: {ex.Message}");
                            }
                        }
                    }

                    // Second pass: Remove all asteroids
                    foreach (var entity in entities)
                    {
                        AsteroidEntity asteroid = entity as AsteroidEntity;
                        if (asteroid != null) {
                            try {
                                Log.Info($"Removing asteroid {asteroid.EntityId} from entities");
                                MyEntities.Remove(asteroid);
                            }
                            catch (Exception ex) {
                                Log.Warning($"Error removing asteroid {asteroid.EntityId}: {ex.Message}");
                            }
                        }
                    }

                    // Verify cleanup
                    entities.Clear();
                    MyAPIGateway.Entities.GetEntities(entities);
                    var remainingAsteroids = entities.Where(e => e is AsteroidEntity).ToList();
                    if (remainingAsteroids.Any()) {
                        Log.Warning($"Found {remainingAsteroids.Count} remaining asteroids after cleanup:");
                        foreach (var asteroid in remainingAsteroids) {
                            Log.Warning($" - Asteroid {asteroid.EntityId} still exists");
                        }
                    }
                }
                catch (Exception ex) {
                    Log.Exception(ex, typeof(MainSession), "Error during entity cleanup");
                }
            });

            // Wait a frame to ensure cleanup is complete
            MyAPIGateway.Utilities.InvokeOnGameThread(() => {
                Log.Info("Cleanup verification complete");
            });
        }
    }
}