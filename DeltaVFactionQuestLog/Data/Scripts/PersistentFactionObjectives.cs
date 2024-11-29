using Sandbox.Game;
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
            long playerId = entry.Key;
            DateTime hideTime = entry.Value;

            if (hideTime <= now)
            {
                MyVisualScriptLogicProvider.SetQuestlog(false, "", playerId);
                playersToRemove.Add(playerId);
                questLogCountdowns.Remove(playerId); // Stop the countdown
            }
            else
            {
                int remainingSeconds = (int)(hideTime - now).TotalSeconds;
                string title = $"Faction Objectives (Hiding in {remainingSeconds}s)";
                MyVisualScriptLogicProvider.SetQuestlog(true, title, playerId); // Update the quest log title
            }
        }

        foreach (var playerId in playersToRemove)
        {
            questLogHideTimes.Remove(playerId);
        }
    }

    private void OnPlayerConnected(long playerId)
    {
        if (!isServer) return; // Only the server handles player connection events

        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
        if (playerFaction == null) return;

        var factionId = playerFaction.FactionId;

        // Show the quest log to the connected player
        ShowQuestLogForPlayer(factionId, playerId, 30); // Show for 30 seconds
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

        if (args[1].ToLower() == "help" || args[1].ToLower() == "motd")
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
            default:
                // Show a warning for invalid commands
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

        if (!factionObjectives[factionId].Contains(objectiveText)) // Avoid duplicate additions
        {
            factionObjectives[factionId].Add(objectiveText);
            SaveObjectives(); // Save changes to the file
            ShowQuestLogToFaction(factionId, 10); // Show log for 10 seconds
            MyAPIGateway.Utilities.ShowMessage("Objectives", $"Objective added: {objectiveText}");
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

        SaveObjectives(); // Save changes to the file
        ShowQuestLogToFaction(factionId, 10); // Show log for 10 seconds
        MyAPIGateway.Utilities.ShowMessage("Objectives", $"Removed objective: {removedObjective}");
    }

    private void HandleBroadcast(string[] args, long factionId)
    {
        int duration;
        if (args.Length < 3 || !int.TryParse(args[2], out duration))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj broadcast <duration in seconds>");
            return;
        }

        // Ensure the faction has objectives
        if (!factionObjectives.ContainsKey(factionId) || factionObjectives[factionId].Count == 0)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives to broadcast.");
            return;
        }

        // Show the quest log to all members of the faction
        ShowQuestLogToFaction(factionId, duration);

        // Provide feedback to the command issuer
        MyAPIGateway.Utilities.ShowMessage("Objectives", $"Broadcasting objectives to faction members for {duration} seconds.");
    }

    private void HandleHideQuestLog(long playerId)
    {
        MyVisualScriptLogicProvider.SetQuestlog(false, "", playerId);
        MyAPIGateway.Utilities.ShowMessage("Objectives", "Quest log hidden.");
    }

    private void HandleNotifications(string[] args, long playerId)
    {
        if (args.Length < 3 || args[2].ToLower() != "off")
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj notifications off");
            return;
        }

        notificationsDisabled.Add(playerId);
        MyAPIGateway.Utilities.ShowMessage("Objectives", "Notifications turned off.");
    }

    private void ShowHelp()
    {
        // Define the mission screen content
        string title = "PersistentFactionObjectives";
        string currentobjprefix = "Available Commands";
        string body = @"
/obj add <text> - Add a new objective (leaders only)
/obj list - List all objectives
/obj show - Show the quest log locally
/obj remove <index> - Remove an objective (leaders only)
/obj broadcast <time> - Show the quest log to all members for <time> seconds
/obj hide - Manually hide the quest log
/obj notifications off - Disable automatic notifications
/obj help - Show this help message
";
        string currentobj = "";

        // Show the mission screen
        MyAPIGateway.Utilities.ShowMissionScreen(title, currentobjprefix, currentobj, body);
    }

    private void ShowQuestLogForPlayer(long factionId, long playerId, int duration = 10)
    {
        if (!factionObjectives.ContainsKey(factionId)) return;

        // Get objectives for the faction
        var objectives = factionObjectives[factionId];

        // Update the quest log title
        string title = $"Faction Objectives (Hiding in {duration}s)";
        MyVisualScriptLogicProvider.SetQuestlog(true, title, playerId);

        // Clear the existing details
        MyVisualScriptLogicProvider.RemoveQuestlogDetails(playerId);

        // Add each objective to the quest log
        foreach (var objective in objectives)
        {
            MyVisualScriptLogicProvider.AddQuestlogObjective(objective, false, true, playerId);
        }

        // Set a timer to hide the quest log
        if (isServer)
        {
            questLogHideTimes[playerId] = DateTime.UtcNow.AddSeconds(duration);
            questLogCountdowns[playerId] = duration;
        }
    }

    private void ShowQuestLogToFaction(long factionId, int duration = 10)
    {
        // Retrieve the faction by ID
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction == null) return;

        // Check if the faction has objectives
        if (!factionObjectives.ContainsKey(factionId) || factionObjectives[factionId].Count == 0)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives found to broadcast.");
            return;
        }

        // Broadcast the quest log to all faction members
        foreach (var memberId in faction.Members.Keys)
        {
            if (notificationsDisabled.Contains(memberId)) continue;

            ShowQuestLogForPlayer(factionId, memberId, duration);
        }
    }

    private bool IsFactionLeaderOrFounder(long factionId, long playerId)
    {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        MyFactionMember member;
        if (faction != null && faction.Members.TryGetValue(playerId, out member))
        {
            return member.IsLeader || member.IsFounder;
        }

        return false;
    }

    private void SaveObjectives()
    {
        try
        {
            var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(PersistentFactionObjectives));

            foreach (var kvp in factionObjectives)
            {
                foreach (var obj in kvp.Value)
                {
                    writer.WriteLine($"{kvp.Key}:{obj}");
                }
            }

            writer.Close();
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

            var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(PersistentFactionObjectives));
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split(new[] { ':' }, 2); // Ensure splitting into only two parts
                long factionId;
                if (parts.Length == 2 && long.TryParse(parts[0], out factionId))
                {
                    if (!factionObjectives.ContainsKey(factionId))
                        factionObjectives[factionId] = new List<string>();

                    if (!factionObjectives[factionId].Contains(parts[1])) // Avoid duplicates
                        factionObjectives[factionId].Add(parts[1]);
                }
            }
            reader.Close();
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLineAndConsole($"Error loading objectives: {ex.Message}");
        }
    }

}
