using Sandbox.ModAPI;
using System.Collections.Generic;
using Sandbox.Game;

namespace Invalid.DeltaVQuestLog.Commands
{
    internal static class CommandMethods
    {
        private static long PlayerId => MyAPIGateway.Session.Player.IdentityId;

        private static bool IsFactionLeaderOrFounder(long factionId, long playerId) =>
            PersistentFactionObjectives.IsFactionLeaderOrFounder(factionId, playerId);

        public static void HandleAddObjective(string objectiveText, long factionId)
        {
            if (string.IsNullOrWhiteSpace(objectiveText))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Please provide an objective description.");
                return;
            }

            if (!IsFactionLeaderOrFounder(factionId, PlayerId))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can add objectives.");
                return;
            }

            var manager = PersistentFactionObjectives.I.GetFactionManger(factionId);
            manager.AddQuest(objectiveText);
        }

        public static void HandleListObjectives(long factionId)
        {
            var manager = PersistentFactionObjectives.I.GetFactionManger(factionId);

            if (manager.Objectives.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives found.");
                return;
            }

            MyAPIGateway.Utilities.ShowMessage("Objectives", "Faction Objectives:");
            for (int i = 0; i < manager.Objectives.Count; i++)
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", $"{i + 1}. {manager.Objectives[i]}");
            }
        }

        public static void HandleShowObjectives(long factionId)
        {
            var manager = PersistentFactionObjectives.I.GetFactionManger(factionId);

            if (manager.Objectives.Count == 0)
            {
                MyVisualScriptLogicProvider.SetQuestlog(false, "", PlayerId);
                MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to display.");
                return;
            }

            manager.UpdatePlayerQuestlog();
        }

        public static void HandleRemoveObjective(string args, long factionId)
        {
            int index;
            if (!int.TryParse(args, out index))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Please provide a valid objective index to remove.");
                return;
            }

            if (!IsFactionLeaderOrFounder(factionId, PlayerId))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can remove objectives.");
                return;
            }

            var manager = PersistentFactionObjectives.I.GetFactionManger(factionId);

            if (!manager.RemoveQuest(index))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Invalid objective index.");
                return;
            }
        }

        public static void HandleBroadcast(string args, long factionId)
        {
            int duration;
            if (!int.TryParse(args, out duration))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj broadcast <duration in seconds>");
                return;
            }

            var manager = PersistentFactionObjectives.I.GetFactionManger(factionId);

            if (manager.Objectives.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to broadcast.");
                return;
            }

            manager.ForceShow(duration);
        }

        public static void HandleHideQuestLog(string[] args)
        {
            MyVisualScriptLogicProvider.SetQuestlog(false, "", PlayerId);
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Quest log hidden.");
        }

        public static void HandleNotifications(string option)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj notifications <on/off>");
                return;
            }

            if (option == "off")
            {
                if (notificationsDisabled.Contains(PlayerId))
                {
                    MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications are already turned off.");
                }
                else
                {
                    notificationsDisabled.Add(PlayerId);
                    MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications turned off.");
                }
            }
            else if (option == "on")
            {
                if (!notificationsDisabled.Contains(PlayerId))
                {
                    MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications are already turned on.");
                }
                else
                {
                    notificationsDisabled.Remove(PlayerId);
                    MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications turned on.");
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Invalid option. Usage: /obj notifications <on/off>");
            }
        }

        public static void HandleClearObjectives(long factionId)
        {
            if (!IsFactionLeaderOrFounder(factionId, PlayerId))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can clear all objectives.");
                return;
            }

            var manager = PersistentFactionObjectives.I.GetFactionManger(factionId);

            if (manager.Objectives.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to clear.");
                return;
            }

            manager.ClearAllQuests();
        }
    }
}
