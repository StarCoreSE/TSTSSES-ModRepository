using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using RealGasGiants;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using static DynamicAsteroids.Data.Scripts.DynamicAsteroids.MainSession;


namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    public class AsteroidZone
    {
        public Vector3D Center { get; set; }
        public double Radius { get; set; }
        public int AsteroidCount { get; set; }
        public bool IsMerged { get; set; }
        public long EntityId { get; set; }

        public AsteroidZone(Vector3D center, double radius)
        {
            Center = center;
            Radius = radius;
            AsteroidCount = 0;
            IsMerged = false;
            EntityId = 0;
        }

        public bool IsPointInZone(Vector3D point)
        {
            return Vector3D.DistanceSquared(Center, point) <= Radius * Radius;
        }
    }

    public class AsteroidSpawner {
        private ConcurrentBag<AsteroidEntity> _asteroids;
        private bool _canSpawnAsteroids = false;
        private DateTime _worldLoadTime;
        private Random rand;
        private List<AsteroidState> _despawnedAsteroids = new List<AsteroidState>();
        private ConcurrentQueue<AsteroidNetworkMessage> _networkMessages = new ConcurrentQueue<AsteroidNetworkMessage>();
        private ConcurrentDictionary<long, AsteroidZone> playerZones = new ConcurrentDictionary<long, AsteroidZone>();
        private ConcurrentDictionary<long, PlayerMovementData> playerMovementData = new ConcurrentDictionary<long, PlayerMovementData>();

        private ConcurrentQueue<AsteroidEntity> _updateQueue = new ConcurrentQueue<AsteroidEntity>();
        private const int UpdatesPerTick = 50;// update rate of the roids

        private RealGasGiantsApi _realGasGiantsApi;


        private class ZoneCache {
            public AsteroidZone Zone { get; set; }
            public DateTime LastUpdateTime { get; set; }
            const int CacheExpirationSeconds = 5;

            public bool IsExpired()
            {
                return (DateTime.UtcNow - LastUpdateTime).TotalSeconds > CacheExpirationSeconds;
            }
        }

        private class AsteroidStateCache {
            private ConcurrentDictionary<long, AsteroidState> _stateCache = new ConcurrentDictionary<long, AsteroidState>();
            private ConcurrentBag<long> _dirtyStates = new ConcurrentBag<long>();
            private const int SaveInterval = 300;
            private DateTime _lastSaveTime = DateTime.UtcNow;
            private ConcurrentBag<long> _dirtyAsteroids = new ConcurrentBag<long>();

            private const double POSITION_CHANGE_THRESHOLD = 1;// 1m
            private const double VELOCITY_CHANGE_THRESHOLD = 1;// 1m/s


            public void UpdateState(AsteroidEntity asteroid)
            {
                if (asteroid == null) return;

                Vector3D currentPosition = asteroid.PositionComp.GetPosition();
                Vector3D currentVelocity = asteroid.Physics.LinearVelocity;

                lock (_stateCache) // Add thread safety
                {
                    AsteroidState cachedState;
                    if (_stateCache.TryGetValue(asteroid.EntityId, out cachedState))
                    {
                        // Use squared distance for better performance
                        double positionDeltaSquared = Vector3D.DistanceSquared(cachedState.Position, currentPosition);
                        double velocityDeltaSquared = Vector3D.DistanceSquared(cachedState.Velocity, currentVelocity);

                        if (!(positionDeltaSquared > POSITION_CHANGE_THRESHOLD * POSITION_CHANGE_THRESHOLD) &&
                            !(velocityDeltaSquared > VELOCITY_CHANGE_THRESHOLD * VELOCITY_CHANGE_THRESHOLD)) return;
                        _stateCache[asteroid.EntityId] = new AsteroidState(asteroid);
                        _dirtyAsteroids.Add(asteroid.EntityId);
                        Log.Info($"Updated state for asteroid {asteroid.EntityId}: Position delta: {Math.Sqrt(positionDeltaSquared):F2}, Velocity delta: {Math.Sqrt(velocityDeltaSquared):F2}");
                    }
                    else
                    {
                        _stateCache[asteroid.EntityId] = new AsteroidState(asteroid);
                        _dirtyAsteroids.Add(asteroid.EntityId);
                        Log.Info($"Initial state cache for asteroid {asteroid.EntityId}");
                    }
                }
            }

            public List<AsteroidState> GetDirtyAsteroids()
            {
                return _dirtyAsteroids.Select(id => _stateCache[id]).ToList();
            }

            public AsteroidState GetState(long asteroidId)
            {
                AsteroidState state;
                _stateCache.TryGetValue(asteroidId, out state);
                return state;
            }

            public List<AsteroidState> GetDirtyStates()
            {
                return _stateCache.Where(kvp => _dirtyStates.Contains(kvp.Key))
                    .Select(kvp => kvp.Value)
                    .ToList();
            }

            public void ClearDirtyStates()
            {
                long id;
                while(_dirtyAsteroids.TryTake(out id)) {}
            }

            public bool ShouldSave()
            {
                return (DateTime.UtcNow - _lastSaveTime).TotalSeconds >= SaveInterval && !_dirtyStates.IsEmpty;
            }


        }

        private class NetworkMessageCache {
            private ConcurrentDictionary<long, AsteroidNetworkMessage> _messageCache = new ConcurrentDictionary<long, AsteroidNetworkMessage>();
            private ConcurrentQueue<AsteroidNetworkMessage> _physicsUpdateQueue = new ConcurrentQueue<AsteroidNetworkMessage>();
            private ConcurrentQueue<AsteroidNetworkMessage> _spawnQueue = new ConcurrentQueue<AsteroidNetworkMessage>();
            private ConcurrentQueue<AsteroidNetworkMessage> _removalQueue = new ConcurrentQueue<AsteroidNetworkMessage>();

            private const int PhysicsMessageBatchSize = 100;
            private const int SpawnMessageBatchSize = 10;
            private const int RemovalMessageBatchSize = 20;

            public void AddMessage(AsteroidNetworkMessage message)
            {
                if (_messageCache.TryAdd(message.EntityId, message))
                {
                    if (message.IsInitialCreation)
                    {
                        _spawnQueue.Enqueue(message);
                    }
                    else if (message.IsRemoval)
                    {
                        _removalQueue.Enqueue(message);
                    }
                    else
                    {
                        _physicsUpdateQueue.Enqueue(message);
                    }
                }
            }

            public void ProcessMessages(Action<AsteroidNetworkMessage> sendAction)
            {
                // Process spawns first
                ProcessQueue(_spawnQueue, SpawnMessageBatchSize, sendAction);

                // Process removals next
                ProcessQueue(_removalQueue, RemovalMessageBatchSize, sendAction);

                // Process physics updates last
                ProcessQueue(_physicsUpdateQueue, PhysicsMessageBatchSize, sendAction);
            }

            private void ProcessQueue(ConcurrentQueue<AsteroidNetworkMessage> queue,
                int batchSize,
                Action<AsteroidNetworkMessage> sendAction)
            {
                int processedCount = 0;
                AsteroidNetworkMessage message;

                while(processedCount < batchSize && queue.TryDequeue(out message))
                {
                    try
                    {
                        sendAction(message);
                        AsteroidNetworkMessage removedMessage;
                        _messageCache.TryRemove(message.EntityId, out removedMessage);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex, typeof(NetworkMessageCache),
                            "Failed to process network message");
                        queue.Enqueue(message);
                    }
                }
            }
        }

        private ConcurrentDictionary<long, ZoneCache> _zoneCache = new ConcurrentDictionary<long, ZoneCache>();
        private AsteroidStateCache _stateCache = new AsteroidStateCache();
        private NetworkMessageCache _messageCache = new NetworkMessageCache();

        public AsteroidSpawner(RealGasGiantsApi realGasGiantsApi)
        {
            _realGasGiantsApi = realGasGiantsApi;
        }

        private class PlayerMovementData {
            public Vector3D LastPosition { get; set; }
            public DateTime LastUpdateTime { get; set; }
            public double Speed { get; set; }
        }

        public void Init(int seed)
        {
            if (!MyAPIGateway.Session.IsServer) return;
            Log.Info("Initializing AsteroidSpawner");
            _asteroids = new ConcurrentBag<AsteroidEntity>();
            _worldLoadTime = DateTime.UtcNow;
            rand = new Random(seed);
            AsteroidSettings.Seed = seed;
        }
        public IEnumerable<AsteroidEntity> GetAsteroids()
        {
            return _asteroids;
        }
        public void AddAsteroid(AsteroidEntity asteroid)
        {
            _asteroids.Add(asteroid);
        }
        public bool TryRemoveAsteroid(AsteroidEntity asteroid)
        {
            return _asteroids.TryTake(out asteroid);
        }
        public bool ContainsAsteroid(long asteroidId)
        {
            return _asteroids.Any(a => a.EntityId == asteroidId);
        }
        private void LoadAsteroidsInRange(Vector3D playerPosition, AsteroidZone zone)
        {
            int skippedCount = 0;
            int respawnedCount = 0;
            List<Vector3D> skippedPositions = new List<Vector3D>();
            List<Vector3D> respawnedPositions = new List<Vector3D>();
            foreach (AsteroidState state in _despawnedAsteroids.ToArray())
            {
                if (!zone.IsPointInZone(state.Position)) continue;
                bool tooClose = _asteroids.Any(a => Vector3D.DistanceSquared(a.PositionComp.GetPosition(), state.Position) < AsteroidSettings.MinDistanceFromPlayer * AsteroidSettings.MinDistanceFromPlayer);
                if (tooClose)
                {
                    skippedCount++;
                    skippedPositions.Add(state.Position);
                    continue;
                }
                respawnedCount++;
                respawnedPositions.Add(state.Position);
                AsteroidEntity asteroid = AsteroidEntity.CreateAsteroid(state.Position, state.Size, Vector3D.Zero, state.Type);
                asteroid.EntityId = state.EntityId;
                _asteroids.Add(asteroid);
                AsteroidNetworkMessage message = new AsteroidNetworkMessage(state.Position, state.Size, Vector3D.Zero, Vector3D.Zero, state.Type, false, asteroid.EntityId, false, true, Quaternion.Identity);
                byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                _despawnedAsteroids.Remove(state);

                _updateQueue.Enqueue(asteroid);
            }
            if (skippedCount > 0)
            {
                Log.Info($"Skipped respawn of {skippedCount} asteroids due to proximity to other asteroids or duplicate ID.");
            }
            if (respawnedCount > 0)
            {
                Log.Info($"Respawned {respawnedCount} asteroids at positions: {string.Join(", ", respawnedPositions.Select(p => p.ToString()))}");
            }
        }

        public void Close()
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            try
            {
                Log.Info("Closing AsteroidSpawner");
                _zoneCache.Clear();
                playerZones.Clear();
                playerMovementData.Clear();

                if (_asteroids != null)
                {
                    foreach (AsteroidEntity asteroid in _asteroids)
                    {
                        try
                        {
                            MyEntities.Remove(asteroid);
                            asteroid.Close();
                        }
                        catch (Exception ex)
                        {
                            Log.Exception(ex, typeof(AsteroidSpawner), "Error closing individual asteroid");
                        }
                    }
                    _asteroids = null;
                }

                _updateQueue = new ConcurrentQueue<AsteroidEntity>();
                _networkMessages = new ConcurrentQueue<AsteroidNetworkMessage>();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in Close method");
            }
        }
        public void AssignMergedZonesToPlayers(List<AsteroidZone> mergedZones)
        {
            const double REASSIGNMENT_THRESHOLD = 100.0; // Distance threshold for zone reassignment
            const double OVERLAP_BUFFER = 50.0; // Buffer distance for handling zone overlaps

            try
            {
                // Get current player states
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                var newPlayerZones = new ConcurrentDictionary<long, AsteroidZone>();
                var zoneAssignments = new ConcurrentDictionary<AsteroidZone, HashSet<long>>();

                // First pass: Initial zone assignment based on closest zones
                foreach (IMyPlayer player in players)
                {
                    Vector3D playerPos = player.GetPosition();
                    PlayerMovementData movementData;

                    // Get or create movement data for velocity checks
                    if (!playerMovementData.TryGetValue(player.IdentityId, out movementData))
                    {
                        movementData = new PlayerMovementData
                        {
                            LastPosition = playerPos,
                            LastUpdateTime = DateTime.UtcNow,
                            Speed = 0
                        };
                        playerMovementData[player.IdentityId] = movementData;
                    }

                    // Skip high-speed players
                    if (movementData.Speed > AsteroidSettings.ZoneSpeedThreshold)
                    {
                        Log.Info($"Skipping zone assignment for high-speed player {player.DisplayName} ({movementData.Speed:F2} m/s).");
                        continue;
                    }

                    // Find closest valid zone
                    AsteroidZone bestZone = null;
                    double bestDistance = double.MaxValue;

                    foreach (AsteroidZone zone in mergedZones)
                    {
                        double distance = Vector3D.Distance(playerPos, zone.Center);
                        if (!(distance < bestDistance) || !(distance <= zone.Radius + OVERLAP_BUFFER)) continue;
                        bestDistance = distance;
                        bestZone = zone;
                    }

                    // Handle zone assignment
                    if (bestZone == null) continue;
                    // Check if player should keep their existing zone
                    AsteroidZone currentZone;
                    if (playerZones.TryGetValue(player.IdentityId, out currentZone))
                    {
                        double distanceToCurrentZone = Vector3D.Distance(playerPos, currentZone.Center);
                        if (distanceToCurrentZone <= currentZone.Radius &&
                            distanceToCurrentZone <= bestDistance + REASSIGNMENT_THRESHOLD)
                        {
                            bestZone = currentZone;
                        }
                    }
                    newPlayerZones[player.IdentityId] = bestZone;

                    // Track zone assignments for overlap handling
                    if (!zoneAssignments.ContainsKey(bestZone))
                    {
                        zoneAssignments[bestZone] = new HashSet<long>();
                    }
                    zoneAssignments[bestZone].Add(player.IdentityId);
                }

                // Second pass: Handle overlapping zones and optimize assignments
                foreach (var zoneAssignment in zoneAssignments)
                {
                    AsteroidZone zone = zoneAssignment.Key;
                    var playerIds = zoneAssignment.Value;
                    if (playerIds.Count > AsteroidSettings.MaxPlayersPerZone)
                    {
                        OptimizeZoneAssignments(zone, playerIds, newPlayerZones, mergedZones);
                    }
                }

                // Final pass: Update zone properties and handle teleportation
                foreach (var kvp in newPlayerZones)
                {
                    long playerId = kvp.Key;
                    AsteroidZone newZone = kvp.Value;
                    AsteroidZone oldZone;

                    if (playerZones.TryGetValue(playerId, out oldZone))
                    {
                        // Handle potential teleportation
                        IMyPlayer player = players.FirstOrDefault(p => p.IdentityId == playerId);
                        if (player != null)
                        {
                            Vector3D playerPos = player.GetPosition();
                            double distanceToOldZone = Vector3D.Distance(playerPos, oldZone.Center);
                            double distanceToNewZone = Vector3D.Distance(playerPos, newZone.Center);

                            if (distanceToNewZone > distanceToOldZone * 2)
                            {
                                // Possible teleportation detected
                                Log.Info($"Possible teleportation detected for player {player.DisplayName}. Reassigning zone.");
                                HandlePlayerTeleportation(player, newZone, oldZone);
                            }
                        }
                    }

                    // Update zone assignment
                    playerZones[playerId] = newZone;
                }

                // Cleanup unassigned zones
                CleanupUnassignedZones();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in AssignMergedZonesToPlayers");
            }
        }
        private void HandlePlayerTeleportation(IMyPlayer player, AsteroidZone newZone, AsteroidZone oldZone)
        {
            Vector3D playerPos = player.GetPosition();

            // Update movement data to reflect teleportation
            playerMovementData[player.IdentityId] = new PlayerMovementData
            {
                LastPosition = playerPos,
                LastUpdateTime = DateTime.UtcNow,
                Speed = 0 // Reset speed after teleportation
            };

            // Trigger immediate cleanup of old zone if necessary
            if (oldZone != null && !playerZones.Values.Contains(oldZone))
            {
                CleanupZone(oldZone);
            }

            Log.Info($"Player {player.DisplayName} teleported from {oldZone?.Center} to {newZone.Center}");
        }
        private AsteroidZone FindAlternateZone(Vector3D position, AsteroidZone excludeZone, List<AsteroidZone> mergedZones)
        {
            return mergedZones
                .Where(z => z != excludeZone && Vector3D.Distance(position, z.Center) <= z.Radius)
                .OrderBy(z => Vector3D.Distance(position, z.Center))
                .FirstOrDefault();
        }

        private void CleanupUnassignedZones()
        {
            var assignedZones = new HashSet<AsteroidZone>(playerZones.Values);
            foreach (AsteroidZone zone in playerZones.Values.ToList())
            {
                if (!assignedZones.Contains(zone))
                {
                    CleanupZone(zone);
                }
            }
        }

        private void CleanupZone(AsteroidZone zone)
        {
            // Handle asteroid cleanup for the zone
            foreach (AsteroidEntity asteroid in _asteroids.Where(a => Vector3D.Distance(a.PositionComp.GetPosition(), zone.Center) <= zone.Radius))
            {
                RemoveAsteroid(asteroid);
            }
        }
        public void AssignZonesToPlayers()
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            Dictionary<long, AsteroidZone> updatedZones = new Dictionary<long, AsteroidZone>();
            foreach (IMyPlayer player in players)
            {
                Vector3D playerPosition = player.GetPosition();
                PlayerMovementData data;
                if (playerMovementData.TryGetValue(player.IdentityId, out data))
                {
                    if (AsteroidSettings.DisableZoneWhileMovingFast && data.Speed > AsteroidSettings.ZoneSpeedThreshold)
                    {
                        Log.Info($"Skipping zone creation for player {player.DisplayName} due to high speed: {data.Speed} m/s.");
                        continue;
                    }
                }
                AsteroidZone existingZone;
                if (playerZones.TryGetValue(player.IdentityId, out existingZone))
                {
                    if (existingZone.IsPointInZone(playerPosition))
                    {
                        updatedZones[player.IdentityId] = existingZone;
                    }
                    else
                    {
                        AsteroidZone newZone = new AsteroidZone(playerPosition, AsteroidSettings.ZoneRadius);
                        updatedZones[player.IdentityId] = newZone;
                    }
                }
                else
                {
                    AsteroidZone newZone = new AsteroidZone(playerPosition, AsteroidSettings.ZoneRadius);
                    updatedZones[player.IdentityId] = newZone;
                }
            }
            playerZones = new ConcurrentDictionary<long, AsteroidZone>(updatedZones);
        }
        private void OptimizeZoneAssignments(AsteroidZone zone, HashSet<long> playerIds, ConcurrentDictionary<long, AsteroidZone> newPlayerZones, List<AsteroidZone> mergedZones)
        {
            // Retrieve the list of all players
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            // Sort players by distance to zone center
            var playerDistances = playerIds
                .Select(id => players.FirstOrDefault(p => p.IdentityId == id))
                .Where(player => player != null)
                .Select(player => new
                {
                    PlayerId = player.IdentityId,
                    Distance = Vector3D.Distance(player.GetPosition(), zone.Center)
                })
                .OrderBy(p => p.Distance)
                .ToList();

            // Keep closest players up to max limit
            for (int i = AsteroidSettings.MaxPlayersPerZone; i < playerDistances.Count; i++)
            {
                var playerId = playerDistances[i].PlayerId;

                // Find next best zone for overflow players
                IMyPlayer player = players.FirstOrDefault(p => p.IdentityId == playerId);
                if (player == null) continue;
                AsteroidZone alternateZone = FindAlternateZone(player.GetPosition(), zone, mergedZones);
                if (alternateZone != null)
                {
                    newPlayerZones[playerId] = alternateZone;
                }
            }
        }

        public void MergeZones()
        {
            List<AsteroidZone> mergedZones = new List<AsteroidZone>();
            foreach (AsteroidZone zone in playerZones.Values)
            {
                bool merged = false;
                foreach (AsteroidZone mergedZone in mergedZones)
                {
                    double distance = Vector3D.Distance(zone.Center, mergedZone.Center);
                    double combinedRadius = zone.Radius + mergedZone.Radius;
                    if (!(distance <= combinedRadius)) continue;
                    Vector3D newCenter = (zone.Center + mergedZone.Center) / 2;
                    double newRadius = Math.Max(zone.Radius, mergedZone.Radius) + distance / 2;
                    mergedZone.Center = newCenter;
                    mergedZone.Radius = newRadius;
                    mergedZone.AsteroidCount += zone.AsteroidCount;
                    merged = true;
                    break;
                }
                if (!merged)
                {
                    mergedZones.Add(new AsteroidZone(zone.Center, zone.Radius) { AsteroidCount = zone.AsteroidCount });
                }
            }
            AssignMergedZonesToPlayers(mergedZones);
        }
        public void UpdateZones()
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            Dictionary<long, AsteroidZone> updatedZones = new Dictionary<long, AsteroidZone>();
            foreach (IMyPlayer player in players)
            {
                Vector3D playerPosition = player.GetPosition();
                PlayerMovementData data;
                if (playerMovementData.TryGetValue(player.IdentityId, out data))
                {
                    if (AsteroidSettings.DisableZoneWhileMovingFast && data.Speed > AsteroidSettings.ZoneSpeedThreshold)
                    {
                        Log.Info($"Skipping zone update for player {player.DisplayName} due to high speed: {data.Speed} m/s.");
                        continue;
                    }
                }
                bool playerInZone = false;
                foreach (AsteroidZone zone in playerZones.Values)
                {
                    if (!zone.IsPointInZone(playerPosition)) continue;
                    playerInZone = true;
                    break;
                }
                if (playerInZone) continue;
                AsteroidZone newZone = new AsteroidZone(playerPosition, AsteroidSettings.ZoneRadius);
                updatedZones[player.IdentityId] = newZone;
            }
            foreach (KeyValuePair<long, AsteroidZone> kvp in playerZones)
            {
                if (players.Any(p => kvp.Value.IsPointInZone(p.GetPosition())))
                {
                    updatedZones[kvp.Key] = kvp.Value;
                }
            }
            playerZones = new ConcurrentDictionary<long, AsteroidZone>(updatedZones);
        }

        private AsteroidZone GetCachedZone(long playerId, Vector3D playerPosition)
        {
            ZoneCache cache;
            if (_zoneCache.TryGetValue(playerId, out cache) && !cache.IsExpired())
            {
                if (cache.Zone.IsPointInZone(playerPosition))
                    return cache.Zone;
            }

            AsteroidZone zone = new AsteroidZone(playerPosition, AsteroidSettings.ZoneRadius);
            _zoneCache.AddOrUpdate(playerId,
                new ZoneCache { Zone = zone, LastUpdateTime = DateTime.UtcNow },
                (key, oldCache) => new ZoneCache { Zone = zone, LastUpdateTime = DateTime.UtcNow });

            return zone;
        }

        private int _spawnIntervalTimer = 0;
        private int _updateIntervalTimer = 0;

        public void UpdateTick()
        {
            if (!MyAPIGateway.Session.IsServer) return;// Ensure this only runs on the server

            AssignZonesToPlayers();
            MergeZones();
            UpdateZones();
            SendZoneUpdates();

            try
            {
                // Get a list of all players
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                // For each player, load asteroids that are within their zone
                foreach (IMyPlayer player in players)
                {
                    AsteroidZone zone = GetCachedZone(player.IdentityId, player.GetPosition());
                    if (zone != null)
                    {
                        LoadAsteroidsInRange(player.GetPosition(), zone);
                    }
                }

                // Periodically send asteroid updates to clients
                if (_updateIntervalTimer <= 0)
                {
                    // Update asteroids' positions and other properties
                    UpdateAsteroids(playerZones.Values.ToList());

                    // Process the updates and send to clients
                    ProcessAsteroidUpdates();
                    _updateIntervalTimer = AsteroidSettings.UpdateInterval;// Reset the update timer
                }
                else
                {
                    _updateIntervalTimer--;
                }

                // Spawn new asteroids periodically
                if (_spawnIntervalTimer > 0)
                {
                    _spawnIntervalTimer--;
                }
                else
                {
                    SpawnAsteroids(playerZones.Values.ToList());
                    _spawnIntervalTimer = AsteroidSettings.SpawnInterval;// Reset the spawn timer
                }


                SendPositionUpdates();


                // Log the number of active asteroids (for debugging purposes)
                if (AsteroidSettings.EnableLogging)
                    MyAPIGateway.Utilities.ShowNotification($"Active Asteroids: {_asteroids.Count}", 1000 / 60);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in UpdateTick");
            }
        }

        private void UpdateAsteroids(List<AsteroidZone> zones)
        {
            int removedCount = 0;

            List<AsteroidEntity> asteroidsToRemove = new List<AsteroidEntity>();

            foreach (AsteroidEntity asteroid in _asteroids)
            {
                bool inAnyZone = false;
                AsteroidZone currentZone = null;
                foreach (AsteroidZone zone in zones)
                {
                    if (!zone.IsPointInZone(asteroid.PositionComp.GetPosition())) continue;
                    inAnyZone = true;
                    currentZone = zone;
                    break;
                }
                if (!inAnyZone)
                {
                    Log.Info($"Removing asteroid at {asteroid.PositionComp.GetPosition()} due to distance from all player zones");
                    asteroidsToRemove.Add(asteroid);
                    removedCount++;
                }
                else if (currentZone != null)
                {
                    foreach (AsteroidZone zone in zones)
                    {
                        if (zone != currentZone && zone.IsPointInZone(asteroid.PositionComp.GetPosition()))
                        {
                            zone.AsteroidCount--;
                        }
                    }
                    currentZone.AsteroidCount++;
                }
            }

            foreach (AsteroidEntity asteroid in asteroidsToRemove)
            {
                RemoveAsteroid(asteroid);
            }
        }

        public void ProcessAsteroidUpdates()
        {
            var dirtyAsteroids = _stateCache.GetDirtyAsteroids()
                .OrderBy(a => GetDistanceToClosestPlayer(a.Position));// Prioritize by distance

            int updatesProcessed = 0;

            foreach (AsteroidState asteroidState in dirtyAsteroids)
            {
                if (updatesProcessed >= UpdatesPerTick) break;

                // Prepare and send an update message to clients
                AsteroidNetworkMessage message = new AsteroidNetworkMessage(
                    asteroidState.Position, asteroidState.Size, asteroidState.Velocity,
                    Vector3D.Zero, asteroidState.Type, false, asteroidState.EntityId,
                    false, true, asteroidState.Rotation);

                _messageCache.AddMessage(message);// Add to message cache for processing
                updatesProcessed++;
            }
        }

        private double GetDistanceToClosestPlayer(Vector3D position)
        {
            double minDistance = double.MaxValue;
            foreach (AsteroidZone zone in playerZones.Values)
            {
                double distance = Vector3D.DistanceSquared(zone.Center, position);
                if (distance < minDistance)
                    minDistance = distance;
            }
            return minDistance;
        }

        public void SpawnAsteroids(List<AsteroidZone> zones)
        {
            int totalSpawnAttempts = 0;

            if (AsteroidSettings.MaxAsteroidCount == 0)
            {
                Log.Info("Asteroid spawning is disabled.");
                return;
            }

            int totalAsteroidsSpawned = 0;
            int totalZoneSpawnAttempts = 0;
            List<Vector3D> skippedPositions = new List<Vector3D>();
            List<Vector3D> spawnedPositions = new List<Vector3D>();

            UpdatePlayerMovementData();

            foreach (AsteroidZone zone in zones)
            {
                int asteroidsSpawned = 0;
                int zoneSpawnAttempts = 0;

                if (zone.AsteroidCount >= AsteroidSettings.MaxAsteroidsPerZone)
                {
                    continue;
                }

                bool skipSpawning = false;
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                foreach (IMyPlayer player in players)
                {
                    Vector3D playerPosition = player.GetPosition();
                    if (!zone.IsPointInZone(playerPosition)) continue;
                    PlayerMovementData data;
                    if (!playerMovementData.TryGetValue(player.IdentityId, out data)) continue;
                    if (!(data.Speed > 1000)) continue;
                    Log.Info($"Skipping asteroid spawning for player {player.DisplayName} due to high speed: {data.Speed} m/s.");
                    skipSpawning = true;
                    break;
                }

                if (skipSpawning)
                {
                    continue;
                }

                while(zone.AsteroidCount < AsteroidSettings.MaxAsteroidsPerZone &&
                      asteroidsSpawned < 10 &&
                      zoneSpawnAttempts < AsteroidSettings.MaxZoneAttempts &&
                      totalSpawnAttempts < AsteroidSettings.MaxTotalAttempts)
                {
                    Vector3D newPosition;
                    bool isInRing = false;
                    bool validPosition = false;
                    float ringInfluence = 0f;

                    do
                    {
                        newPosition = zone.Center + RandVector() * AsteroidSettings.ZoneRadius;
                        zoneSpawnAttempts++;
                        totalSpawnAttempts++;
                        //Log.Info($"Attempting to spawn asteroid at {newPosition} (attempt {totalSpawnAttempts})");

                        if (AsteroidSettings.EnableGasGiantRingSpawning && _realGasGiantsApi != null && _realGasGiantsApi.IsReady)
                        {
                            ringInfluence = _realGasGiantsApi.GetRingInfluenceAtPositionGlobal(newPosition);
                            if (ringInfluence > AsteroidSettings.MinimumRingInfluenceForSpawn)
                            {
                                validPosition = true;
                                isInRing = true;
                                Log.Info($"Position {newPosition} is within a gas giant ring (influence: {ringInfluence})");
                            }
                        }

                        if (!isInRing)
                        {
                            validPosition = IsValidSpawnPosition(newPosition, zones);
                        }

                    } while(!validPosition &&
                            zoneSpawnAttempts < AsteroidSettings.MaxZoneAttempts &&
                            totalSpawnAttempts < AsteroidSettings.MaxTotalAttempts);

                    if (zoneSpawnAttempts >= AsteroidSettings.MaxZoneAttempts || totalSpawnAttempts >= AsteroidSettings.MaxTotalAttempts)
                        break;

                    Vector3D newVelocity;
                    if (!AsteroidSettings.CanSpawnAsteroidAtPoint(newPosition, out newVelocity, isInRing))
                    {
                        //Log.Info($"Cannot spawn asteroid at {newPosition}, skipping.");
                        continue;
                    }

                    if (IsNearVanillaAsteroid(newPosition))
                    {
                        Log.Info($"Position {newPosition} is near a vanilla asteroid, skipping.");
                        skippedPositions.Add(newPosition);
                        continue;
                    }

                    if (AsteroidSettings.MaxAsteroidCount != -1 && _asteroids.Count >= AsteroidSettings.MaxAsteroidCount)
                    {
                        Log.Warning($"Maximum asteroid count of {AsteroidSettings.MaxAsteroidCount} reached. No more asteroids will be spawned until existing ones are removed.");
                        return;
                    }

                    if (zone.AsteroidCount >= AsteroidSettings.MaxAsteroidsPerZone)
                    {
                        Log.Info($"Zone at {zone.Center} has reached its maximum asteroid count ({AsteroidSettings.MaxAsteroidsPerZone}). Skipping further spawning in this zone.");
                        break;
                    }

                    float spawnChance = isInRing ?
                        MathHelper.Lerp(0.1f, 1f, ringInfluence) * AsteroidSettings.MaxRingAsteroidDensityMultiplier :
                        1f;

                    if (MainSession.I.Rand.NextDouble() > spawnChance)
                    {
                        Log.Info($"Asteroid spawn skipped due to density scaling (spawn chance: {spawnChance})");
                        continue;
                    }

                    AsteroidType type = AsteroidSettings.GetAsteroidType(newPosition);
                    float size = AsteroidSettings.GetAsteroidSize(newPosition);

                    if (isInRing)
                    {
                        size *= MathHelper.Lerp(0.5f, 1f, ringInfluence);
                    }

                    Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
                        (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                        (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi,
                        (float)MainSession.I.Rand.NextDouble() * MathHelper.TwoPi);

                    AsteroidEntity asteroid = AsteroidEntity.CreateAsteroid(newPosition, size, newVelocity, type, rotation);

                    if (asteroid == null) continue;
                    _asteroids.Add(asteroid);
                    zone.AsteroidCount++;
                    spawnedPositions.Add(newPosition);

                    _messageCache.AddMessage(new AsteroidNetworkMessage(newPosition, size, newVelocity, Vector3D.Zero, type, false, asteroid.EntityId, false, true, rotation));
                    asteroidsSpawned++;

                    Log.Info($"Spawned asteroid at {newPosition} with size {size} and type {type}");
                }

                totalAsteroidsSpawned += asteroidsSpawned;
                totalZoneSpawnAttempts += zoneSpawnAttempts;
            }

            if (!AsteroidSettings.EnableLogging) return;
            if (skippedPositions.Count > 0)
            {
                Log.Info($"Skipped spawning asteroids due to proximity to vanilla asteroids. Positions: {string.Join(", ", skippedPositions.Select(p => p.ToString()))}");
            }
            if (spawnedPositions.Count > 0)
            {
                Log.Info($"Spawned asteroids at positions: {string.Join(", ", spawnedPositions.Select(p => p.ToString()))}");
            }
        }

        private void UpdatePlayerMovementData()
        {
            const double SPEED_SMOOTHING_FACTOR = 1;
            const double MIN_TIME_DELTA = 1; // Minimum time delta to prevent division by zero

            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (IMyPlayer player in players)
            {
                Vector3D currentPosition = player.GetPosition();
                DateTime currentTime = DateTime.UtcNow;

                PlayerMovementData data;
                if (!playerMovementData.TryGetValue(player.IdentityId, out data))
                {
                    data = new PlayerMovementData
                    {
                        LastPosition = currentPosition,
                        LastUpdateTime = currentTime,
                        Speed = 0
                    };
                    playerMovementData[player.IdentityId] = data;
                    continue;
                }

                double timeElapsed = Math.Max((currentTime - data.LastUpdateTime).TotalSeconds, MIN_TIME_DELTA);
                double distance = Vector3D.Distance(currentPosition, data.LastPosition);
                double instantaneousSpeed = distance / timeElapsed;

                // Smooth speed calculation
                data.Speed = (data.Speed * (1 - SPEED_SMOOTHING_FACTOR)) + (instantaneousSpeed * SPEED_SMOOTHING_FACTOR);
                data.LastPosition = currentPosition;
                data.LastUpdateTime = currentTime;

                if (data.Speed > AsteroidSettings.ZoneSpeedThreshold)
                {
                    Log.Info($"Player {player.DisplayName} moving at high speed: {data.Speed:F2} m/s");
                }
            }
        }

        private bool IsValidSpawnPosition(Vector3D position, List<AsteroidZone> zones)
        {
            BoundingSphereD sphere = new BoundingSphereD(position, AsteroidSettings.MinDistanceFromPlayer);
            List<MyEntity> entities = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Both);
            foreach (MyEntity entity in entities)
            {
                if (entity is IMyCharacter || entity is IMyShipController)
                {
                    return false;
                }
            }
            if (AsteroidSettings.EnableGasGiantRingSpawning && _realGasGiantsApi != null && _realGasGiantsApi.IsReady)
            {
                float ringInfluence = _realGasGiantsApi.GetRingInfluenceAtPositionGlobal(position);
                if (ringInfluence > AsteroidSettings.MinimumRingInfluenceForSpawn)
                {
                    //Log.Info($"Valid position in ring: {position}, influence: {ringInfluence}");
                    return true;
                }
            }
            foreach (SpawnableArea area in AsteroidSettings.ValidSpawnLocations)
            {
                if (!area.ContainsPoint(position)) continue;
                //Log.Info($"Valid position in SpawnableArea: {position}");
                return true;
            }
            foreach (AsteroidZone zone in zones)
            {
                if (!zone.IsPointInZone(position)) continue;
                //Log.Info($"Valid position in player zone: {position}");
                return true;
            }
            //Log.Info($"Invalid spawn position: {position}");
            return false;
        }

        public void SendNetworkMessages()
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            try
            {
                int messagesSent = 0;
                _messageCache.ProcessMessages(message =>
                {
                    if (message.EntityId == 0)
                    {
                        Log.Warning("Attempted to send message for asteroid with ID 0");
                        return;
                    }

                    // Get the actual asteroid to ensure it still exists
                    AsteroidEntity asteroid = MyEntities.GetEntityById(message.EntityId) as AsteroidEntity;
                    if (asteroid == null)
                    {
                        Log.Warning($"Attempted to send update for non-existent asteroid {message.EntityId}");
                        return;
                    }

                    // Create fresh message with current data
                    AsteroidNetworkMessage updateMessage = new AsteroidNetworkMessage(
                        asteroid.PositionComp.GetPosition(),
                        asteroid.Properties.Diameter,
                        asteroid.Physics.LinearVelocity,
                        asteroid.Physics.AngularVelocity,
                        asteroid.Type,
                        false,
                        asteroid.EntityId,
                        false,
                        false,// This is an update, not initial creation
                        Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix)
                    );

                    byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(updateMessage);

                    if (messageBytes == null || messageBytes.Length == 0)
                    {
                        Log.Warning("Failed to serialize network message");
                        return;
                    }

                    Log.Info($"Server: Sending position update for asteroid ID {updateMessage.EntityId}, " +
                             $"Position: {updateMessage.GetPosition()}, " +
                             $"Velocity: {updateMessage.GetVelocity()}, " +
                             $"Data size: {messageBytes.Length} bytes");

                    MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                    messagesSent++;
                });

                if (messagesSent > 0)
                {
                    Log.Info($"Server: Successfully sent {messagesSent} asteroid updates");
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error sending network messages");
            }
        }

        private void RemoveAsteroid(AsteroidEntity asteroid)
        {
            AsteroidEntity removedAsteroid;
            if (!_asteroids.TryTake(out removedAsteroid)) return;

            if (removedAsteroid.EntityId == asteroid.EntityId)
            {
                // Create the AsteroidState using the constructor that accepts an AsteroidEntity
                _despawnedAsteroids.Add(new AsteroidState(asteroid));

                // Send a network message indicating that this asteroid has been removed
                _messageCache.AddMessage(new AsteroidNetworkMessage(
                    asteroid.PositionComp.GetPosition(),
                    asteroid.Properties.Diameter,
                    Vector3D.Zero,
                    Vector3D.Zero,
                    asteroid.Type,
                    false,// IsRemoval
                    asteroid.EntityId,
                    true,// IsRemoval
                    false,// IsInitialCreation
                    Quaternion.Identity));

                // Remove asteroid from the entity list and close it
                MyEntities.Remove(asteroid);
                asteroid.Close();

                Log.Info($"Server: Removed asteroid with ID {asteroid.EntityId} from _asteroids list and MyEntities");
            }
            else
            {
                _asteroids.Add(removedAsteroid);// Return it to the collection if not the same entity
            }
        }

        public void SendPositionUpdates()
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            try
            {
                var messages = new List<AsteroidNetworkMessage>();
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                foreach (AsteroidEntity asteroid in _asteroids)
                {
                    if (asteroid == null || asteroid.MarkedForClose)
                        continue;

                    // Check if asteroid is near any player
                    bool isNearPlayer = false;
                    Vector3D asteroidPos = asteroid.PositionComp.GetPosition();

                    foreach (IMyPlayer player in players)
                    {
                        double distSquared = Vector3D.DistanceSquared(player.GetPosition(), asteroidPos);
                        if (distSquared <= AsteroidSettings.ZoneRadius * AsteroidSettings.ZoneRadius)
                        {
                            isNearPlayer = true;
                            break;
                        }
                    }

                    if (!isNearPlayer)
                        continue;

                    // Update state and check if asteroid needs update
                    _stateCache.UpdateState(asteroid);
                }

                // Get only dirty asteroids that need updates
                var dirtyAsteroids = _stateCache.GetDirtyAsteroids();

                foreach (AsteroidState state in dirtyAsteroids)
                {
                    AsteroidNetworkMessage positionUpdate = new AsteroidNetworkMessage(
                        state.Position,
                        state.Size,
                        state.Velocity,
                        Vector3D.Zero,// Only send angular velocity occasionally
                        state.Type,
                        false,
                        state.EntityId,
                        false,
                        false,
                        state.Rotation
                    );

                    messages.Add(positionUpdate);
                }

                // Only send if we have changes to report
                if (messages.Count > 0)
                {
                    AsteroidNetworkMessageContainer container = new AsteroidNetworkMessageContainer(messages.ToArray());
                    byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(container);
                    MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);

                    Log.Info($"Server: Sent batch update for {messages.Count} changed asteroids");
                }

                _stateCache.ClearDirtyStates();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error sending position updates");
            }
        }

        private bool IsNearVanillaAsteroid(Vector3D position)
        {
            BoundingSphereD sphere = new BoundingSphereD(position, AsteroidSettings.MinDistanceFromVanillaAsteroids);
            List<MyEntity> entities = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Static);
            foreach (MyEntity entity in entities)
            {
                IMyVoxelMap voxelMap = entity as IMyVoxelMap;
                if (voxelMap == null || voxelMap.StorageName.StartsWith("mod_")) continue;
                Log.Info($"Position {position} is near vanilla asteroid {voxelMap.StorageName}");
                return true;
            }
            return false;
        }

        private Vector3D RandVector()
        {
            double theta = rand.NextDouble() * 2.0 * Math.PI;
            double phi = Math.Acos(2.0 * rand.NextDouble() - 1.0);
            double sinPhi = Math.Sin(phi);
            return Math.Pow(rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }

        public void SendZoneUpdates()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            var zoneMessage = new ZoneNetworkMessage();
            var mergedZoneIds = new HashSet<long>();

            // First pass to identify merged zones
            foreach (var zone1 in playerZones.Values)
            {
                foreach (var zone2 in playerZones.Values)
                {
                    if (zone1 != zone2)
                    {
                        double distance = Vector3D.Distance(zone1.Center, zone2.Center);
                        if (distance <= zone1.Radius + zone2.Radius)
                        {
                            mergedZoneIds.Add(zone1.EntityId);
                            mergedZoneIds.Add(zone2.EntityId);
                        }
                    }
                }
            }

            // Create messages with merged status
            foreach (var kvp in playerZones)
            {
                zoneMessage.Zones.Add(new ZoneData
                {
                    Center = kvp.Value.Center,
                    Radius = kvp.Value.Radius,
                    PlayerId = kvp.Key,
                    IsActive = true, // Current zone is always active
                    IsMerged = mergedZoneIds.Contains(kvp.Key)
                });
            }

            byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(zoneMessage);
            MyAPIGateway.Multiplayer.SendMessageToOthers(32001, messageBytes);
        }
    }
}