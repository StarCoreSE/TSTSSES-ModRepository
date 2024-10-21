using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using Sandbox.ModAPI;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids
{
    internal class Log
    {
        private class LogEntry
        {
            public string Message { get; set; }
            public int Count { get; set; }
            public DateTime FirstOccurrence { get; set; }
            public DateTime LastOccurrence { get; set; }
        }

        private static Log I;
        private readonly TextWriter _writer;
        private readonly ConcurrentDictionary<string, LogEntry> _cachedMessages;
        private readonly object _lockObject;
        private readonly int _flushIntervalSeconds;
        private DateTime _lastFlushTime;

        private Log()
        {
            var logFileName = MyAPIGateway.Session.IsServer ? "DynamicAsteroids_Server.log" : "DynamicAsteroids_Client.log";
            MyAPIGateway.Utilities.DeleteFileInGlobalStorage(logFileName);
            _writer = MyAPIGateway.Utilities.WriteFileInGlobalStorage(logFileName);
            _writer.WriteLine($"      Dynamic Asteroids - {(MyAPIGateway.Session.IsServer ? "Server" : "Client")} Debug Log\n===========================================\n");
            _writer.WriteLine($"{DateTime.UtcNow:HH:mm:ss}: Logger initialized for {(MyAPIGateway.Session.IsServer ? "Server" : "Client")}");
            _writer.Flush();

            _cachedMessages = new ConcurrentDictionary<string, LogEntry>();
            _flushIntervalSeconds = 10;
            _lockObject = new object();
            _lastFlushTime = DateTime.UtcNow;
        }

        public static void Info(string message)
        {
            if (AsteroidSettings.EnableLogging)
                I?.CacheLogMessage(message);
        }

        public static void Warning(string message)
        {
            if (AsteroidSettings.EnableLogging)
                I?.WriteToFile("WARNING: " + message);
        }

        public static void Exception(Exception ex, Type callingType, string prefix = "")
        {
            if (AsteroidSettings.EnableLogging)
                I?._LogException(ex, callingType, prefix);
        }

        public static void Init()
        {
            Close();
            I = new Log();
        }

        public static void Close()
        {
            if (I != null)
            {
                Info("Closing log writer.");
                I.FlushCache();
                I._writer.Close();
            }

            I = null;
        }

        public static void Update()
        {
            if (I != null && (DateTime.UtcNow - I._lastFlushTime).TotalSeconds >= I._flushIntervalSeconds)
            {
                I.FlushCache();
            }
        }

        private void CacheLogMessage(string message)
        {
            _cachedMessages.AddOrUpdate(
                message,
                new LogEntry
                {
                    Message = message,
                    Count = 1,
                    FirstOccurrence = DateTime.UtcNow,
                    LastOccurrence = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    existing.Count++;
                    existing.LastOccurrence = DateTime.UtcNow;
                    return existing;
                });
        }

        private void FlushCache()
        {
            lock (_lockObject)
            {
                var currentTime = DateTime.UtcNow;
                var entriesToRemove = new List<string>();

                foreach (var kvp in _cachedMessages)
                {
                    var entry = kvp.Value;
                    if ((currentTime - entry.LastOccurrence).TotalSeconds >= _flushIntervalSeconds)
                    {
                        string logMessage = entry.Count > 1
                            ? string.Format("[{0:HH:mm:ss}]: Repeated {1} times in {2:F1}s: {3}",
                                currentTime, entry.Count, (entry.LastOccurrence - entry.FirstOccurrence).TotalSeconds, entry.Message)
                            : string.Format("[{0:HH:mm:ss}]: {1}", currentTime, entry.Message);

                        WriteToFile(logMessage);
                        entriesToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in entriesToRemove)
                {
                    LogEntry removedEntry;
                    _cachedMessages.TryRemove(key, out removedEntry);
                }

                _lastFlushTime = currentTime;
            }
        }

        private void WriteToFile(string message)
        {
            _writer.WriteLine(message);
            _writer.Flush();
        }

        private void _LogException(Exception ex, Type callingType, string prefix = "")
        {
            if (ex == null)
            {
                WriteToFile("Null exception! CallingType: " + callingType.FullName);
                return;
            }

            string exceptionMessage = prefix + string.Format("Exception in {0}! {1}\n{2}\n{3}",
                callingType.FullName, ex.Message, ex.StackTrace, ex.InnerException);

            WriteToFile(exceptionMessage);
            MyAPIGateway.Utilities.ShowNotification($"{ex.GetType().Name} in Dynamic Asteroids! Check logs for more info.", 10000, "Red");
        }
    }
}