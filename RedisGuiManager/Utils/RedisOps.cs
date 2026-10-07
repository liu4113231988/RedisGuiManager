using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RedisGuiManager
{
    /// <summary>
    /// Server-diagnostics helpers that StackExchange.Redis does not surface directly (MEMORY USAGE,
    /// OBJECT sub-commands, SENTINEL, script cache) plus thin, testable wrappers over the ones it
    /// does. Everything here is UI-free so it can be covered by unit tests.
    /// </summary>
    public static class RedisOps
    {
        // ---------- CONFIG ----------

        /// <summary>
        /// CONFIG GET. Sorted by name so repeated calls render in a stable order.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> ConfigGet(IServer server, string pattern)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            return server.ConfigGet(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim())
                .Select(pair => new KeyValuePair<string, string>(pair.Key.ToString(), pair.Value.ToString()))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void ConfigSet(IServer server, string name, string value)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Setting name is required.", nameof(name));

            server.ConfigSet(name.Trim(), value ?? string.Empty);
        }

        // ---------- CLIENTS ----------

        public static ClientInfo[] ClientList(IServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return server.ClientList();
        }

        /// <summary>
        /// Kills a client by id. <paramref name="skipMe"/> leaves the caller's own connection
        /// alone, which matters because the GUI holds an open multiplexer.
        /// </summary>
        public static long ClientKill(IServer server, long clientId, bool skipMe = true)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return server.ClientKill(clientId, null, null, skipMe);
        }

        /// <summary>
        /// The client id of this application's own connection, or null when it cannot be determined.
        /// Used to warn before the user kills the connection they are working through.
        /// </summary>
        public static long? OwnClientId(IServer server)
        {
            if (server == null) return null;

            try
            {
                RedisResult result = server.Execute("CLIENT", "ID");
                return result.IsNull ? (long?)null : (long)result;
            }
            catch (RedisException)
            {
                // CLIENT ID needs Redis 5.0+ and a non-cluster node.
                return null;
            }
        }

        // ---------- MEMORY ----------

        /// <summary>
        /// MEMORY USAGE. SE.Redis has no binding for this, so it goes through Execute.
        /// Returns null when the key does not exist.
        /// </summary>
        public static long? MemoryUsage(IDatabase database, StackExchange.Redis.RedisKey key, int samples = 5)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));

            RedisResult result = database.Execute(
                "MEMORY", "USAGE", key,
                "SAMPLES", Math.Max(0, samples));

            if (result.IsNull) return null;

            long value = (long)result;
            return value < 0 ? (long?)null : value;
        }

        public static string MemoryDoctor(IServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return server.MemoryDoctor();
        }

        /// <summary>
        /// MEMORY STATS returns a flat blob of "key:value" pairs; parsed so long, double and string
        /// values are all preserved as text for display.
        /// </summary>
        public static IReadOnlyDictionary<string, string> MemoryStats(IServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            return ParseKeyValueLines(server.MemoryStats().ToString());
        }

        // ---------- OBJECT ----------

        /// <summary>
        /// One OBJECT sub-command result. SE.Redis only binds OBJECT ENCODING, so the rest go
        /// through Execute. Missing keys and unsupported sub-commands come back as null.
        /// </summary>
        public static string ObjectSubCommand(IDatabase database, string subCommand, StackExchange.Redis.RedisKey key)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(subCommand)) throw new ArgumentException("Sub-command is required.", nameof(subCommand));

            try
            {
                RedisResult result = database.Execute("OBJECT", subCommand.Trim().ToUpperInvariant(), key);
                return result.IsNull ? null : result.ToString();
            }
            catch (RedisServerException)
            {
                // "no such key", or an unknown sub-command on this server version.
                return null;
            }
        }

        /// <summary>Runs every OBJECT sub-command the server supports and reports what came back.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> ObjectInfo(IDatabase database, StackExchange.Redis.RedisKey key)
        {
            var results = new List<KeyValuePair<string, string>>();
            foreach (string subCommand in new[] { "ENCODING", "REFCOUNT", "IDLETIME", "FREQ" })
            {
                string value = ObjectSubCommand(database, subCommand, key);
                if (value != null)
                {
                    results.Add(new KeyValuePair<string, string>(subCommand, value));
                }
            }

            return results;
        }

        // ---------- PERSISTENCE ----------

        public static DateTime LastSave(IServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return server.LastSave();
        }

        /// <summary>SAVE (blocking) or BGSAVE. Returns the server's reply.</summary>
        public static string Save(IDatabase database, bool background)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            return database.Execute(background ? "BGSAVE" : "SAVE").ToString();
        }

        /// <summary>BGREWRITEAOF, optionally switching appendonly off for the rewrite.</summary>
        public static string RewriteAppendOnlyFile(IDatabase database, bool disableAppendFsync)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));

            return disableAppendFsync
                ? database.Execute("BGREWRITEAOF", "appendonly", "no").ToString()
                : database.Execute("BGREWRITEAOF").ToString();
        }

        // ---------- CLUSTER ----------

        public static ClusterConfiguration ClusterConfiguration(IServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return server.ClusterNodes();
        }

        /// <summary>Flattens CLUSTER NODES into display-friendly rows.</summary>
        public static IReadOnlyList<ClusterNodeRow> ClusterNodeRows(ClusterConfiguration configuration)
        {
            var rows = new List<ClusterNodeRow>();
            if (configuration?.Nodes == null) return rows;

            foreach (ClusterNode node in configuration.Nodes)
            {
                bool isReplica = node.IsReplica;
                rows.Add(new ClusterNodeRow(
                    node.NodeId,
                    node.Hostname,
                    node.EndPoint?.ToString() ?? "",
                    node.IsMyself ? "myself" : isReplica ? "replica" : "master",
                    node.IsConnected ? "connected" : "disconnected",
                    node.IsFail || node.IsPossiblyFail ? "fail" : node.IsHandshake ? "handshake" : "ok",
                    node.IsFail ? "FAIL" : node.IsPossiblyFail ? "PFAIL" : "",
                    node.Slots?.Count ?? 0));
            }

            return rows
                .OrderByDescending(row => row.Role == "myself")
                .ThenBy(row => row.Role, StringComparer.Ordinal)
                .ThenBy(row => row.Endpoint, StringComparer.Ordinal)
                .ToList();
        }

        // ---------- SENTINEL ----------

        /// <summary>
        /// SENTINEL MASTERS. Only meaningful when the connection points at a Sentinel instance;
        /// callers surface the server's error message rather than swallowing it.
        /// </summary>
        public static string[] SentinelMasters(IServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return ToStringArray(server.Execute("SENTINEL", "MASTERS"));
        }

        public static IReadOnlyList<KeyValuePair<string, string>> SentinelMaster(IServer server, string masterName)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (string.IsNullOrWhiteSpace(masterName)) throw new ArgumentException("Master name is required.", nameof(masterName));

            return ToPairs(server.Execute("SENTINEL", "MASTER", masterName));
        }

        public static IReadOnlyList<KeyValuePair<string, string>> SentinelSlaves(IServer server, string masterName)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (string.IsNullOrWhiteSpace(masterName)) throw new ArgumentException("Master name is required.", nameof(masterName));

            return ToPairs(server.Execute("SENTINEL", "SLAVES", masterName));
        }

        public static IReadOnlyList<KeyValuePair<string, string>> SentinelSentinels(IServer server, string masterName)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (string.IsNullOrWhiteSpace(masterName)) throw new ArgumentException("Master name is required.", nameof(masterName));

            return ToPairs(server.Execute("SENTINEL", "SENTINELS", masterName));
        }

        // ---------- SCRIPT CACHE ----------

        /// <summary>SCRIPT EXISTS, one flag per supplied hash.</summary>
        public static IReadOnlyList<bool> ScriptExists(IServer server, IReadOnlyList<string> hashes)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (hashes == null || hashes.Count == 0) return Array.Empty<bool>();

            var arguments = new List<RedisValue>();
            arguments.AddRange(hashes.Select(hash => (RedisValue)hash));

            RedisResult result = server.Execute("SCRIPT", "EXISTS", arguments.ToArray());
            if (result.IsNull) return Array.Empty<bool>();

            var flags = (RedisResult[])result;
            var exists = new bool[flags.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                exists[i] = (long)flags[i] == 1;
            }

            return exists;
        }

        // ---------- SLOWLOG ----------

        /// <summary>
        /// SLOWLOG GET, including the client address and client name.
        /// <see cref="IServer.SlowlogGet"/> maps the reply onto CommandTrace, which drops the two
        /// client fields, so the raw reply is parsed here instead. Redis only reports them from
        /// 4.0 onwards; older servers return four fields per entry and leave these blank.
        /// </summary>
        public static IReadOnlyList<SlowlogEntryInfo> SlowlogEntries(IServer server, int count)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            var entries = new List<SlowlogEntryInfo>();
            foreach (string line in ToStringArray(server.Execute("SLOWLOG", "GET", Math.Max(1, count))))
            {
                // Each entry is itself a flat "value:value" block.
                var fields = ParseKeyValueLines(line);

                string arguments = fields.TryGetValue("command", out var command) ? command : "";

                entries.Add(new SlowlogEntryInfo(
                    ParseLong(fields, "id"),
                    ParseTimestamp(fields, "time"),
                    TimeSpan.FromMicroseconds(ParseLong(fields, "duration")),
                    arguments,
                    fields.TryGetValue("client_address", out var address) ? address : "",
                    fields.TryGetValue("client_name", out var name) ? name : ""));
            }

            return entries;
        }

        private static long ParseLong(IReadOnlyDictionary<string, string> fields, string name)
        {
            return fields.TryGetValue(name, out string value) && long.TryParse(value, out long parsed)
                ? parsed
                : 0;
        }

        private static DateTime ParseTimestamp(IReadOnlyDictionary<string, string> fields, string name)
        {
            return fields.TryGetValue(name, out string value) && long.TryParse(value, out long seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
                : DateTime.MinValue;
        }

        // ---------- HELPERS ----------

        /// <summary>
        /// Parses the flat "a:1\r\nb:2" blob used by MEMORY STATS and the SENTINEL replies.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ParseKeyValueLines(string blob)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(blob)) return result;

            foreach (string line in blob.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf(':');
                if (separator <= 0) continue;

                string name = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (name.Length > 0) result[name] = value;
            }

            return result;
        }

        /// <summary>Converts a RedisResult that may be a bulk or multi-bulk value into strings.</summary>
        public static string[] ToStringArray(RedisResult result)
        {
            if (result.IsNull) return Array.Empty<string>();

            ResultType type = result.Resp2Type;
            if (type == ResultType.Array)
            {
                var values = (RedisResult[])result;
                var strings = new string[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    strings[i] = values[i].IsNull ? string.Empty : values[i].ToString();
                }

                return strings;
            }

            return new[] { result.ToString() };
        }

        /// <summary>Converts a flat SENTINEL reply into name/value pairs.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> ToPairs(RedisResult result)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (string line in ToStringArray(result))
            {
                int separator = line.IndexOf(':');
                if (separator <= 0) continue;

                pairs.Add(new KeyValuePair<string, string>(
                    line.Substring(0, separator).Trim(),
                    line.Substring(separator + 1).Trim()));
            }

            return pairs;
        }

        /// <summary>Formats a byte count for display.</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "-";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", bytes, units[unit])
                : string.Format(CultureInfo.InvariantCulture, "{0:F2} {1}", size, units[unit]);
        }
    }

    /// <summary>One SLOWLOG entry, including the client fields the managed API does not surface.</summary>
    public readonly struct SlowlogEntryInfo
    {
        public SlowlogEntryInfo(long id, DateTime time, TimeSpan duration, string arguments, string clientAddress, string clientName)
        {
            Id = id;
            Time = time;
            Duration = duration;
            Arguments = arguments;
            ClientAddress = clientAddress;
            ClientName = clientName;
        }

        public long Id { get; }
        public DateTime Time { get; }
        public TimeSpan Duration { get; }
        public string Arguments { get; }
        public string ClientAddress { get; }
        public string ClientName { get; }

        /// <summary>Redis omits the name when the client did not set one via CLIENT SETNAME.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(ClientName) ? "(unnamed)" : ClientName;
    }

    /// <summary>One row of CLUSTER NODES, flattened for display.</summary>
    public readonly struct ClusterNodeRow
    {
        public ClusterNodeRow(string nodeId, string host, string endpoint, string role, string linkState, string failState, string failFlag, int slotCount)
        {
            NodeId = nodeId;
            Host = host;
            Endpoint = endpoint;
            Role = role;
            LinkState = linkState;
            FailState = failState;
            FailFlag = failFlag;
            SlotCount = slotCount;
        }

        public string NodeId { get; }
        public string Host { get; }
        public string Endpoint { get; }
        public string Role { get; }
        public string LinkState { get; }
        public string FailState { get; }
        public string FailFlag { get; }
        public int SlotCount { get; }

        /// <summary>Short node id, which is what operators actually recognise.</summary>
        public string ShortNodeId => NodeId != null && NodeId.Length > 8 ? NodeId.Substring(0, 8) : NodeId ?? "";
    }
}