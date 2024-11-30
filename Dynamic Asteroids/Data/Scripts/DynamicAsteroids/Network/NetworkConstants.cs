using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicAsteroids {
    public static class NetworkConstants {
        public const ushort CHANNEL_ASTEROID_SPAWN = 32001;
        public const ushort CHANNEL_ASTEROID_UPDATE = 32002;
        public const ushort CHANNEL_ASTEROID_REMOVE = 32003;
        public const ushort CHANNEL_ZONE_UPDATE = 32004;
        public const ushort CHANNEL_SETTINGS_SYNC = 32005;
        public const ushort CHANNEL_PLAYER_STATE = 32006;
    }
}
