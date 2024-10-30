using DynamicAsteroids;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicAsteroids {
    public class AsteroidNetworkManager : BaseNetworkManager {
        
        public AsteroidNetworkManager(Action<string> logger) : base(logger) {
        }

        private HashSet<long> _knownAsteroids = new HashSet<long>();

        public void SendSpawnMessage(AsteroidEntity asteroid) {
            var message = new AsteroidSpawnPacket(asteroid);
            SendMessage(NetworkConstants.CHANNEL_ASTEROID_SPAWN, message);
        }

        public void SendUpdateMessage(AsteroidEntity asteroid) {
            var message = new AsteroidUpdatePacket(asteroid);
            SendMessage(NetworkConstants.CHANNEL_ASTEROID_UPDATE, message);
        }

        public void SendRemoveMessage(long asteroidId) {
            var message = new AsteroidRemovalPacket { EntityId = asteroidId };
            SendMessage(NetworkConstants.CHANNEL_ASTEROID_REMOVE, message);
        }
    }

}
