using Renci.SshNet;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RedisGuiManager.Properties;

namespace RedisGuiManager
{
    public class RedisClient
    {
        private ConnectionMultiplexer connection = null;
        private ConfigurationOptions config = null;
        private RedisSettings settings = null;

        public event Action<string> ConnectionStatusChanged;
        public bool IsConnected => connection?.IsConnected == true;
        // Cluster-aware multiplexer. Pub/Sub must go through the multiplexer rather than a
        // single IServer, otherwise subscriptions only reach the first endpoint.
        public ConnectionMultiplexer Multiplexer => connection;
        public bool CanWrite()
        {
            if (!Settings.read_only) return true;
            System.Windows.Forms.MessageBox.Show(UiText.ReadOnlyConnection, UiText.ReadOnlyTitle);
            return false;
        }
        public void ApplyReadOnly(System.Windows.Forms.Control root)
        {
            if (root is ValueControl value) value.ConnectionReadOnly = Settings.read_only;
            if (root is System.Windows.Forms.Button button && new[] { "button_save", "button_insert_row", "button_delete_row", "button_delete", "button_ttl", "button_rename" }.Contains(button.Name))
                button.Enabled = !Settings.read_only;
            foreach (System.Windows.Forms.Control child in root.Controls) ApplyReadOnly(child);
        }
        public SshClient TunnelSsh { get; set; }
        public ForwardedPortLocal Tunnel { get; set; }
        public int DBBlock { get; set; }
        public IDatabase Redis { get; set; }
        public IServer RedisServer { get; set; }
        public RedisSettings Settings
        {
            get
            {
                return settings;
            }
            set
            {
                settings = value;

                config = BuildConfiguration(settings.host, settings.port, false);
            }
        }

        public RedisClient(RedisSettings settings)
        {
            Settings = settings;
        }

        private ConfigurationOptions BuildConfiguration(string ip_address, int port, bool isTunnel, int connectTimeout = 5000)
        {
            ConfigurationOptions cfg = new ConfigurationOptions()
            {
                // Keep the multiplexer alive when the server is unreachable so StackExchange.Redis
                // can restore the link on its own instead of leaving a dead client behind.
                AbortOnConnectFail = false,
                ConnectRetry = 3,
                ReconnectRetryPolicy = new ExponentialRetry(5000),
                KeepAlive = 30,
                ConnectTimeout = connectTimeout,
                DefaultDatabase = 0,
                Password = settings.auth,
                User = string.IsNullOrWhiteSpace(settings.username) ? null : settings.username,
                Ssl = settings.use_ssl,
                SslHost = settings.use_ssl ? settings.host : null
            };

            if (isTunnel)
            {
                cfg.EndPoints.Add(ip_address, port);
            }
            else if (settings.use_cluster)
            {
                var endpoints = ParseClusterEndpoints();
                if (endpoints.Count == 0)
                {
                    cfg.EndPoints.Add(ip_address, port);
                }
                else
                {
                    foreach (EndPoint ep in endpoints)
                    {
                        cfg.EndPoints.Add(ep);
                    }
                }
            }
            else
            {
                cfg.EndPoints.Add(ip_address, port);
            }

            return cfg;
        }

        private List<EndPoint> ParseClusterEndpoints()
        {
            var result = new List<EndPoint>();
            if (string.IsNullOrWhiteSpace(settings.cluster_endpoints))
            {
                return result;
            }

            foreach (string item in settings.cluster_endpoints.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = item.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int idx = trimmed.LastIndexOf(':');
                if (idx <= 0) continue;

                string host = trimmed.Substring(0, idx).Trim();
                string port_str = trimmed.Substring(idx + 1).Trim();
                if (string.IsNullOrEmpty(host) || !int.TryParse(port_str, out int port)) continue;

                result.Add(new DnsEndPoint(host, port));
            }

            return result;
        }

