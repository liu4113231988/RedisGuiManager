using System;
using System.Reflection;
using RedisGuiManager;
using Xunit;

namespace RedisGuiManager.Tests
{
    public class RedisGlobMatchTests
    {
        [Theory]
        [InlineData("*", "anything", true)]
        [InlineData("*", "", true)]
        [InlineData("user:*", "user:42", true)]
        [InlineData("user:*", "account:42", false)]
        [InlineData("user:?", "user:1", true)]
        [InlineData("user:?", "user:12", false)]
        [InlineData("a*c", "abc", true)]
        [InlineData("a*c", "abd", false)]
        [InlineData("h?llo", "hello", true)]
        [InlineData("h*llo", "heeeello", true)]
        [InlineData("prefix", "prefix", true)]
        [InlineData("prefix", "prefixx", false)]
        public void Matches_like_redis(string pattern, string value, bool expected)
        {
            Assert.Equal(expected, Utils.RedisGlobMatch(pattern, value));
        }
    }

    public class UtilsFormattingTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(999)]
        [InlineData(1024)]
        [InlineData(1536)]
        [InlineData(1048576)]
        [InlineData(1073741824)]
        public void Size_description_is_produced_for_any_input(int bytes)
        {
            string text = Utils.GetSizeDescription(bytes);

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(text.EndsWith("B"), "expected a unit suffix, got: " + text);
        }
    }

    public class VersionTests
    {
        [Fact]
        public void Version_matches_the_assembly_version()
        {
            // Utils.Version reads the entry assembly (RedisGuiManager), not the test assembly.
            string assemblyVersion = typeof(Utils).Assembly.GetName().Version?.ToString(3);

            Assert.Equal(assemblyVersion, Utils.Version);
        }

        [Fact]
        public void Version_is_not_the_hardcoded_placeholder()
        {
            // The UI used to be pinned to "1.0.0" while the project shipped 1.4.0.
            Assert.NotEqual("1.0.0", Utils.Version);
        }

        [Fact]
        public void Version_is_assignable_for_callers_that_override_it()
        {
            string original = Utils.Version;
            try
            {
                Utils.Version = "9.9.9";
                Assert.Equal("9.9.9", Utils.Version);
            }
            finally
            {
                Utils.Version = original;
            }
        }
    }

    public class CredentialSerializationTests
    {
        [Fact]
        public void Secrets_are_not_written_in_plaintext()
        {
            var settings = new RedisSettings
            {
                name = "test",
                host = "127.0.0.1",
                port = 6379,
                auth = "redis-secret",
                ssh_password = "ssh-secret",
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);

            Assert.DoesNotContain("redis-secret", json);
            Assert.DoesNotContain("ssh-secret", json);
        }

        [Fact]
        public void Legacy_payload_without_encryption_still_loads()
        {
            var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<RedisSettings>("{\"auth\":\"legacy\"}");

            Assert.Equal("legacy", settings.auth);
        }

        [Fact]
        public void Acl_and_readonly_flags_survive_a_round_trip()
        {
            var settings = new RedisSettings { host = "127.0.0.1", port = 6379, username = "inspector", read_only = true };

            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<RedisSettings>(
                Newtonsoft.Json.JsonConvert.SerializeObject(settings));

            Assert.Equal("inspector", copy.username);
            Assert.True(copy.read_only);
        }
    }

    /// <summary>
    /// Covers the parsing and formatting helpers behind the Server tools window - the parts that can
    /// be exercised without a live Redis server.
    /// </summary>
    public class RedisOpsTests
    {
        [Fact]
        public void ParseKeyValueLines_reads_memory_stats_shape()
        {
            var parsed = RedisOps.ParseKeyValueLines(
                "used_memory:1048576\r\nused_memory_peak:2097152\r\nmaxmemory_policy:noeviction");

            Assert.Equal(3, parsed.Count);
            Assert.Equal("1048576", parsed["used_memory"]);
            Assert.Equal("2097152", parsed["used_memory_peak"]);
            Assert.Equal("noeviction", parsed["maxmemory_policy"]);
        }

        [Fact]
        public void ParseKeyValueLines_is_case_insensitive_on_names()
        {
            Assert.Equal("42", RedisOps.ParseKeyValueLines("Used_Memory:42")["used_memory"]);
        }

        [Fact]
        public void ParseKeyValueLines_keeps_colons_inside_values()
        {
            // SENTINEL replies embed addresses such as "127.0.0.1:26379".
            Assert.Equal("127.0.0.1:26379", RedisOps.ParseKeyValueLines("ip:127.0.0.1:26379")["ip"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("no-separator-here")]
        [InlineData(":leading-separator")]
        public void ParseKeyValueLines_ignores_unusable_input(string blob)
        {
            Assert.Empty(RedisOps.ParseKeyValueLines(blob));
        }

        [Fact]
        public void ToPairs_reads_flat_sentinel_reply()
        {
            var pairs = RedisOps.ToPairs(StackExchange.Redis.RedisResult.Create((StackExchange.Redis.RedisValue)"name:mymaster"));

            Assert.Single(pairs);
            Assert.Equal("name", pairs[0].Key);
            Assert.Equal("mymaster", pairs[0].Value);
        }

        [Fact]
        public void ToPairs_returns_empty_for_null_result()
        {
            Assert.Empty(RedisOps.ToPairs(StackExchange.Redis.RedisResult.Create(
                (StackExchange.Redis.RedisValue)StackExchange.Redis.RedisValue.Null)));
        }

        [Fact]
        public void ToStringArray_reads_multi_bulk_result()
        {
            var bulk = StackExchange.Redis.RedisResult.Create(
                new StackExchange.Redis.RedisValue[] { "alpha", "beta" });

            Assert.Equal(new[] { "alpha", "beta" }, RedisOps.ToStringArray(bulk));
        }

        [Fact]
        public void ToStringArray_reads_single_bulk_result()
        {
            var bulk = StackExchange.Redis.RedisResult.Create((StackExchange.Redis.RedisValue)"solo");

            Assert.Equal(new[] { "solo" }, RedisOps.ToStringArray(bulk));
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(512, "512 B")]
        [InlineData(1024, "1.00 KB")]
        [InlineData(1536, "1.50 KB")]
        [InlineData(1048576, "1.00 MB")]
        [InlineData(1073741824, "1.00 GB")]
        public void FormatBytes_uses_binary_units(long bytes, string expected)
        {
            Assert.Equal(expected, RedisOps.FormatBytes(bytes));
        }

        [Fact]
        public void FormatBytes_marks_unknown_size_with_dash()
        {
            Assert.Equal("-", RedisOps.FormatBytes(-1));
        }

        [Fact]
        public void ClusterNodeRows_is_empty_for_a_missing_configuration()
        {
            Assert.Empty(RedisOps.ClusterNodeRows(null));
        }

        [Fact]
        public void ShortNodeId_truncates_long_ids_only()
        {
            Assert.Equal("abc12345",
                new ClusterNodeRow("abc12345", "h", "e", "master", "connected", "ok", "", 0).ShortNodeId);
            Assert.Equal("abc",
                new ClusterNodeRow("abc", "h", "e", "master", "connected", "ok", "", 0).ShortNodeId);
            Assert.Equal("",
                new ClusterNodeRow(null, "h", "e", "master", "connected", "ok", "", 0).ShortNodeId);
        }

        [Fact]
        public void Helpers_reject_a_missing_server_before_touching_redis()
        {
            Assert.Throws<ArgumentNullException>(() => RedisOps.ConfigGet(null, "*"));
            Assert.Throws<ArgumentNullException>(() => RedisOps.ConfigSet(null, "maxmemory", "0"));
            Assert.Throws<ArgumentNullException>(() => RedisOps.ClientList(null));
            Assert.Throws<ArgumentNullException>(() => RedisOps.ClientKill(null, 1));
            Assert.Throws<ArgumentNullException>(() => RedisOps.MemoryDoctor(null));
            Assert.Throws<ArgumentNullException>(() => RedisOps.MemoryStats(null));
            Assert.Throws<ArgumentNullException>(() => RedisOps.LastSave(null));
            Assert.Throws<ArgumentNullException>(() => RedisOps.ClusterConfiguration(null));
            Assert.Throws<ArgumentNullException>(() => RedisOps.SentinelMasters(null));
            Assert.Throws<ArgumentNullException>(() => RedisOps.MemoryUsage(null, "key"));
            Assert.Throws<ArgumentNullException>(() => RedisOps.ObjectInfo(null, "key"));
        }

        [Fact]
        public void OwnClientId_is_lenient_about_a_missing_server()
        {
            // The window calls this before the connection is known to be usable, so a null server
            // means "cannot determine" rather than a programming error.
            Assert.Null(RedisOps.OwnClientId(null));
        }

        [Fact]
        public void Helpers_validate_their_arguments()
        {
            // A non-null server is required before the value checks are reached.
            Assert.Throws<ArgumentNullException>(() => RedisOps.SentinelMaster(null, " "));
            Assert.Throws<ArgumentNullException>(() => RedisOps.SentinelSlaves(null, ""));
            Assert.Throws<ArgumentNullException>(() => RedisOps.ScriptExists(null, new[] { "abc" }));
        }
    }
}