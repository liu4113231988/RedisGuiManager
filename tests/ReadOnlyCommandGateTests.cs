using System;
using System.Collections.Generic;
using RedisGuiManager;
using Xunit;

namespace RedisGuiManager.Tests
{
    /// <summary>
    /// The console gate is the last line of defence for a read-only connection, so both
    /// directions matter: inspection commands must keep working, and anything that can write
    /// or hijack the connection must be refused.
    /// </summary>
    public class ReadOnlyCommandGateTests
    {
        [Theory]
        [InlineData("GET")]
        [InlineData("get key")]
        [InlineData("HGET h f")]
        [InlineData("HKEYS h")]
        [InlineData("HVALS h")]
        [InlineData("HRANDFIELD h 2")]
        [InlineData("ZRANGEBYSCORE z 0 1")]
        [InlineData("ZRANGEBYLEX z - +")]
        [InlineData("ZSCAN z 0")]
        [InlineData("SMISMEMBER s a b")]
        [InlineData("LPOS l x")]
        [InlineData("XRANGE s - +")]
        [InlineData("XREVRANGE s + -")]
        [InlineData("SCAN 0")]
        [InlineData("BITCOUNT k")]
        [InlineData("PFCOUNT hll")]
        [InlineData("GEODIST a b")]
        [InlineData("GEOHASH a b")]
        [InlineData("LCS a b")]
        [InlineData("DBSIZE")]
        [InlineData("INFO")]
        [InlineData("PING")]
        [InlineData("MEMORY USAGE k")]
        [InlineData("CONFIG GET maxmemory")]
        [InlineData("CLIENT LIST")]
        [InlineData("XINFO STREAM s")]
        [InlineData("OBJECT ENCODING k")]
        [InlineData("COMMAND DOCS")]
        [InlineData("ACL WHOAMI")]
        [InlineData("SLOWLOG GET 10")]
        [InlineData("LATENCY LATEST")]
        public void Allows_non_mutating_commands(string command)
        {
            Assert.True(Utils.IsReadOnlyCommandAllowed(command), command);
        }

        [Theory]
        [InlineData("SET k v")]
        [InlineData("SETEX k 1 v")]
        [InlineData("APPEND k v")]
        [InlineData("SETRANGE k 1 v")]
        [InlineData("GETSET k v")]
        [InlineData("DEL k")]
        [InlineData("UNLINK k")]
        [InlineData("EXPIRE k 1")]
        [InlineData("RENAME a b")]
        [InlineData("HSET h f v")]
        [InlineData("HDEL h f")]
        [InlineData("LPUSH l v")]
        [InlineData("LSET l 0 v")]
        [InlineData("LREM l 0 v")]
        [InlineData("SADD s m")]
        [InlineData("SREM s m")]
        [InlineData("ZADD z 1 m")]
        [InlineData("ZREM z m")]
        [InlineData("XADD s * f v")]
        [InlineData("XDEL s 1-1")]
        [InlineData("PFADD hll a")]
        [InlineData("FLUSHALL")]
        [InlineData("FLUSHDB")]
        [InlineData("SHUTDOWN")]
        public void Refuses_commands_that_write(string command)
        {
            Assert.False(Utils.IsReadOnlyCommandAllowed(command), command);
        }

        [Theory]
        // Hijack the connection or block it forever.
        [InlineData("SUBSCRIBE ch")]
        [InlineData("PSUBSCRIBE ch*")]
        [InlineData("SSUBSCRIBE ch")]
        [InlineData("MONITOR")]
        [InlineData("SYNC")]
        [InlineData("DEBUG OBJECT k")]
        // Execute writes server-side, defeating the read-only contract.
        [InlineData("EVAL \"return redis.call('SET',KEYS[1],'x')\" 1 k")]
        [InlineData("EVALSHA a 0")]
        [InlineData("FCALL f 0")]
        [InlineData("SCRIPT LOAD x")]
        [InlineData("FUNCTION LOAD x")]
        [InlineData("MIGRATE h 1")]
        [InlineData("RESTORE k 0 p d")]
        // These are "read" commands with a writing variant (SORT/GEORADIUS STORE).
        [InlineData("SORT k STORE d")]
        [InlineData("GEORADIUS g 0 0 1 km STORE d")]
        [InlineData("GEOSEARCH g FROMMEMBER m BYRADIUS 1 km ASC")]
        // Consumer-group commands mutate group state.
        [InlineData("XREADGROUP GROUP g c COUNT 1 STREAMS s >")]
        [InlineData("XACK s g 1-1")]
        [InlineData("XCLAIM s g c 0 1-1")]
        [InlineData("XTRIM s MAXLEN 1")]
        [InlineData("XGROUP CREATE s g 0")]
        // Mutating subcommands.
        [InlineData("CONFIG SET maxmemory 1")]
        [InlineData("CLIENT KILL ID 1")]
        [InlineData("CLIENT SETNAME x")]
        [InlineData("ACL SETUSER bob")]
        [InlineData("LATENCY RESET")]
        public void Refuses_commands_that_bypass_or_hijack_the_connection(string command)
        {
            Assert.False(Utils.IsReadOnlyCommandAllowed(command), command);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Refuses_empty_input(string command)
        {
            Assert.False(Utils.IsReadOnlyCommandAllowed(command));
        }

        [Fact]
        public void Is_case_insensitive_and_tolerates_extra_whitespace()
        {
            Assert.True(Utils.IsReadOnlyCommandAllowed("  GeT   key  "));
            Assert.True(Utils.IsReadOnlyCommandAllowed("config\tget\tmaxmemory"));
        }

        [Fact]
        public void Requires_a_known_subcommand_for_families()
        {
            // Bare "CONFIG" on its own is not a valid read.
            Assert.False(Utils.IsReadOnlyCommandAllowed("CONFIG"));
            Assert.True(Utils.IsReadOnlyCommandAllowed("CONFIG GET"));
        }
    }
}