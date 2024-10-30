using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicAsteroids {
    public abstract class BaseNetworkManager {
        protected readonly Action<string> Logger;

        protected BaseNetworkManager(Action<string> logger) {
            Logger = logger;
        }

        protected void SendMessage(ushort channel, object message, ulong? targetSteamId = null) {
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(message);
            if (targetSteamId.HasValue)
                MyAPIGateway.Multiplayer.SendMessageTo(channel, data, targetSteamId.Value);
            else
                MyAPIGateway.Multiplayer.SendMessageToOthers(channel, data);
        }
    }
}
