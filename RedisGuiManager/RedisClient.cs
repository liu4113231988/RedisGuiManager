using Renci.SshNet;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RedisGuiManager
{
    public class RedisClient
    {
        private ConnectionMultiplexer connection = null;
        private ConfigurationOptions config = null;
        private RedisSettings settings = null;

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
                AbortOnConnectFail = true,
                ConnectTimeout = connectTimeout,
                DefaultDatabase = 0,
                Password = settings.auth,
                Ssl = settings.use_ssl
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
                        sshConnInfo = new PrivateKeyConnectionInfo(settings.ssh_host, settings.ssh_user, new PrivateKeyFile(settings.ssh_key))
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
                        return new OperateResult(false, "\r\nTunnel not started");
                    }

                    temp_config = BuildConfiguration(ip_address, port, true, 60000);
                    temp_config.SyncTimeout = 60000;
                    temp_config.AsyncTimeout = 60000;
                }
                catch (System.Exception ex)
                {
                    return new OperateResult(false, "\r\nTunnel connection fail\r\n" + ex.ToString());
                }
            }

            try
			{
                connection = ConnectionMultiplexer.Connect(temp_config);
            }
            catch (RedisConnectionException ex)
			{
                return new OperateResult(false, "\r\nConnection fail\r\n" + ex.ToString());
			}
            catch (RedisException ex)
            {
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

            return new OperateResult(true, "");
        }

        public void Close()
        {
            if (connection != null)
            {
                connection.Close();
            }

            if (Tunnel != null && Tunnel.IsStarted)
            {
                Tunnel.Stop();
            }

            if (TunnelSsh != null && TunnelSsh.IsConnected)
            {
                TunnelSsh.Disconnect();
            }
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
