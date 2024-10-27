using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using RealGasGiants;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidZone;
using static DynamicAsteroids.Data.Scripts.DynamicAsteroids.MainSession;


namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    public class AsteroidZone {
        public Vector3D Center { get; set; }
        public double Radius { get; set; }
        public int AsteroidCount { get; set; }
        public bool IsMerged { get; set; }
        public long EntityId { get; set; }
        public HashSet<long> ContainedAsteroids { get; private set; }
        public bool IsMarkedForRemoval { get; set; }
        public DateTime LastActiveTime { get; set; }
        public double CurrentSpeed { get; set; } // Add this property

        public HashSet<long> TransferringAsteroids { get; private set; } = new HashSet<long>();
        public DateTime CreationTime { get; private set; } = DateTime.UtcNow;
        public HashSet<long> TransferredFromOtherZone { get; private set; } = new HashSet<long>();

        public int TotalAsteroidCount => ContainedAsteroids.Count + TransferredFromOtherZone.Count;

        public const double MINIMUM_ZONE_LIFETIME = 5.0; // seconds

        private const float ASTEROID_GRACE_PERIOD = 5f; // seconds


        public AsteroidZone(Vector3D center, double radius) {
            Center = center;
            Radius = radius;
            AsteroidCount = 0;
            IsMerged = false;
            EntityId = 0;
            ContainedAsteroids = new HashSet<long>();
            IsMarkedForRemoval = false;
            LastActiveTime = DateTime.UtcNow;
            CurrentSpeed = 0;
        }

        public enum ZoneState {
            Active,
            PendingRemoval,
            Removed
        }

        public ZoneState State { get; set; } = ZoneState.Active; // Make setter public

        // Add tracking for removal transition
        public DateTime? RemovalStartTime { get; private set; }
        public const double REMOVAL_TRANSITION_TIME = 5.0; // seconds

        public void MarkForRemoval() {
            if (State == ZoneState.Active) {
                State = ZoneState.PendingRemoval;
                RemovalStartTime = DateTime.UtcNow;
                IsMarkedForRemoval = true;
            }
        }

        public bool IsPointInZone(Vector3D point) {
            return Vector3D.DistanceSquared(Center, point) <= Radius * Radius;
        }

        public bool CanBeRemoved() {
            return (DateTime.UtcNow - CreationTime).TotalSeconds >= MINIMUM_ZONE_LIFETIME;
        }

        public bool IsDisabledDueToSpeed() {
            return CurrentSpeed > AsteroidSettings.ZoneSpeedThreshold;
        }
    }

    public class AsteroidSpawner {
        private ConcurrentBag<AsteroidEntity> _asteroids;
        private bool _canSpawnAsteroids = false;
        private DateTime _worldLoadTime;
        private Random rand;
        private List<AsteroidState> _despawnedAsteroids = new List<AsteroidState>();
        private ConcurrentQueue<AsteroidNetworkMessage> _networkMessages = new ConcurrentQueue<AsteroidNetworkMessage>();
        public ConcurrentDictionary<long, AsteroidZone> playerZones = new ConcurrentDictionary<long, AsteroidZone>();
        private ConcurrentDictionary<long, PlayerMovementData> playerMovementData = new ConcurrentDictionary<long, PlayerMovementData>();

        private ConcurrentQueue<AsteroidEntity> _updateQueue = new ConcurrentQueue<AsteroidEntity>();
        private const int UpdatesPerTick = 50;// update rate of the roids

        private ConcurrentQueue<long> _pendingRemovals = new ConcurrentQueue<long>();
        private const int REMOVAL_BATCH_SIZE = 10;
        private const double REMOVAL_BATCH_INTERVAL = 1.0; // seconds
        private DateTime _lastRemovalBatch = DateTime.MinValue;

        private RealGasGiantsApi _realGasGiantsApi;

        private Dictionary<long, DateTime> _newAsteroidTimestamps = new Dictionary<long, DateTime>(); //JUST ONE MORE BRO JUST ONE MORE DICTIONARY WE GOTTA STORE THE DATA BRO WE MIGHT FORGET!!!
        private const double NEW_ASTEROID_GRACE_PERIOD = 5.0; // seconds

        private class ZoneCache {
            public AsteroidZone Zone { get; set; }
            public DateTime LastUpdateTime { get; set; }
            const int CacheExpirationSeconds = 5;

            public bool IsExpired() {
                return (DateTime.UtcNow - LastUpdateTime).TotalSeconds > CacheExpirationSeconds;
            }
        }

        private class AsteroidStateCache {
            private ConcurrentBag<long> _dirtyStates = new ConcurrentBag<long>();
            private const int SaveInterval = 300;
            private DateTime _lastSaveTime = DateTime.UtcNow;

            private ConcurrentDictionary<long, AsteroidState> _stateCache = new ConcurrentDictionary<long, AsteroidState>();
            private ConcurrentBag<long> _dirtyAsteroids = new ConcurrentBag<long>();
            private const double POSITION_CHANGE_THRESHOLD = 1;
            private const double ROTATION_CHANGE_THRESHOLD = 0.01; // Radians
            private const double ACCELERATION_THRESHOLD = 0.1;     // m/s²


            public void UpdateState(AsteroidEntity asteroid) {
                if (asteroid == null || asteroid.Physics == null)
                    return;

                Vector3D currentPosition = asteroid.PositionComp.GetPosition();
                Vector3D currentVelocity = asteroid.Physics.LinearVelocity;
                Vector3D currentAngularVelocity = asteroid.Physics.AngularVelocity;
                Quaternion currentRotation = Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix);

                lock (_stateCache) {
                    AsteroidState cachedState;
                    if (_stateCache.TryGetValue(asteroid.EntityId, out cachedState)) {
                        bool needsUpdate = false;

                        // Check acceleration (velocity change)
                        if (cachedState.Velocity != null) {
                            Vector3D acceleration = (currentVelocity - cachedState.Velocity) / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
                            if (acceleration.LengthSquared() > ACCELERATION_THRESHOLD * ACCELERATION_THRESHOLD) {
                                needsUpdate = true;
                                Log.Info($"Asteroid {asteroid.EntityId} updating due to acceleration: {acceleration.Length():F2} m/s²");
                            }
                        }

                        // Check rotation change
                        float rotationDiff = GetQuaternionAngleDifference(currentRotation, cachedState.Rotation);
                        if (rotationDiff > ROTATION_CHANGE_THRESHOLD) {
                            needsUpdate = true;
                            Log.Info($"Asteroid {asteroid.EntityId} updating due to rotation change: {MathHelper.ToDegrees(rotationDiff):F2}°");
                        }

                        // Still check position for safety but with larger threshold
                        double positionDeltaSquared = Vector3D.DistanceSquared(cachedState.Position, currentPosition);
                        if (positionDeltaSquared > POSITION_CHANGE_THRESHOLD * POSITION_CHANGE_THRESHOLD * 4) {
                            needsUpdate = true;
                            Log.Info($"Asteroid {asteroid.EntityId} updating due to position change: {Math.Sqrt(positionDeltaSquared):F2}m");
                        }

                        if (needsUpdate) {
                            _stateCache[asteroid.EntityId] = new AsteroidState(asteroid);
                            _dirtyAsteroids.Add(asteroid.EntityId);
                        }
                    }
                    else {
                        _stateCache[asteroid.EntityId] = new AsteroidState(asteroid);
                        _dirtyAsteroids.Add(asteroid.EntityId);
                        Log.Info($"Initial state cache for asteroid {asteroid.EntityId}");
                    }
                }
            }

            private float GetQuaternionAngleDifference(Quaternion a, Quaternion b) {
                float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
                dot = MathHelper.Clamp(dot, -1f, 1f);
                return 2f * (float)Math.Acos(Math.Abs(dot));
            }

            public List<AsteroidState> GetDirtyAsteroids() {
                return _dirtyAsteroids.Select(id => _stateCache[id]).ToList();
            }

            public AsteroidState GetState(long asteroidId) {
                AsteroidState state;
                _stateCache.TryGetValue(asteroidId, out state);
                return state;
            }

            public List<AsteroidState> GetDirtyStates() {
                return _stateCache.Where(kvp => _dirtyStates.Contains(kvp.Key))
                    .Select(kvp => kvp.Value)
                    .ToList();
            }

            public void ClearDirtyStates() {
                long id;
                while (_dirtyAsteroids.TryTake(out id)) { }
            }

            public bool ShouldSave() {
                return (DateTime.UtcNow - _lastSaveTime).TotalSeconds >= SaveInterval && !_dirtyStates.IsEmpty;
            }


        }

        private class NetworkMessageCache {
            private ConcurrentDictionary<long, AsteroidNetworkMessage> _messageCache = new ConcurrentDictionary<long, AsteroidNetworkMessage>();
            private ConcurrentQueue<AsteroidNetworkMessage> _messageQueue = new ConcurrentQueue<AsteroidNetworkMessage>();

            public void Clear() {
                _messageCache.Clear();
                AsteroidNetworkMessage message;
                while (_messageQueue.TryDequeue(out message)) { }
            }

            public void AddMessage(AsteroidNetworkMessage message) {
                // Only add if we haven't sent this one recently
                if (_messageCache.TryAdd(message.EntityId, message)) {
                    _messageQueue.Enqueue(message);
                }
            }

            public void ProcessMessages(Action<AsteroidNetworkMessage> sendAction) {
                int processedCount = 0;
                const int MAX_MESSAGES_PER_UPDATE = 50;

                AsteroidNetworkMessage message;
                while (processedCount < MAX_MESSAGES_PER_UPDATE && _messageQueue.TryDequeue(out message)) {
                    // Remove from cache when processing
                    AsteroidNetworkMessage removed;
                    _messageCache.TryRemove(message.EntityId, out removed);
                    sendAction(message);
                    processedCount++;
                }

                if (_messageQueue.Count > MAX_MESSAGES_PER_UPDATE * 2) {
                    Log.Info($"Clearing {_messageQueue.Count} pending messages due to backup");
                    Clear();
                }
            }
        }


        private ConcurrentDictionary<long, ZoneCache> _zoneCache = new ConcurrentDictionary<long, ZoneCache>();
        private AsteroidStateCache _stateCache = new AsteroidStateCache();
        private NetworkMessageCache _messageCache = new NetworkMessageCache();

        private ZoneNetworkManager _networkManager;

        public AsteroidSpawner(RealGasGiantsApi realGasGiantsApi) {
            _realGasGiantsApi = realGasGiantsApi;
            _networkManager = new ZoneNetworkManager();
        }


        public void Init(int seed) {
            if (!MyAPIGateway.Session.IsServer) return;
            Log.Info("Initializing AsteroidSpawner");
            _asteroids = new ConcurrentBag<AsteroidEntity>();
            _worldLoadTime = DateTime.UtcNow;
            rand = new Random(seed);
            AsteroidSettings.Seed = seed;
        }
        public void Close() {
            if (!MyAPIGateway.Session.IsServer)
                return;

            try {
                Log.Info("Closing AsteroidSpawner");
                _zoneCache.Clear();
                playerZones.Clear();
                playerMovementData.Clear();

                if (_asteroids != null) {
                    foreach (AsteroidEntity asteroid in _asteroids) {
                        try {
                            MyEntities.Remove(asteroid);
                            asteroid.Close();
                        }
                        catch (Exception ex) {
                            Log.Exception(ex, typeof(AsteroidSpawner), "Error closing individual asteroid");
                        }
                    }
                    _asteroids = null;
                }

                _updateQueue = new ConcurrentQueue<AsteroidEntity>();
                _networkMessages = new ConcurrentQueue<AsteroidNetworkMessage>();
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in Close method");
            }
        }

        private bool AreAsteroidsEnabled() {
            return AsteroidSettings.EnableGasGiantRingSpawning ||
                   (AsteroidSettings.ValidSpawnLocations != null &&
                    AsteroidSettings.ValidSpawnLocations.Count > 0);
        }

        #region Player and Zone Management
        private void AssignZonesToPlayers() {
            // Early exit if spawning disabled
            if (!AreAsteroidsEnabled()) {
                playerZones.Clear();
                return;
            }

            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players) {
                Vector3D playerPosition = player.GetPosition();

                // Skip invalid positions
                if (Vector3D.IsZero(playerPosition) || double.IsNaN(playerPosition.X) ||
                    double.IsNaN(playerPosition.Y) || double.IsNaN(playerPosition.Z)) {
                    continue;
                }

                PlayerMovementData data;
                if (playerMovementData.TryGetValue(player.IdentityId, out data)) {
                    if (data.Speed > AsteroidSettings.ZoneSpeedThreshold) {
                        Log.Info($"Player {player.DisplayName} moving too fast ({data.Speed:F2} m/s), skipping zone assignment.");
                        continue;
                    }
                }

                // Don't create new zones if spawning is disabled
                if (!AreAsteroidsEnabled()) {
                    continue;
                }

                AsteroidZone existingZone;
                if (playerZones.TryGetValue(player.IdentityId, out existingZone)) {
                    existingZone.LastActiveTime = DateTime.UtcNow;
                    if (!existingZone.IsPointInZone(playerPosition)) {
                        existingZone.MarkForRemoval();
                        if (existingZone.State == ZoneState.Removed)
                        {
                            AsteroidZone removedZone;
                            playerZones.TryRemove(player.IdentityId, out removedZone);
                        }
                    }
                }
            }
        }
        public void AssignMergedZonesToPlayers(List<AsteroidZone> mergedZones) {
            const double REASSIGNMENT_THRESHOLD = 100.0; // Distance threshold for zone reassignment
            const double OVERLAP_BUFFER = 50.0; // Buffer distance for handling zone overlaps

            try {
                // Get current player states
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                var newPlayerZones = new ConcurrentDictionary<long, AsteroidZone>();
                var zoneAssignments = new ConcurrentDictionary<AsteroidZone, HashSet<long>>();

                // First pass: Initial zone assignment based on closest zones
                foreach (IMyPlayer player in players) {
                    Vector3D playerPos = player.GetPosition();
                    PlayerMovementData movementData;

                    // Get or create movement data for velocity checks
                    if (!playerMovementData.TryGetValue(player.IdentityId, out movementData)) {
                        movementData = new PlayerMovementData {
                            LastPosition = playerPos,
                            LastUpdateTime = DateTime.UtcNow,
                            Speed = 0
                        };
                        playerMovementData[player.IdentityId] = movementData;
                    }

                    // Skip high-speed players
                    if (IsPlayerMovingTooFast(player)) {
                        continue;
                    }
                    // Find closest valid zone
                    AsteroidZone bestZone = null;
                    double bestDistance = double.MaxValue;

                    foreach (AsteroidZone zone in mergedZones) {
                        double distance = Vector3D.Distance(playerPos, zone.Center);
                        if (!(distance < bestDistance) || !(distance <= zone.Radius + OVERLAP_BUFFER)) continue;
                        bestDistance = distance;
                        bestZone = zone;
                    }

                    // Handle zone assignment
                    if (bestZone == null) continue;
                    // Check if player should keep their existing zone
                    AsteroidZone currentZone;
                    if (playerZones.TryGetValue(player.IdentityId, out currentZone)) {
                        double distanceToCurrentZone = Vector3D.Distance(playerPos, currentZone.Center);
                        if (distanceToCurrentZone <= currentZone.Radius &&
                            distanceToCurrentZone <= bestDistance + REASSIGNMENT_THRESHOLD) {
                            bestZone = currentZone;
                        }
                    }
                    newPlayerZones[player.IdentityId] = bestZone;

                    // Track zone assignments for overlap handling
                    if (!zoneAssignments.ContainsKey(bestZone)) {
                        zoneAssignments[bestZone] = new HashSet<long>();
                    }
                    zoneAssignments[bestZone].Add(player.IdentityId);
                }

                // Second pass: Handle overlapping zones and optimize assignments
                foreach (var zoneAssignment in zoneAssignments) {
                    AsteroidZone zone = zoneAssignment.Key;
                    var playerIds = zoneAssignment.Value;
                    if (playerIds.Count > AsteroidSettings.MaxPlayersPerZone) {
                        OptimizeZoneAssignments(zone, playerIds, newPlayerZones, mergedZones);
                    }
                }

                // Final pass: Update zone properties and handle teleportation
                foreach (var kvp in newPlayerZones) {
                    long playerId = kvp.Key;
                    AsteroidZone newZone = kvp.Value;
                    AsteroidZone oldZone;

                    if (playerZones.TryGetValue(playerId, out oldZone)) {
                        // Handle potential teleportation
                        IMyPlayer player = players.FirstOrDefault(p => p.IdentityId == playerId);
                        if (player != null) {
                            Vector3D playerPos = player.GetPosition();
                            double distanceToOldZone = Vector3D.Distance(playerPos, oldZone.Center);
                            double distanceToNewZone = Vector3D.Distance(playerPos, newZone.Center);

                            if (distanceToNewZone > distanceToOldZone * 2) {
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
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in AssignMergedZonesToPlayers");
            }
        }
        private void HandlePlayerTeleportation(IMyPlayer player, AsteroidZone newZone, AsteroidZone oldZone) {
            Vector3D playerPos = player.GetPosition();
            playerMovementData[player.IdentityId] = new PlayerMovementData {
                LastPosition = playerPos,
                LastUpdateTime = DateTime.UtcNow,
                Speed = 0
            };

            if (oldZone != null) {
                // Check for asteroids that might get orphaned
                var potentialOrphans = _asteroids.Where(a =>
                    oldZone.IsPointInZone(a.PositionComp.GetPosition()) &&
                    !newZone.IsPointInZone(a.PositionComp.GetPosition())).ToList();

                foreach (var asteroid in potentialOrphans) {
                    Log.Info($"Handling asteroid {asteroid.EntityId} during teleport transition");

                    // Either transfer to new zone or clean up
                    if (Vector3D.Distance(asteroid.PositionComp.GetPosition(), newZone.Center) <= newZone.Radius * 1.5) {
                        newZone.ContainedAsteroids.Add(asteroid.EntityId);
                        Log.Info($"Transferred asteroid {asteroid.EntityId} to new zone");
                    }
                    else {
                        RemoveAsteroid(asteroid);
                        Log.Info($"Removed asteroid {asteroid.EntityId} during zone transition");
                    }
                }
            }

            if (!playerZones.Values.Contains(oldZone)) {
                CleanupZone(oldZone);
            }

            Log.Info($"Player {player.DisplayName} teleported from {oldZone?.Center} to {newZone.Center}");
        }
        private void OptimizeZoneAssignments(AsteroidZone zone, HashSet<long> playerIds, ConcurrentDictionary<long, AsteroidZone> newPlayerZones, List<AsteroidZone> mergedZones) {
            // Retrieve the list of all players
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            // Sort players by distance to zone center
            var playerDistances = playerIds
                .Select(id => players.FirstOrDefault(p => p.IdentityId == id))
                .Where(player => player != null)
                .Select(player => new {
                    PlayerId = player.IdentityId,
                    Distance = Vector3D.Distance(player.GetPosition(), zone.Center)
                })
                .OrderBy(p => p.Distance)
                .ToList();

            // Keep closest players up to max limit
            for (int i = AsteroidSettings.MaxPlayersPerZone; i < playerDistances.Count; i++) {
                var playerId = playerDistances[i].PlayerId;

                // Find next best zone for overflow players
                IMyPlayer player = players.FirstOrDefault(p => p.IdentityId == playerId);
                if (player == null) continue;
                AsteroidZone alternateZone = FindAlternateZone(player.GetPosition(), zone, mergedZones);
                if (alternateZone != null) {
                    newPlayerZones[playerId] = alternateZone;
                }
            }
        }
        private AsteroidZone FindAlternateZone(Vector3D position, AsteroidZone excludeZone, List<AsteroidZone> mergedZones) {
            return mergedZones
                .Where(z => z != excludeZone && Vector3D.Distance(position, z.Center) <= z.Radius)
                .OrderBy(z => Vector3D.Distance(position, z.Center))
                .FirstOrDefault();
        }
        #endregion

        #region Zone Updates
        public void MergeZones() {
            List<AsteroidZone> mergedZones = new List<AsteroidZone>();
            foreach (AsteroidZone zone in playerZones.Values) {
                bool merged = false;
                foreach (AsteroidZone mergedZone in mergedZones) {
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
                if (!merged) {
                    mergedZones.Add(new AsteroidZone(zone.Center, zone.Radius) { AsteroidCount = zone.AsteroidCount });
                }
            }
            AssignMergedZonesToPlayers(mergedZones);
        }
        public void UpdateZones() {
            if (!AreAsteroidsEnabled())
                return;

            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            Dictionary<long, AsteroidZone> updatedZones = new Dictionary<long, AsteroidZone>();
            HashSet<AsteroidZone> oldZones = new HashSet<AsteroidZone>(playerZones.Values);

            foreach (IMyPlayer player in players) {
                if (player?.Character == null) continue;

                Vector3D playerPosition = player.GetPosition();
                PlayerMovementData data;
                if (playerMovementData.TryGetValue(player.IdentityId, out data)) {
                    if (AsteroidSettings.DisableZoneWhileMovingFast && IsPlayerMovingTooFast(player)) {
                        continue;
                    }
                }

                bool playerInZone = false;
                foreach (AsteroidZone zone in playerZones.Values) {
                    if (zone.IsPointInZone(playerPosition)) {
                        playerInZone = true;
                        oldZones.Remove(zone);
                        updatedZones[player.IdentityId] = zone;
                        zone.Center = playerPosition; // Update zone position to follow player
                        break;
                    }
                }

                if (!playerInZone) {
                    updatedZones[player.IdentityId] = new AsteroidZone(playerPosition, AsteroidSettings.ZoneRadius);
                }
            }

            foreach (var oldZone in oldZones) {
                CleanupZone(oldZone);
            }

            playerZones = new ConcurrentDictionary<long, AsteroidZone>(updatedZones);
        }
        private void CleanupUnassignedZones() {
            var assignedZones = new HashSet<AsteroidZone>(playerZones.Values);
            foreach (AsteroidZone zone in playerZones.Values.ToList()) {
                if (!assignedZones.Contains(zone)) {
                    CleanupZone(zone);
                }
            }
        }
        private void CleanupZone(AsteroidZone zone) {
            if (zone == null) return;

            var asteroidsToRemove = _asteroids.Where(a =>
                a != null &&
                !a.MarkedForClose &&
                zone.IsPointInZone(a.PositionComp.GetPosition())
            ).ToList();

            if (asteroidsToRemove.Count > 0) {
                Log.Info($"Cleaning up {asteroidsToRemove.Count} asteroids from abandoned zone at {zone.Center}");
                foreach (var asteroid in asteroidsToRemove) {
                    RemoveAsteroid(asteroid);
                }
            }
        }
        public void SendZoneUpdates() {
            if (!MyAPIGateway.Session.IsServer)
                return;

            // Don't send zone updates if spawning is disabled
            if (!AreAsteroidsEnabled()) {
                return;
            }

            var zonePacket = new ZoneUpdatePacket();
            var mergedZoneIds = new HashSet<long>();

            // Find merged zones
            foreach (var zone1 in playerZones.Values) {
                foreach (var zone2 in playerZones.Values) {
                    if (zone1 != zone2) {
                        double distance = Vector3D.Distance(zone1.Center, zone2.Center);
                        if (distance <= zone1.Radius + zone2.Radius) {
                            mergedZoneIds.Add(zone1.EntityId);
                            mergedZoneIds.Add(zone2.EntityId);
                        }
                    }
                }
            }

            // Create zone data
            foreach (var kvp in playerZones) {
                zonePacket.Zones.Add(new ZoneData {
                    Center = kvp.Value.Center,
                    Radius = kvp.Value.Radius,
                    PlayerId = kvp.Key,
                    IsActive = true,
                    IsMerged = mergedZoneIds.Contains(kvp.Key),
                    CurrentSpeed = kvp.Value.CurrentSpeed
                });
            }

            // Serialize and send
            byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(zonePacket);
            MyAPIGateway.Multiplayer.SendMessageToOthers(32001, messageBytes);
        }
        #endregion

        #region Asteroid Management

        public IEnumerable<AsteroidEntity> GetAsteroids() {
            return _asteroids;
        }
        public void AddAsteroid(AsteroidEntity asteroid) {
            _asteroids.Add(asteroid);
            _newAsteroidTimestamps[asteroid.EntityId] = DateTime.UtcNow;
            Log.Info($"Added new asteroid {asteroid.EntityId} with grace period");
        }
        public bool TryRemoveAsteroid(AsteroidEntity asteroid) {
            return _asteroids.TryTake(out asteroid);
        }
        private void RemoveAsteroid(AsteroidEntity asteroid) {
            if (asteroid == null) return;

            try {
                if (MyAPIGateway.Session.IsServer) {
                    _pendingRemovals.Enqueue(asteroid.EntityId);
                }

                MyEntities.Remove(asteroid);
                asteroid.Close();

                AsteroidEntity removed;
                _asteroids.TryTake(out removed);
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), $"Error removing asteroid {asteroid?.EntityId}");
            }
        }
        public void SpawnAsteroids(List<AsteroidZone> zones) {
            if (!MyAPIGateway.Session.IsServer) return; // Ensure only server handles spawning

            if (!AreAsteroidsEnabled())
                return;

            int totalSpawnAttempts = 0;
            if (AsteroidSettings.MaxAsteroidCount == 0) {
                Log.Info("Asteroid spawning is disabled.");
                return;
            }

            int totalAsteroidsSpawned = 0;
            int totalZoneSpawnAttempts = 0;
            List<Vector3D> skippedPositions = new List<Vector3D>();
            List<Vector3D> spawnedPositions = new List<Vector3D>();

            UpdatePlayerMovementData();

            foreach (AsteroidZone zone in zones) {
                int asteroidsSpawned = 0;
                int zoneSpawnAttempts = 0;

                Log.Info($"Attempting spawn in zone at {zone.Center}, current count: {zone.TotalAsteroidCount}/{AsteroidSettings.MaxAsteroidsPerZone}");

                if (zone.TotalAsteroidCount >= AsteroidSettings.MaxAsteroidsPerZone) {
                    Log.Info($"Zone at {zone.Center} is at capacity ({zone.TotalAsteroidCount} asteroids)");
                    continue;
                }

                bool skipSpawning = false;
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                foreach (IMyPlayer player in players) {
                    Vector3D playerPosition = player.GetPosition();
                    if (!zone.IsPointInZone(playerPosition)) continue;
                    PlayerMovementData data;
                    if (!playerMovementData.TryGetValue(player.IdentityId, out data)) continue;
                    if (IsPlayerMovingTooFast(player)) {
                        skipSpawning = true;
                        Log.Info($"Skipping asteroid spawning for player {player.DisplayName} due to high speed: {data.Speed:F2} m/s > {AsteroidSettings.ZoneSpeedThreshold} m/s");
                        break;
                    }
                }

                if (skipSpawning) {
                    continue;
                }

                while (zone.AsteroidCount < AsteroidSettings.MaxAsteroidsPerZone &&
                      asteroidsSpawned < 10 &&
                      zoneSpawnAttempts < AsteroidSettings.MaxZoneAttempts &&
                      totalSpawnAttempts < AsteroidSettings.MaxTotalAttempts) {
                    Vector3D newPosition;
                    bool isInRing = false;
                    bool validPosition = false;
                    float ringInfluence = 0f;

                    do {
                        newPosition = zone.Center + RandVector() * AsteroidSettings.ZoneRadius;
                        zoneSpawnAttempts++;
                        totalSpawnAttempts++;
                        //Log.Info($"Attempting to spawn asteroid at {newPosition} (attempt {totalSpawnAttempts})");

                        if (AsteroidSettings.EnableGasGiantRingSpawning && _realGasGiantsApi != null && _realGasGiantsApi.IsReady) {
                            ringInfluence = _realGasGiantsApi.GetRingInfluenceAtPositionGlobal(newPosition);
                            if (ringInfluence > AsteroidSettings.MinimumRingInfluenceForSpawn) {
                                validPosition = true;
                                isInRing = true;
                                Log.Info($"Position {newPosition} is within a gas giant ring (influence: {ringInfluence})");
                            }
                        }

                        if (!isInRing) {
                            validPosition = IsValidSpawnPosition(newPosition, zones);
                        }

                    } while (!validPosition &&
                            zoneSpawnAttempts < AsteroidSettings.MaxZoneAttempts &&
                            totalSpawnAttempts < AsteroidSettings.MaxTotalAttempts);

                    if (zoneSpawnAttempts >= AsteroidSettings.MaxZoneAttempts || totalSpawnAttempts >= AsteroidSettings.MaxTotalAttempts)
                        break;

                    Vector3D newVelocity;
                    if (!AsteroidSettings.CanSpawnAsteroidAtPoint(newPosition, out newVelocity, isInRing)) {
                        //Log.Info($"Cannot spawn asteroid at {newPosition}, skipping.");
                        continue;
                    }

                    if (IsNearVanillaAsteroid(newPosition)) {
                        Log.Info($"Position {newPosition} is near a vanilla asteroid, skipping.");
                        skippedPositions.Add(newPosition);
                        continue;
                    }

                    if (AsteroidSettings.MaxAsteroidCount != -1 && _asteroids.Count >= AsteroidSettings.MaxAsteroidCount) {
                        Log.Warning($"Maximum asteroid count of {AsteroidSettings.MaxAsteroidCount} reached. No more asteroids will be spawned until existing ones are removed.");
                        return;
                    }

                    if (zone.AsteroidCount >= AsteroidSettings.MaxAsteroidsPerZone) {
                        Log.Info($"Zone at {zone.Center} has reached its maximum asteroid count ({AsteroidSettings.MaxAsteroidsPerZone}). Skipping further spawning in this zone.");
                        break;
                    }

                    float spawnChance = isInRing ?
                        MathHelper.Lerp(0.1f, 1f, ringInfluence) * AsteroidSettings.MaxRingAsteroidDensityMultiplier :
                        1f;

                    if (MainSession.I.Rand.NextDouble() > spawnChance) {
                        Log.Info($"Asteroid spawn skipped due to density scaling (spawn chance: {spawnChance})");
                        continue;
                    }

                    AsteroidType type = AsteroidSettings.GetAsteroidType(newPosition);
                    float size = AsteroidSettings.GetAsteroidSize(newPosition);

                    if (isInRing) {
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
            if (skippedPositions.Count > 0) {
                Log.Info($"Skipped spawning asteroids due to proximity to vanilla asteroids. Positions: {string.Join(", ", skippedPositions.Select(p => p.ToString()))}");
            }
            if (spawnedPositions.Count > 0) {
                Log.Info($"Spawned asteroids at positions: {string.Join(", ", spawnedPositions.Select(p => p.ToString()))}");
            }
        }
        private bool IsNearVanillaAsteroid(Vector3D position) {
            BoundingSphereD sphere = new BoundingSphereD(position, AsteroidSettings.MinDistanceFromVanillaAsteroids);
            List<MyEntity> entities = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Static);
            foreach (MyEntity entity in entities) {
                IMyVoxelMap voxelMap = entity as IMyVoxelMap;
                if (voxelMap == null || voxelMap.StorageName.StartsWith("mod_")) continue;
                Log.Info($"Position {position} is near vanilla asteroid {voxelMap.StorageName}");
                return true;
            }
            return false;
        }
        private bool IsValidSpawnPosition(Vector3D position, List<AsteroidZone> zones) {
            // Check for nearby players
            BoundingSphereD sphere = new BoundingSphereD(position, AsteroidSettings.MinDistanceFromPlayer);
            List<MyEntity> entities = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Both);
            foreach (MyEntity entity in entities) {
                if (entity is IMyCharacter || entity is IMyShipController) {
                    return false;
                }
            }

            // Check gas giant rings
            if (AsteroidSettings.EnableGasGiantRingSpawning && _realGasGiantsApi != null && _realGasGiantsApi.IsReady) {
                float ringInfluence = _realGasGiantsApi.GetRingInfluenceAtPositionGlobal(position);
                if (ringInfluence > AsteroidSettings.MinimumRingInfluenceForSpawn) {
                    return true;
                }
            }

            // Check spawnable areas
            foreach (AsteroidSettings.SpawnableArea area in AsteroidSettings.ValidSpawnLocations) {
                if (area.ContainsPoint(position))
                    return true;
            }

            // Check zones and their asteroid counts
            foreach (AsteroidZone zone in zones) {
                if (zone.IsPointInZone(position)) {
                    // Don't spawn if zone is at or over limit
                    if (zone.TotalAsteroidCount >= AsteroidSettings.MaxAsteroidsPerZone) {
                        Log.Info($"Zone at {zone.Center} is at capacity ({zone.TotalAsteroidCount} asteroids)");
                        return false;
                    }
                    return true;
                }
            }

            return false;
        }
        private void CleanupOrphanedAsteroids() {
            if (_isCleanupRunning) return;
            _isCleanupRunning = true;

            try {
                var currentZones = playerZones.Values.ToList();
                var asteroidsToRemove = new List<AsteroidEntity>();
                var trackedAsteroids = _asteroids.ToList();

                foreach (var asteroid in trackedAsteroids) {
                    if (asteroid == null || asteroid.MarkedForClose) continue;

                    Vector3D asteroidPosition = asteroid.PositionComp.GetPosition();
                    bool isInAnyZone = false;

                    foreach (var zone in currentZones) {
                        if (zone.IsPointInZone(asteroidPosition)) {
                            isInAnyZone = true;
                            break;
                        }
                    }

                    if (!isInAnyZone) {
                        Log.Info($"Found orphaned asteroid {asteroid.EntityId} at {asteroidPosition}");
                        asteroidsToRemove.Add(asteroid);
                    }
                }

                if (asteroidsToRemove.Count > 0) {
                    Log.Info($"Cleaning up {asteroidsToRemove.Count} orphaned asteroids");
                    foreach (var asteroid in asteroidsToRemove) {
                        RemoveAsteroid(asteroid);
                    }
                }

                _lastCleanupTime = DateTime.UtcNow;
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in cleanup");
            }
            finally {
                _isCleanupRunning = false;
            }
        }

        #endregion

        #region Main Update and Cleanup Cycle

        private int _spawnIntervalTimer = 0;
        private int _updateIntervalTimer = 0;

        private DateTime _lastCleanupTime = DateTime.MinValue;
        private bool _isCleanupRunning = false;
        private const double CLEANUP_COOLDOWN_SECONDS = 10.0; // Minimum time between cleanups


        public void UpdateTick() {
            if (!MyAPIGateway.Session.IsServer)
                return;

            // Skip all processing if spawning is disabled
            bool spawningEnabled = (AreAsteroidsEnabled());

            if (!spawningEnabled) {
                if (playerZones.Count > 0) {
                    playerZones.Clear();
                    Log.Info("Spawning disabled - cleared all zones");
                }
                if (_asteroids.Count > 0) {
                    foreach (var asteroid in _asteroids.ToList()) {
                        RemoveAsteroid(asteroid);
                    }
                    Log.Info("Spawning disabled - cleared all asteroids");
                }
                return;
            }

            try {
                AssignZonesToPlayers();

                // Only process zones if we have any
                if (playerZones.Count > 0) {
                    MergeZones();
                    UpdateZones();
                    SendZoneUpdates();
                }

                // Only process network messages if we have active zones and asteroids
                if (playerZones.Count > 0 && _asteroids.Count > 0) {
                    _networkMessageTimer++;
                    if (_networkMessageTimer >= AsteroidSettings.NetworkUpdateInterval) {
                        SendNetworkMessages();
                        _networkMessageTimer = 0;
                    }
                }

                // Only process spawning if enabled
                if (spawningEnabled) {
                    if (_spawnIntervalTimer <= 0) {
                        SpawnAsteroids(playerZones.Values.ToList());
                        _spawnIntervalTimer = AsteroidSettings.SpawnInterval;
                    }
                    else {
                        _spawnIntervalTimer--;
                    }
                }

                // Regular maintenance
                if (_updateIntervalTimer % 600 == 0) {
                    ValidateAsteroidTracking();
                }

                if (!_isCleanupRunning &&
                    (DateTime.UtcNow - _lastCleanupTime).TotalSeconds >= CLEANUP_COOLDOWN_SECONDS) {
                    if (_asteroids.Count > 0) {
                        CleanupOrphanedAsteroids();
                    }
                }

                ProcessPendingRemovals();
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in UpdateTick");
            }
        }

        #endregion

        #region Network Management

        private int _networkMessageTimer = 0;

        public void SendNetworkMessages() {
            if (!MyAPIGateway.Session.IsServer || !MyAPIGateway.Utilities.IsDedicated)
                return;

            // Skip if spawning is disabled or no active content
            bool spawningEnabled = AreAsteroidsEnabled();

            if (!spawningEnabled || playerZones.Count == 0 || _asteroids.Count == 0) {
                return;
            }

            try {
                ProcessImmediateMessages();

                if (_networkMessageTimer >= AsteroidSettings.NetworkUpdateInterval) {
                    var playerPositions = GetPlayerPositions();
                    if (playerPositions.Count > 0) {
                        _networkManager.UpdateZoneAwareness(playerPositions, playerZones);
                        ProcessBatchedUpdates();
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error sending network messages");
            }
        }
        private void ProcessPendingRemovals() {
            if ((DateTime.UtcNow - _lastRemovalBatch).TotalSeconds < REMOVAL_BATCH_INTERVAL)
                return;

            var removalBatch = new List<long>();
            long entityId;

            while (removalBatch.Count < REMOVAL_BATCH_SIZE && _pendingRemovals.TryDequeue(out entityId)) {
                removalBatch.Add(entityId);
            }

            if (removalBatch.Count > 0) {
                var packet = new AsteroidBatchUpdatePacket { Removals = removalBatch };
                byte[] data = MyAPIGateway.Utilities.SerializeToBinary(packet);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, data);
                _lastRemovalBatch = DateTime.UtcNow;
            }
        }
        private bool ShouldUpdateAsteroid(AsteroidEntity asteroid, Vector3D playerPos) {
            double distance = Vector3D.Distance(asteroid.PositionComp.GetPosition(), playerPos);

            // More aggressive distance-based throttling
            if (distance < 1000) return _networkMessageTimer % 2 == 0;  // Every 2 ticks
            if (distance < 5000) return _networkMessageTimer % 15 == 0; // Every 15 ticks
            if (distance < 10000) return _networkMessageTimer % 30 == 0; // Every 30 ticks
            return _networkMessageTimer % 60 == 0; // Every 60 ticks for distant asteroids
        }
        private void ProcessImmediateMessages() {
            int immediateMessagesSent = 0;
            _messageCache.ProcessMessages(message => {
                if (message.EntityId == 0) {
                    Log.Warning("Attempted to send message for asteroid with ID 0");
                    return;
                }

                AsteroidEntity asteroid = MyEntities.GetEntityById(message.EntityId) as AsteroidEntity;
                if (asteroid == null) {
                    Log.Warning($"Attempted to send update for non-existent asteroid {message.EntityId}");
                    return;
                }

                var state = _stateCache.GetState(asteroid.EntityId);
                if (state != null && !state.HasChanged(asteroid)) {
                    return;
                }

                AsteroidNetworkMessage updateMessage = new AsteroidNetworkMessage(
                    asteroid.PositionComp.GetPosition(),
                    asteroid.Properties.Diameter,
                    asteroid.Physics.LinearVelocity,
                    asteroid.Physics.AngularVelocity,
                    asteroid.Type,
                    false,
                    asteroid.EntityId,
                    false,
                    false,
                    Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix)
                );

                byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(updateMessage);
                if (messageBytes == null || messageBytes.Length == 0) {
                    Log.Warning("Failed to serialize network message");
                    return;
                }

                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                immediateMessagesSent++;
            });

            if (immediateMessagesSent > 0) {
                Log.Info($"Server: Processed {immediateMessagesSent} immediate messages");
            }
        }
        private void ProcessBatchedUpdates() {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            if (players.Count == 0) {
                Log.Info("No players to update, skipping batch update");
                return;
            }

            foreach (AsteroidEntity asteroid in _asteroids) {
                if (asteroid == null || asteroid.MarkedForClose) continue;

                bool shouldUpdate = false;
                foreach (IMyPlayer player in players) {
                    if (ShouldUpdateAsteroid(asteroid, player.GetPosition())) {
                        shouldUpdate = true;
                        break;
                    }
                }

                if (shouldUpdate) {
                    _stateCache.UpdateState(asteroid);
                }
            }

            var dirtyAsteroids = _stateCache.GetDirtyAsteroids();
            if (dirtyAsteroids.Count > 0) {
                // Replace SendBatchedUpdates call with ZoneNetworkManager version
                _networkManager.SendBatchedUpdates(dirtyAsteroids, playerZones);
            }

            _stateCache.ClearDirtyStates();
        }
        #endregion


        #region Player Movement/Speed Management
        private bool IsPlayerMovingTooFast(IMyPlayer player) {
            PlayerMovementData data;
            if (!playerMovementData.TryGetValue(player.IdentityId, out data))
                return false;

            double smoothedSpeed = data.GetSmoothedSpeed();
            bool isTooFast = smoothedSpeed > AsteroidSettings.ZoneSpeedThreshold;

            if (isTooFast) {
                Log.Info($"Player {player.DisplayName} moving too fast: {smoothedSpeed:F2} m/s (threshold: {AsteroidSettings.ZoneSpeedThreshold} m/s)");
            }

            return isTooFast;
        }
        private void UpdatePlayerMovementData() {
            const double MIN_TIME_DELTA = 0.016; // Minimum 1/60th second
            const double MAX_TIME_DELTA = 1.0;   // Maximum 1 second
            const double TELEPORT_THRESHOLD = 1000.0; // Distance in meters that suggests teleportation

            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (IMyPlayer player in players) {
                Vector3D currentPosition = player.GetPosition();
                DateTime currentTime = DateTime.UtcNow;

                PlayerMovementData data;
                if (!playerMovementData.TryGetValue(player.IdentityId, out data)) {
                    data = new PlayerMovementData {
                        LastPosition = currentPosition,
                        LastUpdateTime = currentTime,
                        Speed = 0
                    };
                    playerMovementData[player.IdentityId] = data;
                    continue;
                }

                double timeDelta = (currentTime - data.LastUpdateTime).TotalSeconds;
                timeDelta = MathHelper.Clamp(timeDelta, MIN_TIME_DELTA, MAX_TIME_DELTA);

                double distance = Vector3D.Distance(currentPosition, data.LastPosition);

                // Check for probable teleport
                if (distance > TELEPORT_THRESHOLD) {
                    Log.Info($"Player {player.DisplayName} likely teleported: {distance:F0}m");
                    data.LastPosition = currentPosition;
                    data.LastUpdateTime = currentTime;
                    data.SpeedHistory.Clear(); // Reset history after teleport
                    continue;
                }

                double instantaneousSpeed = distance / timeDelta;
                data.AddSpeedSample(instantaneousSpeed);
                data.Speed = data.GetSmoothedSpeed();

                data.LastPosition = currentPosition;
                data.LastUpdateTime = currentTime;

                // Log only significant speed changes
                if (Math.Abs(instantaneousSpeed - data.Speed) > AsteroidSettings.ZoneSpeedThreshold * 0.1) {
                    Log.Info($"Player {player.DisplayName} speed: {data.Speed:F2} m/s (instant: {instantaneousSpeed:F2} m/s)");
                }
            }
        }
        private class PlayerMovementData {
            public Vector3D LastPosition { get; set; }
            public DateTime LastUpdateTime { get; set; }
            public double Speed { get; set; }
            public Queue<double> SpeedHistory { get; private set; }
            private const int SPEED_HISTORY_LENGTH = 10;
            private const double SPEED_SPIKE_THRESHOLD = 2.0; // Factor above average to consider a spike

            public PlayerMovementData() {
                SpeedHistory = new Queue<double>(SPEED_HISTORY_LENGTH);
            }

            public void AddSpeedSample(double speed) {
                SpeedHistory.Enqueue(speed);
                if (SpeedHistory.Count > SPEED_HISTORY_LENGTH)
                    SpeedHistory.Dequeue();
            }

            public double GetSmoothedSpeed() {
                if (SpeedHistory.Count == 0)
                    return Speed;

                // Calculate average excluding obvious spikes
                double avg = SpeedHistory.Average();
                var validSpeeds = SpeedHistory.Where(s => s <= avg * SPEED_SPIKE_THRESHOLD);
                return validSpeeds.Any() ? validSpeeds.Average() : avg;
            }
        }
        #endregion

        #region Utility Methods
        private double GetDistanceToClosestPlayer(Vector3D position) {
            if (!AreAsteroidsEnabled()) return double.MaxValue; double minDistance = double.MaxValue;
            foreach (AsteroidZone zone in playerZones.Values) {
                double distance = Vector3D.DistanceSquared(zone.Center, position);
                if (distance < minDistance)
                    minDistance = distance;
            }
            return minDistance;
        }
        private Vector3D RandVector() {
            double theta = rand.NextDouble() * 2.0 * Math.PI;
            double phi = Math.Acos(2.0 * rand.NextDouble() - 1.0);
            double sinPhi = Math.Sin(phi);
            return Math.Pow(rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }
        private Dictionary<long, Vector3D> GetPlayerPositions() {
            var positions = new Dictionary<long, Vector3D>();
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (var player in players) {
                if (player?.Character != null) {
                    positions[player.IdentityId] = player.GetPosition();
                }
            }

            return positions;
        }
        #endregion

        private const int VALIDATION_BATCH_SIZE = 100; // Only check this many entities per validation
        private const double VALIDATION_MAX_TIME_MS = 16.0; // Max milliseconds to spend on validation (1 frame at 60fps)
        private int _lastValidatedIndex = 0; // Track where we left off
        private void ValidateAsteroidTracking() {
            try {
                var startTime = DateTime.UtcNow;
                var trackedIds = _asteroids.Select(a => a.EntityId).ToHashSet();
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                // Convert to list for indexed access
                var entityList = entities.Skip(_lastValidatedIndex).Take(VALIDATION_BATCH_SIZE).ToList();
                int untrackedCount = 0;
                int processedCount = 0;

                foreach (var entity in entityList) {
                    // Check if we're taking too long
                    if ((DateTime.UtcNow - startTime).TotalMilliseconds > VALIDATION_MAX_TIME_MS) {
                        Log.Warning($"Validation taking too long - processed {processedCount} entities before timeout");
                        break;
                    }

                    var asteroid = entity as AsteroidEntity;
                    if (asteroid != null && !asteroid.MarkedForClose) {
                        if (!trackedIds.Contains(asteroid.EntityId)) {
                            untrackedCount++;
                            Log.Warning($"Found untracked asteroid {asteroid.EntityId} at {asteroid.PositionComp.GetPosition()}");
                        }
                    }
                    processedCount++;
                }

                // Update the index for next time
                _lastValidatedIndex += processedCount;
                if (_lastValidatedIndex >= entities.Count) {
                    _lastValidatedIndex = 0; // Reset when we've checked everything

                    if (untrackedCount > 0) {
                        Log.Warning($"Validation cycle complete: Found {untrackedCount} untracked asteroids in world");
                    }
                }

                double elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsedMs > 5.0) // Log if taking more than 5ms
                {
                    Log.Info($"Asteroid validation took {elapsedMs:F2}ms to process {processedCount} entities");
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in asteroid validation");
                _lastValidatedIndex = 0; // Reset on error
            }
        }



    }
}