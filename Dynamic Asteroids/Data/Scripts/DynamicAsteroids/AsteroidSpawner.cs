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


namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids
{
    public class AsteroidZone
    {
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

    public class AsteroidSpawner
    {
        private ConcurrentBag<AsteroidEntity> _asteroids;
        private bool _canSpawnAsteroids = false;
        private DateTime _worldLoadTime;
        private Random rand;
        private List<AsteroidState> _despawnedAsteroids = new List<AsteroidState>();
        private ConcurrentQueue<AsteroidNetworkMessage> _networkMessages = new ConcurrentQueue<AsteroidNetworkMessage>();
        private ConcurrentDictionary<long, AsteroidZone> playerZones = new ConcurrentDictionary<long, AsteroidZone>();
        private ConcurrentDictionary<long, PlayerMovementData> playerMovementData = new ConcurrentDictionary<long, PlayerMovementData>();

        private ConcurrentQueue<AsteroidEntity> _updateQueue = new ConcurrentQueue<AsteroidEntity>();
        private const int UpdatesPerTick = 50;   // update rate of the roids

        private RealGasGiantsApi _realGasGiantsApi;


        private class ZoneCache
        {
            public AsteroidZone Zone { get; set; }
            public DateTime LastUpdateTime { get; set; }
            const int CacheExpirationSeconds = 5;

            public bool IsExpired()
            {
                return (DateTime.UtcNow - LastUpdateTime).TotalSeconds > CacheExpirationSeconds;
            }
        }

        private class AsteroidStateCache
        {
            private ConcurrentDictionary<long, AsteroidState> _stateCache = new ConcurrentDictionary<long, AsteroidState>();
            private ConcurrentBag<long> _dirtyStates = new ConcurrentBag<long>();
            private const int SaveInterval = 300;
            private DateTime _lastSaveTime = DateTime.UtcNow;

            public void UpdateState(long asteroidId, AsteroidState state)
            {
                _stateCache.AddOrUpdate(asteroidId, state, (key, oldValue) => state);
                _dirtyStates.Add(asteroidId);
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

            public bool ShouldSave()
            {
                return (DateTime.UtcNow - _lastSaveTime).TotalSeconds >= SaveInterval && !_dirtyStates.IsEmpty;
            }
        }

        private class NetworkMessageCache
        {
            private ConcurrentDictionary<long, AsteroidNetworkMessage> _messageCache = new ConcurrentDictionary<long, AsteroidNetworkMessage>();
            private ConcurrentQueue<AsteroidNetworkMessage> _messageQueue = new ConcurrentQueue<AsteroidNetworkMessage>();
            private const int MessageBatchSize = 100;  //the metal pipes sent to the client (we can probably hit 10k without issue, all server load is physics!!)
            private const int MessageExpirationSeconds = 10;

            public void AddMessage(AsteroidNetworkMessage message)
            {
                if (_messageCache.TryAdd(message.EntityId, message))
                {
                    _messageQueue.Enqueue(message);
                }
            }

            public void ProcessMessages(Action<AsteroidNetworkMessage> sendAction)
            {
                int processedCount = 0;
                AsteroidNetworkMessage message;
                while (processedCount < MessageBatchSize && _messageQueue.TryDequeue(out message))
                {
                    try
                    {
                        sendAction(message);
                        AsteroidNetworkMessage removedMessage;
                        _messageCache.TryRemove(message.EntityId, out removedMessage);
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex, typeof(NetworkMessageCache), "Failed to process network message");
                        _messageQueue.Enqueue(message);
                    }
                    processedCount++;
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

        private class PlayerMovementData
        {
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
        public void SaveAsteroidState()
        {
            if (!MyAPIGateway.Session.IsServer || !AsteroidSettings.EnablePersistence) return;

            if (!_stateCache.ShouldSave()) return;

            List<AsteroidState> dirtyStates = _stateCache.GetDirtyStates();
            if (dirtyStates.Count == 0) return;

            List<AsteroidState> allStates = _asteroids.Select(asteroid => new AsteroidState
            {
                Position = asteroid.PositionComp.GetPosition(),
                Size = asteroid.Size,
                Type = asteroid.Type,
                EntityId = asteroid.EntityId
            }).Concat(dirtyStates).ToList();

            allStates.AddRange(_despawnedAsteroids);
            byte[] stateBytes = MyAPIGateway.Utilities.SerializeToBinary(allStates);

            using (BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage("asteroid_states.dat", typeof(AsteroidSpawner)))
            {
                writer.Write(stateBytes, 0, stateBytes.Length);
            }
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
        public void LoadAsteroidState()
        {
            if (!MyAPIGateway.Session.IsServer || !AsteroidSettings.EnablePersistence) return;
            _asteroids = new ConcurrentBag<AsteroidEntity>();
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("asteroid_states.dat", typeof(AsteroidSpawner))) return;
            byte[] stateBytes;
            using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage("asteroid_states.dat", typeof(AsteroidSpawner)))
            {
                stateBytes = reader.ReadBytes((int)reader.BaseStream.Length);
            }
            List<AsteroidState> asteroidStates = MyAPIGateway.Utilities.SerializeFromBinary<List<AsteroidState>>(stateBytes);
            foreach (AsteroidState state in asteroidStates)
            {
                if (ContainsAsteroid(state.EntityId))// Use the ContainsAsteroid method
                {
                    Log.Info($"Skipping duplicate asteroid with ID {state.EntityId}");
                    continue;
                }
                AsteroidEntity asteroid = AsteroidEntity.CreateAsteroid(state.Position, state.Size, Vector3D.Zero, state.Type);
                asteroid.EntityId = state.EntityId;
                _asteroids.Add(asteroid);
                MyEntities.Add(asteroid);

                _updateQueue.Enqueue(asteroid);
            }
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
            if (!MyAPIGateway.Session.IsServer) return;

            try
            {
                Log.Info("Closing AsteroidSpawner");

                // Save state if persistence is enabled
                SaveAsteroidState();

                // Clear caches
                _zoneCache.Clear();
                playerZones.Clear();
                playerMovementData.Clear();

                // Safely clear asteroids
                if (_asteroids != null)
                {
                    foreach (var asteroid in _asteroids)
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

                // Clear update and network queues
                _updateQueue = new ConcurrentQueue<AsteroidEntity>();
                _networkMessages = new ConcurrentQueue<AsteroidNetworkMessage>();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner), "Error in Close method");
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
            playerZones = new ConcurrentDictionary<long, AsteroidZone>();
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (AsteroidZone mergedZone in mergedZones)
            {
                foreach (IMyPlayer player in players)
                {
                    if (!mergedZone.IsPointInZone(player.GetPosition())) continue;
                    playerZones[player.IdentityId] = mergedZone;
                    break;
                }
            }
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
            if (!MyAPIGateway.Session.IsServer) return;
            AssignZonesToPlayers();
            MergeZones();
            UpdateZones();
            try
            {
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                foreach (IMyPlayer player in players)
                {
                    AsteroidZone zone = GetCachedZone(player.IdentityId, player.GetPosition());
                    if (zone != null)
                    {
                        LoadAsteroidsInRange(player.GetPosition(), zone);
                    }
                }

                if (_updateIntervalTimer <= 0)
                {
                    UpdateAsteroids(playerZones.Values.ToList());
                    ProcessAsteroidUpdates();
                    _updateIntervalTimer = AsteroidSettings.UpdateInterval;
                }
                else
                {
                    _updateIntervalTimer--;
                }

                if (_spawnIntervalTimer > 0)
                {
                    _spawnIntervalTimer--;
                }
                else
                {
                    SpawnAsteroids(playerZones.Values.ToList());
                    _spawnIntervalTimer = AsteroidSettings.SpawnInterval;
                }

                if (AsteroidSettings.EnableLogging)
                    MyAPIGateway.Utilities.ShowNotification($"Active Asteroids: {_asteroids.Count}", 1000 / 60);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(AsteroidSpawner));
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

        private void ProcessAsteroidUpdates()
        {
            int updatesProcessed = 0;

            AsteroidEntity asteroid;
            while (updatesProcessed < UpdatesPerTick && _updateQueue.TryDequeue(out asteroid))
            {
                UpdateAsteroid(asteroid);

                _updateQueue.Enqueue(asteroid);

                updatesProcessed++;
            }
        }

        private void UpdateAsteroid(AsteroidEntity asteroid)
        {
            _stateCache.UpdateState(asteroid.EntityId, new AsteroidState
            {
                Position = asteroid.PositionComp.GetPosition(),
                Size = asteroid.Size,
                Type = asteroid.Type,
                EntityId = asteroid.EntityId
            });

            Vector3D currentPosition = asteroid.PositionComp.GetPosition();
            bool inAnyZone = false;
            AsteroidZone currentZone = null;
            foreach (AsteroidZone zone in playerZones.Values)
            {
                if (!zone.IsPointInZone(currentPosition)) continue;
                inAnyZone = true;
                currentZone = zone;
                break;
            }
            if (!inAnyZone)
            {
                RemoveAsteroid(asteroid);
            }
            else
            {
                currentZone.AsteroidCount++;
            }
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
                    if (data.Speed > 1000)
                    {
                        Log.Info($"Skipping asteroid spawning for player {player.DisplayName} due to high speed: {data.Speed} m/s.");
                        skipSpawning = true;
                        break;
                    }
                }

                if (skipSpawning)
                {
                    continue;
                }

                while (zone.AsteroidCount < AsteroidSettings.MaxAsteroidsPerZone &&
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

                    } while (!validPosition &&
                            zoneSpawnAttempts < AsteroidSettings.MaxZoneAttempts &&
                            totalSpawnAttempts < AsteroidSettings.MaxTotalAttempts);

                    if (zoneSpawnAttempts >= AsteroidSettings.MaxZoneAttempts || totalSpawnAttempts >= AsteroidSettings.MaxTotalAttempts)
                        break;

                    Vector3D newVelocity;
                    if (!AsteroidSettings.CanSpawnAsteroidAtPoint(newPosition, out newVelocity, isInRing))
                    {
                        Log.Info($"Cannot spawn asteroid at {newPosition}, skipping.");
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

                    // Calculate mass and clamp between MinMass and MaxMass
                    AsteroidSettings.MassRange massRange;
                    if (AsteroidSettings.MinMaxMassByType.TryGetValue(type, out massRange))
                    {
                        float radius = size / 2.0f;
                        float volume = (4.0f / 3.0f) * MathHelper.Pi * (float)Math.Pow(radius, 3); // Volume of a sphere
                        float density = 917.0f; // Ice density
                        float mass = MathHelper.Clamp(density * volume, massRange.MinMass, massRange.MaxMass);

                        Log.Info($"Spawned asteroid type {type} with calculated mass {mass}, clamped between {massRange.MinMass} and {massRange.MaxMass}");

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
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players)
            {
                Vector3D currentPosition = player.GetPosition();
                DateTime currentTime = DateTime.UtcNow;
                PlayerMovementData data;
                if (playerMovementData.TryGetValue(player.IdentityId, out data))
                {
                    double distance = Vector3D.Distance(currentPosition, data.LastPosition);
                    double timeElapsed = (currentTime - data.LastUpdateTime).TotalSeconds;
                    double speed = distance / timeElapsed;
                    data.Speed = speed;
                    playerMovementData[player.IdentityId].LastPosition = currentPosition;
                    playerMovementData[player.IdentityId].LastUpdateTime = currentTime;
                }
                else
                {
                    playerMovementData[player.IdentityId] = new PlayerMovementData
                    {
                        LastPosition = currentPosition,
                        LastUpdateTime = currentTime,
                        Speed = 0
                    };
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
            _messageCache.ProcessMessages(message =>
            {
                byte[] messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                Log.Info($"Server: Sent message for asteroid ID {message.EntityId}");
            });
        }

        private void RemoveAsteroid(AsteroidEntity asteroid)
        {
            AsteroidEntity removedAsteroid;
            if (!_asteroids.TryTake(out removedAsteroid)) return;
            if (removedAsteroid.EntityId == asteroid.EntityId)
            {
                _despawnedAsteroids.Add(new AsteroidState
                {
                    Position = asteroid.PositionComp.GetPosition(),
                    Size = asteroid.Size,
                    Type = asteroid.Type,
                    EntityId = asteroid.EntityId
                });
                _messageCache.AddMessage(new AsteroidNetworkMessage(asteroid.PositionComp.GetPosition(), asteroid.Size, Vector3D.Zero, Vector3D.Zero, asteroid.Type, false, asteroid.EntityId, true, false, Quaternion.Identity));
                MyEntities.Remove(asteroid);
                asteroid.Close();
                Log.Info($"Server: Removed asteroid with ID {asteroid.EntityId} from _asteroids list and MyEntities");
            }
            else
            {
                _asteroids.Add(removedAsteroid);
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
    }
}