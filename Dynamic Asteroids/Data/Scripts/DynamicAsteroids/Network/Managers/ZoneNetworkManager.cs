using DynamicAsteroids;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI;
using VRageMath;

namespace DynamicAsteroids
{
    public class ZoneNetworkManager : BaseNetworkManager {
        public ZoneNetworkManager(Action<string> logger) : base(logger) {
        }

        private Dictionary<long, HashSet<long>> _playerZoneAwareness = new Dictionary<long, HashSet<long>>();
        private const double ZONE_AWARENESS_RADIUS = 25000;

        public void UpdateZoneAwareness(Dictionary<long, Vector3D> playerPositions, ConcurrentDictionary<long, AsteroidZone> zones) {
            foreach (var player in playerPositions) {
                if (!_playerZoneAwareness.ContainsKey(player.Key)) {
                    _playerZoneAwareness[player.Key] = new HashSet<long>();
                }
                var relevantZones = zones.Where(z => Vector3D.Distance(player.Value, z.Value.Center) <= ZONE_AWARENESS_RADIUS);
                _playerZoneAwareness[player.Key] = new HashSet<long>(relevantZones.Select(z => z.Key));
            }
        }

        public void SendMessage(ushort channel, object message, ulong? targetSteamId = null) {
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(message);
            if (targetSteamId.HasValue)
                MyAPIGateway.Multiplayer.SendMessageTo(channel, data, targetSteamId.Value);
            else
                MyAPIGateway.Multiplayer.SendMessageToOthers(channel, data);
        }

        public void SendBatchedUpdates(List<AsteroidState> updates, ConcurrentDictionary<long, AsteroidZone> zones) {
            if (updates == null || updates.Count == 0 || zones == null || zones.Count == 0) {
                Logger?.Invoke("No updates to send or no zones available");
                return;
            }

            var updatesByZone = updates.GroupBy(u =>
            {
                var zone = zones.FirstOrDefault(z => z.Value.IsPointInZone(u.Position));
                return zone.Key;
            }).Where(g => g.Key != 0);

            foreach (var zoneGroup in updatesByZone) {
                var relevantPlayers = _playerZoneAwareness
                    .Where(p => p.Value.Contains(zoneGroup.Key))
                    .Select(p => p.Key)
                    .ToList();

                if (relevantPlayers.Count == 0) continue;

                const int MAX_UPDATES_PER_PACKET = 25;
                for (int i = 0; i < zoneGroup.Count(); i += MAX_UPDATES_PER_PACKET) {
                    var batch = zoneGroup.Skip(i).Take(MAX_UPDATES_PER_PACKET).ToList();
                    if (batch.Count == 0) continue;

                    var packet = new AsteroidBatchUpdatePacket();
                    packet.Updates.AddRange(batch);

                    foreach (var playerId in relevantPlayers) {
                        var steamId = NetworkUtils.GetSteamId(playerId);
                        if (steamId != 0) {
                            SendMessage(NetworkConstants.CHANNEL_ASTEROID_UPDATE, packet, steamId);
                        }
                    }
                }
            }
        }
    }
}