        public OperateResult Connect()
        {
            if (config == null)
            {
                return new OperateResult(false, "\r\nInvalid configuration");
            }

            string ip_address = settings.host;
            int port = settings.port;

            ConfigurationOptions temp_config = BuildConfiguration(ip_address, port, false);

            if (settings.use_tunnel && !settings.use_cluster)
            {
                try
                {
                    ConnectionInfo sshConnInfo = null;
                    if (settings.use_ssh_key == false)
                    {
                        sshConnInfo = new PasswordConnectionInfo(settings.ssh_host, settings.ssh_port, settings.ssh_user, settings.ssh_password)
                        {
                            Timeout = TimeSpan.FromSeconds(60)
                        };
                    }
                    else
                    {
                        sshConnInfo = new PrivateKeyConnectionInfo(settings.ssh_host, settings.ssh_port, settings.ssh_user, new PrivateKeyFile(settings.ssh_key))
                        {
                            Timeout = TimeSpan.FromSeconds(60)
                        };
                    }

                    TunnelSsh = new SshClient(sshConnInfo);

                    ip_address = "127.0.0.1";
                    port = Utils.FreeTcpPort();

                    // ssh
                    TunnelSsh.Connect();

                    // tunnel
                    Tunnel = new ForwardedPortLocal(ip_address, (uint)port, settings.host, (uint)settings.port);
                    TunnelSsh.AddForwardedPort(Tunnel);
                    Tunnel.Start();

                    if (Tunnel.IsStarted == false)
                    {
                        string message = "\r\nTunnel not started";
                        Close();
                        return new OperateResult(false, message);
                    }

                    temp_config = BuildConfiguration(ip_address, port, true, 60000);
                    temp_config.SyncTimeout = 60000;
                    temp_config.AsyncTimeout = 60000;
                }
                catch (System.Exception ex)
                {
                    Close();
                    return new OperateResult(false, "\r\nTunnel connection fail\r\n" + ex.ToString());
                }
            }

            try
			{
                ReportStatus("Connecting…");
                connection = ConnectionMultiplexer.Connect(temp_config);

                // AbortOnConnectFail is off so the multiplexer can recover later, which means
                // Connect can hand back a client that is not usable yet. Verify explicitly.
                if (!connection.IsConnected)
                {
                    Close();
                    return new OperateResult(false, $"\r\nConnection fail\r\nCannot reach {string.Join(", ", config.EndPoints.Select(e => e.ToString()))}");
                }

                connection.ConnectionFailed += (s, e) => ReportStatus("Disconnected; reconnecting…");
                connection.ConnectionRestored += (s, e) => ReportStatus("Connected");
            }
            catch (RedisConnectionException ex)
			{
                Close();
                return new OperateResult(false, "\r\nConnection fail\r\n" + ex.ToString());
			}
            catch (RedisException ex)
            {
                Close();
                return new OperateResult(false, "\r\nConnection fail\r\n" + ex.ToString());
            }

            if (settings.use_cluster)
            {
                var ep = connection.GetEndPoints().FirstOrDefault();
                RedisServer = ep != null ? connection.GetServer(ep) : null;
            }
            else
            {
                RedisServer = connection.GetServer(string.Format("{0}:{1}", ip_address, port));
            }
            Redis = connection.GetDatabase();

            ReportStatus("Connected");
            return new OperateResult(true, "");
        }

        // Status text is cosmetic; a broken subscriber must never take down a Redis operation.
        private void ReportStatus(string message)
        {
            try
            {
                ConnectionStatusChanged?.Invoke(message);
            }
            catch (Exception)
            {
            }
        }

        public void Close()
        {
            // Report through the guarded helper first: a throwing subscriber must not prevent the
            // multiplexer and the SSH session from being released.
            ReportStatus("Disconnected");

            try
            {
                connection?.Dispose();
            }
            catch
            {
            }
            finally
            {
                connection = null;
                Redis = null;
                RedisServer = null;
            }

            try
            {
                // A tunnel that failed to start still owns its native handle.
                Tunnel?.Dispose();
            }
            catch
            {
            }
            finally
            {
                Tunnel = null;
            }

            try
            {
                TunnelSsh?.Dispose();
            }
            catch
            {
            }
            finally
            {
                TunnelSsh = null;
            }
        }

        /// <summary>
        /// Enumerates matching keys without throwing. Prefer <see cref="TryScanKeys"/> when the
        /// caller needs to tell "no matches" apart from "the server was unreachable".
        /// </summary>
        public IEnumerable<StackExchange.Redis.RedisKey> ScanKeys(int database, string pattern = "*", int pageSize = 1000)
        {
            return TryScanKeys(database, pattern, pageSize, out var keys) ? keys : Array.Empty<StackExchange.Redis.RedisKey>();
        }

        public bool TryScanKeys(int database, string pattern, int pageSize, out IEnumerable<StackExchange.Redis.RedisKey> keys)
        {
            keys = Array.Empty<StackExchange.Redis.RedisKey>();

            if (connection == null || !connection.IsConnected)
            {
                ReportStatus("Not connected");
                return false;
            }

            if (!settings.use_cluster)
            {
                if (RedisServer == null)
                {
                    ReportStatus("Server is not available");
                    return false;
                }

                keys = RedisServer.Keys(database, pattern, pageSize);
                return true;
            }

            if (database != 0)
            {
                ReportStatus("Cluster supports DB 0 only");
                return false;
            }

            if (!TryClusterPrimaryServers(out var servers))
            {
                return false;
            }

            keys = servers.SelectMany(s => s.Keys(0, pattern, pageSize)).Distinct();
            return true;
        }

