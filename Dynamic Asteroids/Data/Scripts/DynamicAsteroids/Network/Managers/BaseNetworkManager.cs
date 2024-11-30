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
            if (logger == null) {
                throw new ArgumentNullException("logger");
            }
            Logger = logger;
        }

        protected void SendMessage(ushort channel, object message, ulong? targetSteamId = null) {
            if (message == null) {
                Logger("Attempted to send null message");
                return;
            }

            try {
                byte[] data = MyAPIGateway.Utilities.SerializeToBinary(message);
                if (data == null || data.Length == 0) {
                    Logger("Failed to serialize message");
                    return;
                }

                if (targetSteamId.HasValue) {
                    MyAPIGateway.Multiplayer.SendMessageTo(channel, data, targetSteamId.Value);
                }
                else {
                    MyAPIGateway.Multiplayer.SendMessageToOthers(channel, data);
                }
            }
            catch (Exception ex) {
                Logger(string.Format("Error sending message: {0}", ex.Message));
            }
        }
    }
}
