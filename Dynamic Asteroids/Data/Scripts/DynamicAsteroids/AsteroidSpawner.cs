using Sandbox.ModAPI;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using System.Linq;
using System;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using RealGasGiants;
using System.IO;


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
        private const int UpdatesPerTick = 50; // Adjust this number based on performance needs

        private RealGasGiantsApi _realGasGiantsApi;

        public AsteroidSpawner(RealGasGiantsApi realGasGiantsApi)
        {
            _realGasGiantsApi = realGasGiantsApi;
            // ... (initialize other fields)
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

        public void SaveAsteroidState()
        {
            if (!MyAPIGateway.Session.IsServer || !AsteroidSettings.EnablePersistence) return;
            var asteroidStates = _asteroids.Select(asteroid => new AsteroidState
            {
                Position = asteroid.PositionComp.GetPosition(),
                Size = asteroid.Size,
                Type = asteroid.Type,
                EntityId = asteroid.EntityId
            }).ToList();
            asteroidStates.AddRange(_despawnedAsteroids);
            var stateBytes = MyAPIGateway.Utilities.SerializeToBinary(asteroidStates);
            using (BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage("asteroid_states.dat", typeof(AsteroidSpawner)))
            {
                writer.Write(stateBytes, 0, stateBytes.Length);
            }

            // Ensure the update queue is saved as well
            _updateQueue = new ConcurrentQueue<AsteroidEntity>();
            foreach (AsteroidEntity asteroid in _asteroids)
            {
                _updateQueue.Enqueue(asteroid);
            }
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
            var asteroidStates = MyAPIGateway.Utilities.SerializeFromBinary<List<AsteroidState>>(stateBytes);
            foreach (AsteroidState state in asteroidStates)
            {
                if (_asteroids.Any(a => a.EntityId == state.EntityId))
                {
                    Log.Info($"Skipping duplicate asteroid with ID {state.EntityId}");
                    continue;
                }
                AsteroidEntity asteroid = AsteroidEntity.CreateAsteroid(state.Position, state.Size, Vector3D.Zero, state.Type);
                asteroid.EntityId = state.EntityId;
                _asteroids.Add(asteroid);
                MyEntities.Add(asteroid);

                // Add to update queue
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
                var messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                _despawnedAsteroids.Remove(state);

                // Add to update queue
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
            SaveAsteroidState();
            Log.Info("Closing AsteroidSpawner");
            _asteroids = null;
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
            foreach (var kvp in playerZones)
            {
                if (players.Any(p => kvp.Value.IsPointInZone(p.GetPosition())))
                {
                    updatedZones[kvp.Key] = kvp.Value;
                }
            }
            playerZones = new ConcurrentDictionary<long, AsteroidZone>(updatedZones);
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
                if (_updateIntervalTimer > 0)
                {
                    _updateIntervalTimer--;
                }
                else
                {
                    UpdateAsteroids(playerZones.Values.ToList());
                    ProcessAsteroidUpdates();
                    _updateIntervalTimer = AsteroidSettings.UpdateInterval;
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
                foreach (IMyPlayer player in players)
                {
                    Vector3D playerPosition = player.GetPosition();
                    AsteroidZone zone;
                    if (playerZones.TryGetValue(player.IdentityId, out zone))
                    {
                        LoadAsteroidsInRange(playerPosition, zone);
                    }
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
            //Log.Info($"Updating asteroids. Total asteroids: {_asteroids.Count}, Total zones: {zones.Count}");
            int removedCount = 0;

            // Use a separate list to store the asteroids to be removed
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

            // Remove the asteroids after the iteration
            foreach (AsteroidEntity asteroid in asteroidsToRemove)
            {
                RemoveAsteroid(asteroid);
            }

            //Log.Info($"Update complete. Removed asteroids: {removedCount}, Remaining asteroids: {_asteroids.Count}");
            foreach (AsteroidZone zone in zones)
            {
                //Log.Info($"Zone center: {zone.Center}, Radius: {zone.Radius}, Asteroid count: {zone.AsteroidCount}");
            }
        }

        private void ProcessAsteroidUpdates()
        {
            int updatesProcessed = 0;

            AsteroidEntity asteroid;
            while (updatesProcessed < UpdatesPerTick && _updateQueue.TryDequeue(out asteroid))
            {
                // Perform the update logic for the asteroid here
                UpdateAsteroid(asteroid);

                // Re-enqueue the asteroid for future updates
                _updateQueue.Enqueue(asteroid);

                updatesProcessed++;
            }
        }

        private void UpdateAsteroid(AsteroidEntity asteroid)
        {
            // Implement the actual update logic for an individual asteroid here
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
               // Log.Info($"Removing asteroid at {currentPosition} due to being out of any player zone");
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
                    if (!(data.Speed > 1000)) continue;
                    Log.Info($"Skipping asteroid spawning for player {player.DisplayName} due to high speed: {data.Speed} m/s.");
                    skipSpawning = true;
                    break;
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
                        Log.Info($"Attempting to spawn asteroid at {newPosition} (attempt {totalSpawnAttempts})");

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

                    // Scale spawn chance based on ring influence
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

                    // Optionally scale size based on ring influence
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

                    AsteroidNetworkMessage message = new AsteroidNetworkMessage(newPosition, size, newVelocity, Vector3D.Zero, type, false, asteroid.EntityId, false, true, rotation);
                    _networkMessages.Enqueue(message);
                    asteroidsSpawned++;

                    Log.Info($"Spawned asteroid at {newPosition} with size {size} and type {type}");
                }

                totalAsteroidsSpawned += asteroidsSpawned;
                totalZoneSpawnAttempts += zoneSpawnAttempts;
            }

            if (!AsteroidSettings.EnableLogging) return;
            //Log.Info($"All zones spawn attempt complete. Total spawn attempts: {totalSpawnAttempts}, New total asteroid count: {_asteroids.Count}");
            //Log.Info($"Total asteroids spawned: {totalAsteroidsSpawned}, Total zone spawn attempts: {totalZoneSpawnAttempts}");
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
                    Log.Info($"Valid position in ring: {position}, influence: {ringInfluence}");
                    return true;
                }
            }
            foreach (SpawnableArea area in AsteroidSettings.ValidSpawnLocations)
            {
                if (!area.ContainsPoint(position)) continue;
                Log.Info($"Valid position in SpawnableArea: {position}");
                return true;
            }
            foreach (AsteroidZone zone in zones)
            {
                if (!zone.IsPointInZone(position)) continue;
                Log.Info($"Valid position in player zone: {position}");
                return true;
            }
            Log.Info($"Invalid spawn position: {position}");
            return false;
        }

        public void SendNetworkMessages()
        {
            AsteroidNetworkMessage message;
            while (_networkMessages.TryDequeue(out message))
            {
                try
                {
                    var messageBytes = MyAPIGateway.Utilities.SerializeToBinary(message);
                    Log.Info($"Server: Serialized message size: {messageBytes.Length} bytes");
                    MyAPIGateway.Multiplayer.SendMessageToOthers(32000, messageBytes);
                    Log.Info($"Server: Sent message for asteroid ID {message.EntityId}");
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, typeof(AsteroidSpawner), "Failed to send network message");
                }
            }
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
                AsteroidNetworkMessage removalMessage = new AsteroidNetworkMessage(asteroid.PositionComp.GetPosition(), asteroid.Size, Vector3D.Zero, Vector3D.Zero, asteroid.Type, false, asteroid.EntityId, true, false, Quaternion.Identity);
                _networkMessages.Enqueue(removalMessage);
                MyEntities.Remove(asteroid);
                asteroid.Close();
                Log.Info($"Server: Removed asteroid with ID {asteroid.EntityId} from _asteroids list and MyEntities");
            }
            else
            {
                // The removed asteroid is not the one we wanted to remove, so add it back to the bag
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
            var theta = rand.NextDouble() * 2.0 * Math.PI;
            var phi = Math.Acos(2.0 * rand.NextDouble() - 1.0);
            var sinPhi = Math.Sin(phi);
            return Math.Pow(rand.NextDouble(), 1 / 3d) * new Vector3D(sinPhi * Math.Cos(theta), sinPhi * Math.Sin(theta), Math.Cos(phi));
        }
    }
}