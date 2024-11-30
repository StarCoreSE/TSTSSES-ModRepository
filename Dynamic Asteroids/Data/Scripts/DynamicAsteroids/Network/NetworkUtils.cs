using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI;

namespace DynamicAsteroids {
    public static class NetworkUtils {
        public static ulong GetSteamId(long playerId) {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var player = players.FirstOrDefault(p => p.IdentityId == playerId);
            return player?.SteamUserId ?? 0;
        }
    }
}
