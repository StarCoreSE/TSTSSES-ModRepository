using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Utils;
using VRage.ModAPI;
using System.Collections.Generic;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class FactionQuestLog : MySessionComponentBase
{
    private Dictionary<long, List<string>> factionQuestLogs = new Dictionary<long, List<string>>();

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
        if (!messageText.StartsWith("/quest")) return;

        sendToOthers = false;

        var args = messageText.Split(' ');
        if (args.Length < 2)
        {
            MyAPIGateway.Utilities.ShowMessage("QuestLog", "Usage: /quest <add|list> [text]");
            return;
        }

        var playerId = MyAPIGateway.Session.Player.IdentityId;
        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);

        if (playerFaction == null)
        {
            MyAPIGateway.Utilities.ShowMessage("QuestLog", "You are not in a faction.");
            return;
        }

        var factionId = playerFaction.FactionId;

        if (args[1].ToLower() == "add")
        {
            if (args.Length < 3)
            {
                MyAPIGateway.Utilities.ShowMessage("QuestLog", "Please provide a quest text.");
                return;
            }

            var questText = string.Join(" ", args, 2, args.Length - 2);
            if (!factionQuestLogs.ContainsKey(factionId))
            {
                factionQuestLogs[factionId] = new List<string>();
            }

            factionQuestLogs[factionId].Add(questText);
            MyAPIGateway.Utilities.ShowMessage("QuestLog", $"Quest added: {questText}");
        }
        else if (args[1].ToLower() == "list")
        {
            if (factionQuestLogs.ContainsKey(factionId) && factionQuestLogs[factionId].Count > 0)
            {
                MyAPIGateway.Utilities.ShowMessage("QuestLog", "Faction Quest Log:");
                foreach (var quest in factionQuestLogs[factionId])
                {
                    MyAPIGateway.Utilities.ShowMessage("QuestLog", $"- {quest}");
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowMessage("QuestLog", "No quests found.");
            }
        }
        else
        {
            MyAPIGateway.Utilities.ShowMessage("QuestLog", "Invalid command. Use /quest <add|list>.");
        }
    }
}
