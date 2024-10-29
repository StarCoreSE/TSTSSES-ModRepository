using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI;
using VRageMath;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    public class ZoneNetworkManager {
        private Dictionary<long, HashSet<long>> _playerZoneAwareness = new Dictionary<long, HashSet<long>>();
        private const double ZONE_AWARENESS_RADIUS = 25000; // Only sync zones within this range

        public void UpdateZoneAwareness(
            Dictionary<long, Vector3D> playerPositions,
            ConcurrentDictionary<long, AsteroidZone> zones) {
            foreach (var player in playerPositions) {
                if (!_playerZoneAwareness.ContainsKey(player.Key)) {
                    _playerZoneAwareness[player.Key] = new HashSet<long>();
                }

                // Find zones this player should be aware of
                var relevantZones = zones.Where(z =>
                    Vector3D.Distance(player.Value, z.Value.Center) <= ZONE_AWARENESS_RADIUS);

                _playerZoneAwareness[player.Key] = new HashSet<long>(relevantZones.Select(z => z.Key));
            }
        }

        public void SendBatchedUpdates(List<AsteroidState> updates, ConcurrentDictionary<long, AsteroidZone> zones) {
            if (updates == null || updates.Count == 0 || zones == null || zones.Count == 0) {
                return;
            }

            // Group updates by zone
            var updatesByZone = updates.GroupBy(u => {
                var zone = zones.FirstOrDefault(z => z.Value.IsPointInZone(u.Position));
                return zone.Key;
            }).Where(g => g.Key != 0); // Filter out updates with no zone

            if (!updatesByZone.Any()) return;

            foreach (var zoneGroup in updatesByZone) {
                // Find players who should receive these updates
                var relevantPlayers = _playerZoneAwareness
                    .Where(p => p.Value.Contains(zoneGroup.Key))
                    .Select(p => p.Key)
                    .ToList();

                if (relevantPlayers.Count == 0) continue;

                // Send batched updates only to relevant players
                const int MAX_UPDATES_PER_PACKET = 25;
                for (int i = 0; i < zoneGroup.Count(); i += MAX_UPDATES_PER_PACKET) {
                    var batch = zoneGroup.Skip(i).Take(MAX_UPDATES_PER_PACKET).ToList();
                    if (batch.Count == 0) continue;

                    var packet = new AsteroidBatchUpdatePacket();
                    packet.Updates.AddRange(batch);

                    byte[] data = MyAPIGateway.Utilities.SerializeToBinary(packet);

                    // Send only to players who care about this zone
                    foreach (var playerId in relevantPlayers) {
                        var steamId = GetSteamId(playerId);
                        if (steamId != 0) {
                            MyAPIGateway.Multiplayer.SendMessageTo(32000, data, steamId);
                        }
                    }
                }
            }
        }

        private ulong GetSteamId(long playerId) {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var player = players.FirstOrDefault(p => p.IdentityId == playerId);
            return player?.SteamUserId ?? 0;
        }
    }
}
