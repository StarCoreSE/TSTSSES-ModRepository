using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicAsteroids {
    public class NetworkLogger {
        private readonly TextWriter _writer;
        private readonly string _component;
        private const int MESSAGE_CACHE_SIZE = 100;
        private readonly Queue<string> _recentMessages;
        private readonly object _lockObject = new object();

        public enum LogLevel {
            Debug,      // Detailed information for debugging
            Info,       // General connection/state information
            Warning,    // Potential issues that don't break functionality
            Error,      // Serious issues that affect functionality
            Critical    // Connection failures, desync, etc.
        }

        public Action<string> LogAction { get; private set; }

        public NetworkLogger(string component) {
            _component = component;
            _recentMessages = new Queue<string>(MESSAGE_CACHE_SIZE);
            var logFileName = string.Format("DynamicAsteroids_Network_{0}_{1}.log",
                MyAPIGateway.Session.IsServer ? "Server" : "Client",
                component);

            MyAPIGateway.Utilities.DeleteFileInGlobalStorage(logFileName);
            _writer = MyAPIGateway.Utilities.WriteFileInGlobalStorage(logFileName);
            WriteHeader();

            // Create the logging delegate
            LogAction = (message) => Log(LogLevel.Info, message);
        }

        private void WriteHeader() {
            _writer.WriteLine("===========================================");
            _writer.WriteLine("Dynamic Asteroids Network Log - " + _component);
            _writer.WriteLine("Session Type: " + (MyAPIGateway.Session.IsServer ? "Server" : "Client"));
            _writer.WriteLine("Start Time: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            _writer.WriteLine("===========================================\n");
            _writer.Flush();
        }

        public void Log(LogLevel level, string message, bool showNotification = false) {
            if (!AsteroidSettings.EnableLogging && level < LogLevel.Warning)
                return;

            lock (_lockObject) {
                var formattedMessage = FormatLogMessage(level, message);
                _writer.WriteLine(formattedMessage);
                _writer.Flush();

                if (_recentMessages.Count >= MESSAGE_CACHE_SIZE)
                    _recentMessages.Dequeue();
                _recentMessages.Enqueue(formattedMessage);

                if (showNotification || level >= LogLevel.Error) {
                    var color = level == LogLevel.Critical ? "Red" :
                               level == LogLevel.Error ? "Yellow" : "White";
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Network [{_component}]: {message}",
                        5000,
                        color);
                }
            }
        }

        private string FormatLogMessage(LogLevel level, string message) {
            return string.Format("[{0:HH:mm:ss}] [{1}] [{2}]: {3}",
                DateTime.UtcNow,
                level.ToString().ToUpper(),
                _component,
                message);
        }

        public void LogConnection(ulong steamId, string status) {
            Log(LogLevel.Info, string.Format("Player {0} {1}", steamId, status));
        }

        public void LogPacket(string packetType, long entityId, string details = null) {
            if (!AsteroidSettings.EnableLogging) return;

            Log(LogLevel.Debug, string.Format("Packet: {0}, ID: {1}{2}",
                packetType,
                entityId,
                details != null ? ", " + details : ""));
        }

        public void LogNetworkError(Exception ex, string context) {
            Log(LogLevel.Error, string.Format("Network error in {0}: {1}", context, ex.Message));
            _writer.WriteLine(ex.StackTrace);
            _writer.Flush();
        }

        public void Close() {
            lock (_lockObject) {
                Log(LogLevel.Info, "Closing network logger");
                _writer.Close();
            }
        }
    }
}
