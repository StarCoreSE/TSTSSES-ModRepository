using System;
using System.Collections.Generic;
using Invalid.DeltaVQuestLog.Commands;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace Invalid.DeltaVQuestLog
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class PersistentFactionObjectives : MySessionComponentBase
    {
        public static PersistentFactionObjectives I;

        private const string FileName = "FactionObjectives.txt";

        private Dictionary<long, QuestLogManager> _factionObjectives = new Dictionary<long, QuestLogManager>();
        private HashSet<long> notificationsDisabled = new HashSet<long>();
        private bool isServer;
        private Dictionary<long, DateTime> questLogHideTimes = new Dictionary<long, DateTime>();
        private Dictionary<long, int> questLogCountdowns = new Dictionary<long, int>();


        internal ObjectiveNetworking Network = new ObjectiveNetworking();

        public override void LoadData()
        {
            I = this;
            isServer = MyAPIGateway.Multiplayer.IsServer;
            bool isDedicated = MyAPIGateway.Utilities.IsDedicated;

            if (isServer && !isDedicated)
            {
                LoadObjectives();
            }
            else if (isDedicated)
            {
                LoadObjectives();
            }

            if (isServer)
            {
                MyVisualScriptLogicProvider.PlayerConnected += OnPlayerConnected;
            }

            Network.Init();
            CommandHandler.Init();
        }

        protected override void UnloadData()
        {
            CommandHandler.Close();
            Network.Close();

            if (isServer)
            {
                SaveObjectives();
                MyVisualScriptLogicProvider.PlayerConnected -= OnPlayerConnected;
            }

            I = null;
        }

        private int _ticks = 0;
        public override void UpdateAfterSimulation()
        {
            if (!isServer) return;
            _ticks++;

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

            if (_ticks % 10 == 0)
                foreach (var manager in _factionObjectives.Values)
                    manager.Update10();
        }

        private void OnPlayerConnected(long playerId)
        {
            if (!isServer) return;

            var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
            if (playerFaction == null) return;

            var factionId = playerFaction.FactionId;
            ShowQuestLogForPlayer(factionId, playerId, 30);
        }

        public QuestLogManager GetFactionManger(long factionId)
        {
            if (!_factionObjectives.ContainsKey(factionId))
                _factionObjectives[factionId] = new QuestLogManager(factionId);
            return _factionObjectives[factionId];
        }

        private void ShowQuestLogForPlayer(long factionId, long playerId, int duration, string customTitle = "Faction Objectives")
        {
            if (!factionObjectives.ContainsKey(factionId)) return;

            if (isServer)
            {
                try
                {
                    Network.SendQuestLogIndividual(new QuestLogMessage
                    {
                        FactionId = factionId,
                        Objectives = factionObjectives[factionId],
                        Title = customTitle,
                        Duration = duration
                    }, (ulong) playerId);
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole($"Error sending quest log message: {e}");
                }
            }
            else
            {
                DisplayQuestLog(factionObjectives[factionId], customTitle, playerId);
            }
        }

        private void DisplayQuestLog(List<string> objectives, string title, long playerId)
        {
            if (objectives == null || objectives.Count == 0)
            {
                MyVisualScriptLogicProvider.SetQuestlog(false, "", playerId);
                return;
            }

            MyVisualScriptLogicProvider.SetQuestlog(true, title, playerId);
            MyVisualScriptLogicProvider.RemoveQuestlogDetails(playerId);

            foreach (var objective in objectives)
            {
                MyVisualScriptLogicProvider.AddQuestlogObjective(objective, false, true, playerId);
            }
        }

        public void HandleClientQuestLogDisplay(QuestLogMessage message)
        {
            if (MyAPIGateway.Session.Player == null) return;

            var playerId = MyAPIGateway.Session.Player.IdentityId;
            DisplayQuestLog(message.Objectives, message.Title, playerId);

            if (message.Duration > 0)
            {
                questLogHideTimes[playerId] = DateTime.UtcNow.AddSeconds(message.Duration);
            }
        }

        public void ShowQuestLogToFaction(long factionId, int duration, string customTitle = "Faction Objectives")
        {
            var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
            if (faction == null) return;

            foreach (var memberId in faction.Members.Keys)
            {
                if (notificationsDisabled.Contains(memberId)) continue;

                ShowQuestLogForPlayer(factionId, memberId, duration, customTitle);
            }
        }

        public static bool IsFactionLeaderOrFounder(long factionId, long playerId)
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

}
