using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace DynamicAsteroids {
    public class PlayerStateNetworkManager : BaseNetworkManager {
     
        public PlayerStateNetworkManager(Action<string> logger) : base(logger) {
        }


        public void SendPlayerState(long playerId, Vector3D position, double speed) {
            var message = new PlayerStatePacket {
                PlayerId = playerId,
                Position = position,
                Speed = speed
            };
            SendMessage(NetworkConstants.CHANNEL_PLAYER_STATE, message);
        }
    }
}