        /// <summary>
        /// Enumerates a whole stream from the beginning. Replays the stream on every call, so paging
        /// should use <see cref="StreamPageAfter"/> instead; kept because the regression checks
        /// exercise it for boundary-duplication correctness.
        /// </summary>
        public IEnumerable<StreamEntry> ScanStream(IDatabase database, string key)
        {
            RedisValue min = "-";
            while (true)
            {
                var page = database.StreamRange(key, minId: min, count: PageNavigator.PageSize + 1);
                foreach (var entry in page) if (entry.Id != min) yield return entry;
                if (page.Length <= PageNavigator.PageSize) yield break;
                min = page[page.Length - 1].Id;
            }
        }

        /// <summary>
        /// Reads one hash page starting from <paramref name="cursor"/> and returns the cursor to
        /// resume from. Unlike <see cref="IDatabase.HashScan(RedisKey, int)"/> this keeps the
        /// server-side cursor, so paging to page N does not replay the scan from the beginning.
        /// </summary>
        public HashScanPage HashScanPage(string key, string cursor, int count)
        {
            var raw = (RedisResult[])Redis.Execute("HSCAN", key, cursor, "COUNT", count);
            string next = (string)raw[0];
            var flat = (RedisResult[])raw[1];
            var entries = new List<HashEntry>((flat.Length + 1) / 2);
            for (int i = 0; i + 1 < flat.Length; i += 2)
            {
                entries.Add(new HashEntry((RedisValue)flat[i], (RedisValue)flat[i + 1]));
            }

            return new HashScanPage(next, entries);
        }

        /// <summary>Set counterpart of <see cref="HashScanPage"/>.</summary>
        public SetScanPage SetScanPage(string key, string cursor, int count)
        {
            var raw = (RedisResult[])Redis.Execute("SSCAN", key, cursor, "COUNT", count);
            string next = (string)raw[0];
            var flat = (RedisResult[])raw[1];
            var members = new List<RedisValue>(flat.Length);
            foreach (var item in flat) members.Add((RedisValue)item);

            return new SetScanPage(next, members);
        }

        /// <summary>
        /// Reads entries strictly after <paramref name="afterId"/>. Pass null for the first page.
        /// This replaces the replay-based <see cref="ScanStream"/>, which re-read the whole stream
        /// every time the user moved to another page.
        /// </summary>
        public StreamPage StreamPageAfter(IDatabase database, string key, RedisValue? afterId, int count)
        {
            RedisValue min = afterId == null ? (RedisValue)"-" : afterId.Value;

            // IDatabase.StreamRange has no exclusive-minimum overload, and minId is inclusive, so
            // ask for one extra entry and drop the boundary entry ourselves.
            var raw = database.StreamRange(key, minId: min, count: count + 2);
            int start = afterId == null ? 0 : 1;

            var entries = new List<StreamEntry>(count);
            for (int i = start; i < raw.Length && entries.Count < count; i++)
            {
                entries.Add(raw[i]);
            }

            return new StreamPage(raw.Length - start > count, entries);
        }

        private bool TryClusterPrimaryServers(out IServer[] servers)
        {
            servers = Array.Empty<IServer>();
            if (connection == null || !connection.IsConnected)
            {
                ReportStatus("Not connected");
                return false;
            }

            servers = connection.GetEndPoints().Select(ep => connection.GetServer(ep)).Where(s => !s.IsReplica).ToArray();
            if (servers.Length == 0 || servers.Any(s => !s.IsConnected))
            {
                ReportStatus("Not all cluster primary nodes are connected");
                servers = Array.Empty<IServer>();
                return false;
            }

            return true;
        }

        public long DatabaseSize(int database)
        {
            return TryDatabaseSize(database, out long size) ? size : -1;
        }

        public bool TryDatabaseSize(int database, out long size)
        {
            size = -1;
            if (connection == null || !connection.IsConnected)
            {
                ReportStatus("Not connected");
                return false;
            }

            if (settings.use_cluster)
            {
                if (!TryClusterPrimaryServers(out var servers)) return false;
                size = servers.Sum(s => s.DatabaseSize(database));
                return true;
            }

            if (RedisServer == null)
            {
                ReportStatus("Server is not available");
                return false;
            }

            size = RedisServer.DatabaseSize(database);
            return true;
        }

        public OperateResult SelectDB(int db_num)
        {
            if (connection == null)
            {
                return new OperateResult(false, "Redis not connected");
            }

            Redis = connection.GetDatabase(db_num);
            DBBlock = db_num;

            return new OperateResult(true, "");
        }

        public IDatabase GetDB(int db_num)
        {
            if (connection == null)
            {
                return null;
            }

            return connection.GetDatabase(db_num);
        }
    }
}
