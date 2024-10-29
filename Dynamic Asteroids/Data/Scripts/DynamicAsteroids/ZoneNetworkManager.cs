using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI;
using VRageMath;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids
{
    public class ZoneNetworkManager
    {
        private Dictionary<long, HashSet<long>> _playerZoneAwareness = new Dictionary<long, HashSet<long>>();
        private const double ZONE_AWARENESS_RADIUS = 25000;

        public void UpdateZoneAwareness(Dictionary<long, Vector3D> playerPositions,
            ConcurrentDictionary<long, AsteroidZone> zones)
        {
            // Clear old awareness data
            _playerZoneAwareness.Clear();

            // Build new awareness data
            foreach (var playerKvp in playerPositions)
            {
                var playerPos = playerKvp.Value;
                var playerid = playerKvp.Key;

                // First, add player's own zone
                var ownZone = zones.FirstOrDefault(z => z.Value.IsPointInZone(playerPos));
                if (ownZone.Value != null)
                {
                    if (!_playerZoneAwareness.ContainsKey(playerid))
                    {
                        _playerZoneAwareness[playerid] = new HashSet<long>();
                    }

                    _playerZoneAwareness[playerid].Add(ownZone.Key);

                    // Then check for merged zones
                    foreach (var otherZone in zones)
                    {
                        if (otherZone.Key == ownZone.Key) continue;

                        double distance = Vector3D.Distance(ownZone.Value.Center, otherZone.Value.Center);
                        if (distance <= ownZone.Value.Radius + otherZone.Value.Radius)
                        {
                            _playerZoneAwareness[playerid].Add(otherZone.Key);
                        }
                    }
                }
            }
        }

        public void SendBatchedUpdates(List<AsteroidState> updates, ConcurrentDictionary<long, AsteroidZone> zones)
        {
            if (updates == null || updates.Count == 0 || zones == null || zones.Count == 0)
            {
                return;
            }

            // Group updates by zone
            var updatesByZone = updates.GroupBy(u =>
            {
                var zone = zones.FirstOrDefault(z => z.Value.IsPointInZone(u.Position));
                return zone.Key;
            }).Where(g => g.Key != 0);

            foreach (var zoneGroup in updatesByZone)
            {
                // Find players who should receive updates for this zone
                var relevantPlayers = _playerZoneAwareness
                    .Where(p => p.Value.Contains(zoneGroup.Key))
                    .Select(p => p.Key)
                    .ToList();

                if (relevantPlayers.Count == 0) continue;

                // Send updates in batches
                const int MAX_UPDATES_PER_PACKET = 25;
                for (int i = 0; i < zoneGroup.Count(); i += MAX_UPDATES_PER_PACKET)
                {
                    var batch = zoneGroup.Skip(i).Take(MAX_UPDATES_PER_PACKET).ToList();
                    if (batch.Count == 0) continue;

                    var packet = new AsteroidBatchUpdatePacket();
                    packet.Updates.AddRange(batch);
                    byte[] data = MyAPIGateway.Utilities.SerializeToBinary(packet);

                    foreach (var playerId in relevantPlayers)
                    {
                        var steamId = NetworkUtils.GetSteamId(playerId);
                        if (steamId != 0)
                        {
                            MyAPIGateway.Multiplayer.SendMessageTo(32000, data, steamId);
                        }
                    }
                }
            }
        }
    }

    public static class NetworkUtils {
        public static ulong GetSteamId(long playerId) {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var player = players.FirstOrDefault(p => p.IdentityId == playerId);
            return player != null ? player.SteamUserId : 0;
        }
    }
}