using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Utils;
using System.Collections.Generic;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class FactionObjectiveLog : MySessionComponentBase
{
    private Dictionary<long, List<string>> factionObjectives = new Dictionary<long, List<string>>();

    public override void LoadData()
    {
        MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
    }

    protected override void UnloadData()
    {
        MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
    }

    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
        if (!messageText.StartsWith("/obj") && !messageText.StartsWith("/objective")) return;

        sendToOthers = false;

        var args = messageText.Split(' ');
        if (args.Length < 2)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Usage: /obj <add|list|show|remove> [text|index]");
            return;
        }

        var playerId = MyAPIGateway.Session.Player.IdentityId;
        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);

        if (playerFaction == null)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "You are not in a faction.");
            return;
        }

        var factionId = playerFaction.FactionId;

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
            default:
                MyAPIGateway.Utilities.ShowMessage("Objectives", "Invalid command. Use /obj <add|list|show|remove>.");
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

        // Only faction leaders or founders can add objectives
        if (!IsFactionLeaderOrFounder(factionId, playerId))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can add objectives.");
            return;
        }

        if (!factionObjectives.ContainsKey(factionId))
        {
            factionObjectives[factionId] = new List<string>();
        }

        factionObjectives[factionId].Add(objectiveText);
        MyAPIGateway.Utilities.ShowMessage("Objectives", $"Objective added: {objectiveText}");
    }

    private void HandleListObjectives(long factionId)
    {
        if (factionObjectives.ContainsKey(factionId) && factionObjectives[factionId].Count > 0)
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Faction Objectives:");
            for (int i = 0; i < factionObjectives[factionId].Count; i++)
            {
                MyAPIGateway.Utilities.ShowMessage("Objectives", $"{i + 1}. {factionObjectives[factionId][i]}");
            }
        }
        else
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "No objectives found.");
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

        // Only faction leaders or founders can remove objectives
        if (!IsFactionLeaderOrFounder(factionId, playerId))
        {
            MyAPIGateway.Utilities.ShowMessage("Objectives", "Only faction leaders or founders can remove objectives.");
            return;
        }

        var removedObjective = factionObjectives[factionId][index - 1];
        factionObjectives[factionId].RemoveAt(index - 1);
        MyAPIGateway.Utilities.ShowMessage("Objectives", $"Removed objective: {removedObjective}");
    }

    private bool IsFactionLeaderOrFounder(long factionId, long playerId)
    {
        var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
        if (faction != null)
        {
            var member = faction.Members[playerId];
            return member.IsLeader || member.IsFounder;
        }

        return false;
    }
}
