using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using RealGasGiants;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;


namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    public class AsteroidZone {
        public Vector3D Center { get; set; }
        public double Radius { get; set; }
        public int AsteroidCount { get; set; }

        public AsteroidZone(Vector3D center, double radius)
        {
            Center = center;
            Radius = radius;
            AsteroidCount = 0;
        }

        public bool IsPointInZone(Vector3D point)
        {
            return Vector3D.DistanceSquared(Center, point) <= Radius * Radius;
        }
    }

    public class PlayerMovementData {
        public Vector3D LastPosition { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public double Speed { get; set; }
    }

    public class AsteroidSpawner {
        private ConcurrentBag<AsteroidEntity> _asteroids;
        private DateTime _worldLoadTime;
        private Random rand;
        private List<AsteroidState> _despawnedAsteroids = new List<AsteroidState>();
        private ConcurrentQueue<AsteroidEntity> _updateQueue = new ConcurrentQueue<AsteroidEntity>();
        private const int UpdatesPerTick = 50;
        private RealGasGiantsApi _realGasGiantsApi;

        // Managers
        private readonly ZoneManager _zoneManager;
        private NetworkMessageCache _messageCache;
        private readonly SpatialPartitioningSystem _spatialPartitioning;

        public AsteroidSpawner(RealGasGiantsApi realGasGiantsApi)
        {
            _realGasGiantsApi = realGasGiantsApi;

            // Initialize managers
            _zoneManager = new ZoneManager();
            _messageCache = new NetworkMessageCache();
            _spatialPartitioning = new SpatialPartitioningSystem();
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

        private int _spawnIntervalTimer = 0;

        public void UpdateTick()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            _spatialPartitioning.UpdatePlayerPartitions(players);
            _zoneManager.UpdatePlayerMovementData(players);
            _zoneManager.AssignZonesToPlayers();

            List<AsteroidZone> mergedZones = _zoneManager.MergeZones();
            _zoneManager.AssignZonesToPlayers();

            foreach (IMyPlayer player in players)
            {
                AsteroidZone zone = _zoneManager.GetCachedZone(player.IdentityId, player.GetPosition());
                if (zone != null)
                {
                    LoadAsteroidsInRange(player.GetPosition(), zone);
                }
            }

            if (_spawnIntervalTimer <= 0)
            {
                SpawnAsteroids(mergedZones);
                _spawnIntervalTimer = AsteroidSettings.SpawnInterval;// Reset the spawn timer
            }
            else
            {
                _spawnIntervalTimer--;
            }

            ProcessAsteroidUpdates();
            SendPositionUpdates();
        }

        private void LoadAsteroidsInRange(Vector3D playerPosition, AsteroidZone zone)
        {
            int skippedCount = 0;
            int respawnedCount = 0;
            List<Vector3D> skippedPositions = new List<Vector3D>();
            List<Vector3D> respawnedPositions = new List<Vector3D>();

            // Get nearby players from the spatial partitioning system
            var nearbyPlayers = _spatialPartitioning.GetNearbyPlayers(playerPosition, AsteroidSettings.ZoneRadius);

            foreach (AsteroidState state in _despawnedAsteroids.ToArray())
            {
                if (!zone.IsPointInZone(state.Position)) continue;

                // Check if the asteroid is too close to any nearby players
                bool tooClose = nearbyPlayers.Any(player => Vector3D.DistanceSquared(player.GetPosition(), state.Position) < AsteroidSettings.MinDistanceFromPlayer * AsteroidSettings.MinDistanceFromPlayer);
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

                // Add the respawn message to the message cache for processing
                AsteroidNetworkMessage message = new AsteroidNetworkMessage(state.Position, state.Size, Vector3D.Zero, Vector3D.Zero, state.Type, false, asteroid.EntityId, false, true, Quaternion.Identity);
                _messageCache.AddMessage(message);

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

        private void ProcessAsteroidUpdates()
        {
            // Get dirty asteroids that need updates
            var dirtyAsteroids = _zoneManager.GetActiveZones()
                .SelectMany(zone => _asteroids.Where(a => zone.IsPointInZone(a.PositionComp.GetPosition())))
                .ToList();

            int updatesProcessed = 0;

            foreach (AsteroidEntity asteroid in dirtyAsteroids)
            {
                if (updatesProcessed >= UpdatesPerTick) break;

                // Prepare and send an update message to clients
                AsteroidNetworkMessage message = new AsteroidNetworkMessage(
                    asteroid.PositionComp.GetPosition(), asteroid.Properties.Diameter, asteroid.Physics.LinearVelocity,
                    Vector3D.Zero, asteroid.Type, false, asteroid.EntityId, false, true, Quaternion.Identity
                );

                _messageCache.AddMessage(message);
                updatesProcessed++;
            }
        }

        public void SendPositionUpdates()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            try
            {
                _messageCache.ProcessMessages(message =>
                {
                    if (message.EntityId == 0)
                    {
                        Log.Warning("Attempted to send message for asteroid with ID 0");
                        return;
                    }

                    AsteroidEntity asteroid = MyEntities.GetEntityById(message.EntityId) as AsteroidEntity;
                    if (asteroid == null)
                    {
                        Log.Warning($"Attempted to send update for non-existent asteroid {message.EntityId}");
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
                    MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);

                    Log.Info($"Server: Sent position update for asteroid ID {updateMessage.EntityId}, Position: {updateMessage.GetPosition()}");
                });
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error sending network messages");
            }
        }

        public void Close()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            try
            {
                Log.Info("Closing AsteroidSpawner");
                _zoneManager.ClearZones();
                _messageCache = new NetworkMessageCache();// Clear out cached messages

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
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in Close method");
            }
        }

        private Vector3D RandVector()
        {
            double theta = rand.NextDouble() * 2.0 * Math.PI;
            double phi = Math.Acos(2.0 * rand.NextDouble() - 1.0);
            double sinPhi = Math.Sin(phi);
            return Math.Pow(rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
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

            foreach (AsteroidZone zone in zones)
            {
                int asteroidsSpawned = 0;
                int zoneSpawnAttempts = 0;

                if (zone.AsteroidCount >= AsteroidSettings.MaxAsteroidsPerZone)
                {
                    continue;
                }

                while(zone.AsteroidCount < AsteroidSettings.MaxAsteroidsPerZone &&
                      asteroidsSpawned < 10 &&
                      zoneSpawnAttempts < AsteroidSettings.MaxZoneAttempts &&
                      totalSpawnAttempts < AsteroidSettings.MaxTotalAttempts)
                {
                    Vector3D newPosition = zone.Center + RandVector() * AsteroidSettings.ZoneRadius;
                    zoneSpawnAttempts++;
                    totalSpawnAttempts++;

                    bool validPosition = IsValidSpawnPosition(newPosition, zones);

                    if (!validPosition) continue;

                    AsteroidEntity asteroid = AsteroidEntity.CreateAsteroid(newPosition, AsteroidSettings.GetAsteroidSize(newPosition), Vector3D.Zero, AsteroidSettings.GetAsteroidType(newPosition));
                    if (asteroid == null) continue;

                    _asteroids.Add(asteroid);
                    zone.AsteroidCount++;
                    spawnedPositions.Add(newPosition);

                    AsteroidNetworkMessage message = new AsteroidNetworkMessage(newPosition, asteroid.Properties.Diameter, Vector3D.Zero, Vector3D.Zero, asteroid.Type, false, asteroid.EntityId, false, true, Quaternion.Identity);
                    _messageCache.AddMessage(message);

                    asteroidsSpawned++;
                    Log.Info($"Spawned asteroid at {newPosition} with type {asteroid.Type}");
                }
            }

            if (skippedPositions.Count > 0)
            {
                Log.Info($"Skipped spawning asteroids due to invalid positions. Positions: {string.Join(", ", skippedPositions.Select(p => p.ToString()))}");
            }
            if (spawnedPositions.Count > 0)
            {
                Log.Info($"Spawned asteroids at positions: {string.Join(", ", spawnedPositions.Select(p => p.ToString()))}");
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
                    return true;
                }
            }

            foreach (SpawnableArea area in AsteroidSettings.ValidSpawnLocations)
            {
                if (area.ContainsPoint(position))
                {
                    return true;
                }
            }

            foreach (AsteroidZone zone in zones)
            {
                if (zone.IsPointInZone(position))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class NetworkMessageCache {
        private class MessageEntry {
            public AsteroidNetworkMessage Message { get; set; }
            public DateTime LastSentTime { get; set; }
            public int RetryCount { get; set; }
            public bool IsHighPriority { get; set; }
        }

        private readonly ConcurrentDictionary<long, MessageEntry> _messageCache = new ConcurrentDictionary<long, MessageEntry>();
        private readonly ConcurrentQueue<MessageEntry> _highPriorityQueue = new ConcurrentQueue<MessageEntry>();
        private readonly ConcurrentQueue<MessageEntry> _normalPriorityQueue = new ConcurrentQueue<MessageEntry>();

        private const int HIGH_PRIORITY_BATCH_SIZE = 20;
        private const int NORMAL_PRIORITY_BATCH_SIZE = 50;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int MIN_MESSAGE_INTERVAL_MS = 100;

        public void AddMessage(AsteroidNetworkMessage message)
        {
            MessageEntry entry = new MessageEntry {
                Message = message,
                LastSentTime = DateTime.UtcNow,
                RetryCount = 0,
                IsHighPriority = message.IsInitialCreation || message.IsRemoval
            };

            if (!_messageCache.TryAdd(message.EntityId, entry)) return;
            if (entry.IsHighPriority)
                _highPriorityQueue.Enqueue(entry);
            else
                _normalPriorityQueue.Enqueue(entry);
        }

        public void ProcessMessages(Action<AsteroidNetworkMessage> sendAction)
        {
            ProcessQueueBatch(_highPriorityQueue, HIGH_PRIORITY_BATCH_SIZE, sendAction);
            ProcessQueueBatch(_normalPriorityQueue, NORMAL_PRIORITY_BATCH_SIZE, sendAction);
        }

        private void ProcessQueueBatch(ConcurrentQueue<MessageEntry> queue, int batchSize, Action<AsteroidNetworkMessage> sendAction)
        {
            int processed = 0;
            MessageEntry entry;

            while(processed < batchSize && queue.TryDequeue(out entry))
            {
                if ((DateTime.UtcNow - entry.LastSentTime).TotalMilliseconds < MIN_MESSAGE_INTERVAL_MS)
                {
                    queue.Enqueue(entry);
                    continue;
                }

                try
                {
                    sendAction(entry.Message);
                    MessageEntry removedEntry;
                    _messageCache.TryRemove(entry.Message.EntityId, out removedEntry);
                    processed++;
                }
                catch (Exception ex)
                {
                    entry.RetryCount++;
                    entry.LastSentTime = DateTime.UtcNow;

                    if (entry.RetryCount < MAX_RETRY_ATTEMPTS)
                    {
                        queue.Enqueue(entry);
                        Log.Warning($"Failed to send message (attempt {entry.RetryCount}): {ex.Message}");
                    }
                    else
                    {
                        Log.Warning($"Message failed after {MAX_RETRY_ATTEMPTS} attempts: {ex.Message}");
                    }
                }
            }
        }
    }

    // Spatial Partitioning System
    public class SpatialPartitioningSystem {
        private class Partition {
            public HashSet<IMyPlayer> Players { get; set; }
            public HashSet<AsteroidZone> Zones { get; set; }

            public Partition()
            {
                Players = new HashSet<IMyPlayer>();
                Zones = new HashSet<AsteroidZone>();
            }
        }

        private readonly Dictionary<string, Partition> _partitions;
        private const double PARTITION_SIZE = 1000.0;

        public SpatialPartitioningSystem()
        {
            _partitions = new Dictionary<string, Partition>();
        }

        public void UpdatePlayerPartitions(List<IMyPlayer> players)
        {
            _partitions.Clear();

            foreach (IMyPlayer player in players)
            {
                Vector3D position = player.GetPosition();
                var key = GetPartitionKey(position);

                if (!_partitions.ContainsKey(key))
                {
                    _partitions[key] = new Partition();
                }

                _partitions[key].Players.Add(player);
            }
        }

        private string GetPartitionKey(Vector3D position)
        {
            int x = (int)(position.X / PARTITION_SIZE);
            int y = (int)(position.Y / PARTITION_SIZE);
            int z = (int)(position.Z / PARTITION_SIZE);
            return $"{x}:{y}:{z}";
        }

        public List<IMyPlayer> GetNearbyPlayers(Vector3D position, double radius)
        {
            var result = new HashSet<IMyPlayer>();
            var centerKey = GetPartitionKey(position);
            var neighborKeys = GetNeighboringPartitionKeys(position);

            foreach (var key in neighborKeys)
            {
                Partition partition;
                if (!_partitions.TryGetValue(key, out partition)) continue;
                foreach (IMyPlayer player in partition.Players)
                {
                    if (Vector3D.Distance(player.GetPosition(), position) <= radius)
                    {
                        result.Add(player);
                    }
                }
            }

            return result.ToList();
        }

        private List<string> GetNeighboringPartitionKeys(Vector3D position)
        {
            var keys = new List<string>();
            int x = (int)(position.X / PARTITION_SIZE);
            int y = (int)(position.Y / PARTITION_SIZE);
            int z = (int)(position.Z / PARTITION_SIZE);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                keys.Add($"{x + dx}:{y + dy}:{z + dz}");
            }

            return keys;
        }
    }

    // ZoneManager Class
    public class ZoneManager {
        private readonly ConcurrentDictionary<long, AsteroidZone> _playerZones;
        private readonly ConcurrentDictionary<long, PlayerMovementData> _playerMovementData;
        public readonly ConcurrentDictionary<long, ZoneCache> _zoneCache;
        private readonly object _zoneLock = new object();

        public ZoneManager()
        {
            _playerZones = new ConcurrentDictionary<long, AsteroidZone>();
            _playerMovementData = new ConcurrentDictionary<long, PlayerMovementData>();
            _zoneCache = new ConcurrentDictionary<long, ZoneCache>();
        }

        public void AssignZonesToPlayers()
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var updatedZones = new Dictionary<long, AsteroidZone>();

            foreach (IMyPlayer player in players)
            {
                Vector3D playerPosition = player.GetPosition();
                if (ShouldSkipZoneAssignment(player.IdentityId, playerPosition))
                    continue;

                AsteroidZone zone = GetOrCreateZone(player.IdentityId, playerPosition);
                updatedZones[player.IdentityId] = zone;
            }

            _playerZones.Clear();
            foreach (var kvp in updatedZones)
            {
                _playerZones.TryAdd(kvp.Key, kvp.Value);
            }
        }

        private bool ShouldSkipZoneAssignment(long playerId, Vector3D position)
        {
            PlayerMovementData data;
            if (!_playerMovementData.TryGetValue(playerId, out data))
                return false;

            return AsteroidSettings.DisableZoneWhileMovingFast &&
                   data.Speed > AsteroidSettings.ZoneSpeedThreshold;
        }

        private AsteroidZone GetOrCreateZone(long playerId, Vector3D position)
        {
            AsteroidZone existingZone;
            if (_playerZones.TryGetValue(playerId, out existingZone) &&
                existingZone.IsPointInZone(position))
            {
                return existingZone;
            }

            return new AsteroidZone(position, AsteroidSettings.ZoneRadius);
        }

        public List<AsteroidZone> MergeZones()
        {
            lock (_zoneLock)
            {
                var mergedZones = new List<AsteroidZone>();
                var processedCenters = new HashSet<Vector3D>();

                foreach (AsteroidZone zone in _playerZones.Values)
                {
                    if (processedCenters.Contains(zone.Center))
                        continue;

                    AsteroidZone mergedZone = new AsteroidZone(zone.Center, zone.Radius) {
                        AsteroidCount = zone.AsteroidCount
                    };

                    foreach (AsteroidZone otherZone in _playerZones.Values)
                    {
                        if (processedCenters.Contains(otherZone.Center))
                            continue;

                        if (!ShouldMergeZones(mergedZone, otherZone)) continue;
                        MergeZoneProperties(mergedZone, otherZone);
                        processedCenters.Add(otherZone.Center);
                    }

                    mergedZones.Add(mergedZone);
                    processedCenters.Add(zone.Center);
                }

                return mergedZones;
            }
        }

        private bool ShouldMergeZones(AsteroidZone zone1, AsteroidZone zone2)
        {
            double distance = Vector3D.Distance(zone1.Center, zone2.Center);
            return distance <= (zone1.Radius + zone2.Radius);
        }

        private void MergeZoneProperties(AsteroidZone target, AsteroidZone source)
        {
            Vector3D newCenter = (target.Center + source.Center) / 2;
            double distance = Vector3D.Distance(target.Center, source.Center);
            double newRadius = Math.Max(target.Radius, source.Radius) + distance / 2;

            target.Center = newCenter;
            target.Radius = newRadius;
            target.AsteroidCount += source.AsteroidCount;
        }

        public void UpdatePlayerMovementData(List<IMyPlayer> players)
        {
            const double SPEED_SMOOTHING_FACTOR = 0.3;
            const double MIN_TIME_DELTA = 0.016;

            foreach (IMyPlayer player in players)
            {
                Vector3D currentPosition = player.GetPosition();
                DateTime currentTime = DateTime.UtcNow;

                PlayerMovementData data;
                if (!_playerMovementData.TryGetValue(player.IdentityId, out data))
                {
                    data = new PlayerMovementData {
                        LastPosition = currentPosition,
                        LastUpdateTime = currentTime,
                        Speed = 0
                    };
                    _playerMovementData[player.IdentityId] = data;
                    continue;
                }

                UpdatePlayerSpeed(data, currentPosition, currentTime,
                    SPEED_SMOOTHING_FACTOR, MIN_TIME_DELTA);
            }
        }

        private void UpdatePlayerSpeed(PlayerMovementData data, Vector3D currentPosition,
            DateTime currentTime, double smoothingFactor,
            double minTimeDelta)
        {
            double timeElapsed = Math.Max(
                (currentTime - data.LastUpdateTime).TotalSeconds,
                minTimeDelta);
            double distance = Vector3D.Distance(currentPosition, data.LastPosition);
            double instantaneousSpeed = distance / timeElapsed;

            data.Speed = (data.Speed * (1 - smoothingFactor)) +
                         (instantaneousSpeed * smoothingFactor);
            data.LastPosition = currentPosition;
            data.LastUpdateTime = currentTime;
        }

        public IEnumerable<AsteroidZone> GetActiveZones()
        {
            return _playerZones.Values;
        }

        public AsteroidZone GetCachedZone(long playerId, Vector3D playerPosition)
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

        public void ClearZones()
        {
            _playerZones.Clear();
            _playerMovementData.Clear();
            _zoneCache.Clear();
        }
    }

    public class ZoneCache {
        public AsteroidZone Zone { get; set; }
        public DateTime LastUpdateTime { get; set; }
        private const int CacheExpirationSeconds = 5;

        public bool IsExpired()
        {
            return (DateTime.UtcNow - LastUpdateTime).TotalSeconds > CacheExpirationSeconds;
        }
    }
}