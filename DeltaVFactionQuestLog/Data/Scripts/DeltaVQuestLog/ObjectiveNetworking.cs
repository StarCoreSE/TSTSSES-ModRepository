using System;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Invalid.DeltaVQuestLog
{
    internal class ObjectiveNetworking
    {
        private const ushort QuestLogMessageId = 16852; // Choose a unique ID

        public void Init()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(QuestLogMessageId, OnQuestLogMessageReceived);
        }

        public void Close()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(QuestLogMessageId, OnQuestLogMessageReceived);
        }

        public void SendQuestLogIndividual(QuestLogMessage message, ulong playerId)
        {
            var data = MyAPIGateway.Utilities.SerializeToBinary(message);
            MyAPIGateway.Multiplayer.SendMessageTo(QuestLogMessageId, data, playerId);
        }

        private void OnQuestLogMessageReceived(byte[] data)
        {
            try
            {
                var message = MyAPIGateway.Utilities.SerializeFromBinary<QuestLogMessage>(data);
                if (message == null) return;

                if (MyAPIGateway.Session.IsServer)
                {
                    // Handle server-side logic
                }
                else
                {
                    // Handle client-side display
                    PersistentFactionObjectives.I.HandleClientQuestLogDisplay(message);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"Error processing quest log message: {e}");
            }
        }
    }
}
