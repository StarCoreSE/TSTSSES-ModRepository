﻿using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Utils;
using System.Collections.Generic;
using System;
using VRage.Game;

[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class PersistentFactionObjectives : MySessionComponentBase
{
    private const string FileName = "FactionObjectives.txt";

    private Dictionary<long, List<string>> factionObjectives = new Dictionary<long, List<string>>();
    private HashSet<long> notificationsDisabled = new HashSet<long>();
    private bool isServer;
    private Dictionary<long, DateTime> questLogHideTimes = new Dictionary<long, DateTime>();
    private Dictionary<long, int> questLogCountdowns = new Dictionary<long, int>();


    public override void LoadData()
    {
        isServer = MyAPIGateway.Multiplayer.IsServer; // True for server instances (single-player, listen server host, dedicated server)
        bool isDedicated = MyAPIGateway.Utilities.IsDedicated; // True only for dedicated servers

        if (isServer && !isDedicated)
        {
            // Single-player or listen server host
            LoadObjectives();
        }
        else if (isDedicated)
        {
            // Dedicated server
            LoadObjectives();
        }

        // Register chat commands for all instances
        MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;

        // Register player connect events on the server side (for dedicated and listen servers)
        if (isServer)
        {
            MyVisualScriptLogicProvider.PlayerConnected += OnPlayerConnected;
        }
    }

    protected override void UnloadData()
    {
        MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

        if (isServer)
        {
            SaveObjectives();
            MyVisualScriptLogicProvider.PlayerConnected -= OnPlayerConnected;
        }
    }

    public override void UpdateAfterSimulation()
    {
        if (!isServer) return;

        var now = DateTime.UtcNow;
        var playersToRemove = new List<long>();

        foreach (var entry in questLogHideTimes)
        {
            if (entry.Value <= now)
            {
                MyVisualScriptLogicProvider.SetQuestlog(false, "", entry.Key);
                playersToRemove.Add(entry.Key);
            }
        }

        foreach (var playerId in playersToRemove)
        {
            questLogHideTimes.Remove(playerId);
        }
    }

    private void OnPlayerConnected(long playerId)
    {
        if (!isServer) return;

        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
        if (playerFaction == null) return;

        var factionId = playerFaction.FactionId;
        ShowQuestLogForPlayer(factionId, playerId, 30);
    }

    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
        if (!messageText.StartsWith("/obj") && !messageText.StartsWith("/objective")) return;

        sendToOthers = false;

        var args = messageText.Split(' ');
        if (args.Length < 2)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Invalid command. Use '/obj help' for a list of valid commands.");
            return;
        }

        var playerId = MyAPIGateway.Session.Player.IdentityId;
        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);

        if (args[1].ToLower() == "help")
        {
            ShowHelp();
            return;
        }

        if (playerFaction == null && args[1].ToLower() != "notifications")
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "You are not in a faction.");
            return;
        }

        var factionId = playerFaction?.FactionId ?? -1;

        switch (args[1].ToLower())
        {
            case "add":
                HandleAddObjective(args, factionId, playerId);
                break;
            case "list":
                HandleListObjectives(factionId);
                break;
            case "show":
                HandleShowObjectives(factionId, playerId);
                break;
            case "remove":
                HandleRemoveObjective(args, factionId, playerId);
                break;
            case "broadcast":
                HandleBroadcast(args, factionId);
                break;
            case "hide":
                HandleHideQuestLog(playerId);
                break;
            case "notifications":
                HandleNotifications(args, playerId);
                break;
            case "clear":
                HandleClearObjectives(factionId, playerId);
                break;
            default:
                MyAPIGateway.Utilities.ShowMessage("Objectives", $"Invalid command '{args[1]}'. Use '/obj help' for a list of valid commands.");
                break;
        }
    }

    private void HandleAddObjective(string[] args, long factionId, long playerId)
    {
        if (args.Length < 3)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Please provide an objective description.");
            return;
        }

        var objectiveText = string.Join(" ", args, 2, args.Length - 2);

        if (!IsFactionLeaderOrFounder(factionId, playerId))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can add objectives.");
            return;
        }

        if (!factionObjectives.ContainsKey(factionId))
        {
            factionObjectives[factionId] = new List<string>();
        }

        if (!factionObjectives[factionId].Contains(objectiveText)) // Avoid duplicates
        {
            factionObjectives[factionId].Add(objectiveText);
            SaveObjectives();

            // Get the player's name
            string playerName = MyAPIGateway.Session.Player?.DisplayName ?? "Unknown";

            // Show updated quest log to all faction members
            ShowQuestLogToFaction(factionId, 10, $"Faction Objectives [Added by {playerName}: {objectiveText}]");

            MyAPIGateway.Utilities.ShowMessage("Objectives", $"{playerName} added objective: {objectiveText}");
        }
        else
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "This objective already exists.");
        }
    }

    private void HandleListObjectives(long factionId)
    {
        if (!factionObjectives.ContainsKey(factionId) || factionObjectives[factionId] == null)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives found.");
            return;
        }

        var objectives = factionObjectives[factionId];
        if (objectives.Count == 0)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives found.");
            return;
        }

        MyAPIGateway.Utilities.ShowMessage("Objectives", "Faction Objectives:");
        for (int i = 0; i < objectives.Count; i++)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", $"{i + 1}. {objectives[i]}");
        }
    }

    private void HandleShowObjectives(long factionId, long playerId)
    {
        if (!factionObjectives.ContainsKey(factionId) || factionObjectives[factionId].Count == 0)
        {
            MyVisualScriptLogicProvider.SetQuestlog(false, "", playerId);
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to display.");
            return;
        }

        MyVisualScriptLogicProvider.SetQuestlog(true, "Faction Objectives", playerId);
        MyVisualScriptLogicProvider.RemoveQuestlogDetails(playerId);

        foreach (var objective in factionObjectives[factionId])
        {
            MyVisualScriptLogicProvider.AddQuestlogObjective(objective, false, true, playerId);
        }
    }

    private void HandleRemoveObjective(string[] args, long factionId, long playerId)
    {
        int index;
        if (args.Length < 3 || !int.TryParse(args[2], out index))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Please provide a valid objective index to remove.");
            return;
        }

        if (!factionObjectives.ContainsKey(factionId) || index < 1 || index > factionObjectives[factionId].Count)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Invalid objective index.");
            return;
        }

        if (!IsFactionLeaderOrFounder(factionId, playerId))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can remove objectives.");
            return;
        }

        var removedObjective = factionObjectives[factionId][index - 1];
        factionObjectives[factionId].RemoveAt(index - 1);
        SaveObjectives();

        // Get the player's name
        string playerName = MyAPIGateway.Session.Player?.DisplayName ?? "Unknown";

        // Show updated quest log to all faction members
        ShowQuestLogToFaction(factionId, 10, $"Faction Objectives [Removed by {playerName}: {removedObjective}]");

        MyAPIGateway.Utilities.ShowMessage("Objectives", $"{playerName} removed objective: {removedObjective}");
    }

    private void HandleBroadcast(string[] args, long factionId)
    {
        int duration;
        if (args.Length < 3 || !int.TryParse(args[2], out duration))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj broadcast <duration in seconds>");
            return;
        }

        if (!factionObjectives.ContainsKey(factionId) || factionObjectives[factionId].Count == 0)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to broadcast.");
            return;
        }

        var playerName = MyAPIGateway.Session.Player?.DisplayName ?? "Unknown";
        string customTitle = $"Faction Objectives [Broadcasted by {playerName}, {duration}s]";

        // Broadcast to faction members
        ShowQuestLogToFaction(factionId, duration, customTitle);

        MyAPIGateway.Utilities.ShowMessage("Objectives", $"Broadcasting objectives for {duration} seconds by {playerName}.");
    }

    private void HandleHideQuestLog(long playerId)
    {
        MyVisualScriptLogicProvider.SetQuestlog(false, "", playerId);
        MyAPIGateway.Utilities.ShowMessage("Objectives", "Quest log hidden.");
    }

    private void HandleNotifications(string[] args, long playerId)
    {
        if (args.Length < 3)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj notifications <on/off>");
            return;
        }

        string option = args[2].ToLower();
        if (option == "off")
        {
            if (notificationsDisabled.Contains(playerId))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications are already turned off.");
            }
            else
            {
                notificationsDisabled.Add(playerId);
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications turned off.");
            }
        }
        else if (option == "on")
        {
            if (!notificationsDisabled.Contains(playerId))
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications are already turned on.");
            }
            else
            {
                notificationsDisabled.Remove(playerId);
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications turned on.");
            }
        }
        else
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Invalid option. Usage: /obj notifications <on/off>");
        }
    }

    private void HandleClearObjectives(long factionId, long playerId)
    {
        if (!IsFactionLeaderOrFounder(factionId, playerId))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can clear all objectives.");
            return;
        }

        if (!factionObjectives.ContainsKey(factionId) || factionObjectives[factionId].Count == 0)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to clear.");
            return;
        }

        // Clear all objectives for the faction
        factionObjectives[factionId].Clear();
        SaveObjectives();

        // Get the player's name
        string playerName = MyAPIGateway.Session.Player?.DisplayName ?? "Unknown";

        // Notify all faction members about the cleared objectives
        ShowQuestLogToFaction(factionId, 10, $"[Cleared by {playerName}]");

        MyAPIGateway.Utilities.ShowMessage("Objectives", $"{playerName} cleared all objectives.");
    }

    private void ShowHelp()
    {
        string title = "PersistentFactionObjectives";
        string currentobjprefix = "Available Commands";
        string body = @"
/obj add <text> - Add a new objective (leaders only)
/obj list - List all objectives
/obj show - Show the quest log locally
/obj remove <index> - Remove an objective (leaders only)
/obj broadcast <time> - Show the quest log to all members for <time> seconds
/obj hide - Manually hide the quest log
/obj notifications on - Enable notifications
/obj notifications off - Disable notifications
/obj clear - Clear all objectives (leaders only)
/obj help - Show this help message
";
        string currentobj = "";

        MyAPIGateway.Utilities.ShowMissionScreen(title, currentobjprefix, currentobj, body);
    }

    private void ShowQuestLogForPlayer(long factionId, long playerId, int duration, string customTitle = "Faction Objectives")
    {
        if (!factionObjectives.ContainsKey(factionId)) return;

        var objectives = factionObjectives[factionId];

        // Show the quest log with the custom title
        MyVisualScriptLogicProvider.SetQuestlog(true, customTitle, playerId);
        MyVisualScriptLogicProvider.RemoveQuestlogDetails(playerId);

        // Add each objective
        foreach (var objective in objectives)
        {
            MyVisualScriptLogicProvider.AddQuestlogObjective(objective, false, true, playerId);
        }

        // Set a timer to hide the quest log
        questLogHideTimes[playerId] = DateTime.UtcNow.AddSeconds(duration);
    }


    private void ShowQuestLogToFaction(long factionId, int duration, string customTitle = "Faction Objectives")
    {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction == null) return;

        foreach (var memberId in faction.Members.Keys)
        {
            if (notificationsDisabled.Contains(memberId)) continue;

            ShowQuestLogForPlayer(factionId, memberId, duration, customTitle);
        }
    }

    private bool IsFactionLeaderOrFounder(long factionId, long playerId)
    {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction == null)
        {
            MyLog.Default.WriteLineAndConsole($"Faction not found for ID {factionId}");
            return false;
        }

        MyFactionMember member;
        if (faction.Members.TryGetValue(playerId, out member))
        {
            return member.IsLeader || member.IsFounder;
        }

        MyLog.Default.WriteLineAndConsole($"Player {playerId} is not a member of faction {factionId}");
        return false;
    }
         
    private void SaveObjectives()
    {
        try
        {
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(PersistentFactionObjectives)))
            {
                foreach (var kvp in factionObjectives)
                {
                    foreach (var obj in kvp.Value)
                    {
                        writer.WriteLine($"{kvp.Key}:{obj}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLineAndConsole($"Error saving objectives: {ex.Message}");
        }
    }

    private void LoadObjectives()
    {
        try
        {
            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, typeof(PersistentFactionObjectives)))
                return;

            using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(PersistentFactionObjectives)))
            {
                string line;
                long factionId;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    if (parts.Length == 2 && long.TryParse(parts[0], out factionId))
                    {
                        if (!factionObjectives.ContainsKey(factionId))
                            factionObjectives[factionId] = new List<string>();

                        if (!factionObjectives[factionId].Contains(parts[1]))
                            factionObjectives[factionId].Add(parts[1]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLineAndConsole($"Error loading objectives: {ex.Message}");
        }
    }

}
