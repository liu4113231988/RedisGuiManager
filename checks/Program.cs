using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using RedisGuiManager;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using StackExchange.Redis;

static class RegressionChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [STAThread]
    static void Main()
    {
        var settings = new RedisSettings { name = "test", host = "127.0.0.1", port = 6379, auth = "redis-secret", ssh_password = "ssh-secret", additional_dbs = new List<int> { 20 } };
        string json = JsonConvert.SerializeObject(settings);
        Check(!json.Contains("redis-secret") && !json.Contains("ssh-secret"), "Credentials saved in plaintext");
        var restored = JsonConvert.DeserializeObject<RedisSettings>(json);
        Check(restored.auth == settings.auth && restored.ssh_password == settings.ssh_password, "Credential round trip failed");
        Check(JsonConvert.DeserializeObject<RedisSettings>("{\"auth\":\"legacy\"}").auth == "legacy", "Legacy connection compatibility failed");
        using (var form = new FormRedisAdd(settings))
        {
            form.Settings.host = "changed";
            form.Settings.additional_dbs.Add(21);
            Check(settings.host == "127.0.0.1" && settings.additional_dbs.Count == 1, "Editing mutated original settings before confirmation");
        }
        using (var value = new ValueControl())
        {
            value.SetValue((StackExchange.Redis.RedisValue)new byte[] { 0xff, 0xfe });
            Check(!value.CanEditText, "Binary value is editable as lossy text");
            value.SetValue("hello");
            Check(value.CanEditText, "Text value cannot be edited");
        }
        string dir = Path.Combine(Path.GetTempPath(), "RedisGuiManager-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "broken.json");
        File.WriteAllText(file, "broken original contents");
        try
        {
            using var main = new FormMain();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            ((HashSet<string>)typeof(FormMain).GetField("_knownFiles", flags).GetValue(main)).Add(file);
            ((HashSet<string>)typeof(FormMain).GetField("_failedFiles", flags).GetValue(main)).Add(file);
            typeof(FormMain).GetMethod("SaveRedisSettings", flags).Invoke(main, null);
            Check(File.ReadAllText(file) == "broken original contents", "Failed connection file was overwritten");
        }
        finally { File.Delete(file); Directory.Delete(dir); }
        CheckFeatures();
        CheckRedisIfConfigured();
        Console.WriteLine("PASS: credential protection, legacy config, draft isolation, binary editing, failed-file protection");
    }
    // A connection is persisted either inside a group or at the top level, never both. Verify the
    // group editor preserves that invariant, since breaking it writes the same connection twice.
    static void CheckGroupEditing()
    {
        var alpha = new RedisSettings { name = "alpha", host = "10.0.0.1", port = 6379 };
        var beta = new RedisSettings { name = "beta", host = "10.0.0.2", port = 6379 };
        var gamma = new RedisSettings { name = "gamma", host = "10.0.0.3", port = 6379 };
        var existingGroup = new RedisGroup { name = "prod", type = "group", connections = new List<RedisSettings> { alpha } };
        var loose = new List<RedisSettings> { beta, gamma };
        var groups = new List<RedisGroup> { existingGroup };

        // Resolve members against the union, exactly as FormMain does.
        var allKnown = loose.Concat(groups.SelectMany(g => g.connections)).ToList();

        using var dialog = new FormGroups(groups, loose);
        var result = dialog.BuildResult(groups, allKnown);

        Check(result.Count == 1, "Group editing lost or invented a group");
        Check(result[0].name == "prod", "Existing group name not preserved");
        Check(result[0].connections.Count == 1 && result[0].connections[0].name == "alpha",
            "Existing group membership not preserved");
        Check(ReferenceEquals(result[0], existingGroup), "Group instance replaced, breaking the live tree");

        // Every member must resolve to a real connection object, and no connection may repeat.
        var memberNames = result.SelectMany(g => g.connections ?? new List<RedisSettings>()).Select(c => c.name).ToList();
        Check(memberNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == memberNames.Count,
            "A connection ended up in more than one group");

        // Ungrouped connections must stay out of every group.
        Check(!memberNames.Contains("beta", StringComparer.OrdinalIgnoreCase)
              && !memberNames.Contains("gamma", StringComparer.OrdinalIgnoreCase),
            "Top-level connection leaked into a group");
        Check(ReferenceEquals(result[0].connections[0], alpha),
            "Group member was cloned instead of reusing the live connection");
    }

    // Keyspace notifications arrive on "__keyspace@<db>__:<key>"; the db has to be pulled out of that
    // shape without swallowing the key or an "@" that appears inside the key itself.
    static void CheckKeyspaceChannelParsing()
    {
        var extract = typeof(FormPubSub).GetMethod("ExtractDatabase",
            BindingFlags.NonPublic | BindingFlags.Static);
        Check(extract != null, "Keyspace channel parser missing");

        Check((string)extract.Invoke(null, new object[] { "__keyspace@0__:mykey" }) == "0",
            "Database not extracted from a keyspace channel");
        Check((string)extract.Invoke(null, new object[] { "__keyspace@12__:some:key" }) == "12",
            "Multi-digit database not extracted from a keyspace channel");
        Check((string)extract.Invoke(null, new object[] { "__keyspace@3__:user@example.com" }) == "3",
            "An '@' inside the key confused the database parser");
        Check((string)extract.Invoke(null, new object[] { "no-at-sign" }) == "?",
            "Unexpected channel did not fall back to '?'");
    }

// The import must refuse a file that is not an export before writing anything, and the messages it
    // shows the user have to name the offending key rather than just the field.
    static void CheckImportFileInspection()
    {
        var inspect = typeof(FormMain).GetMethod("TryInspectImportFile",
            BindingFlags.NonPublic | BindingFlags.Static);
        Check(inspect != null, "Import file inspector missing");

        string temp = Path.Combine(Path.GetTempPath(), "rgm-import-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            bool accepted = (bool)inspect.Invoke(null, new object[] { temp, 0, null });
            Check(!accepted, "A missing file was accepted for import");

            File.WriteAllText(temp, "");
            accepted = (bool)inspect.Invoke(null, new object[] { temp, 0, null });
            Check(!accepted, "An empty file was accepted for import");

            File.WriteAllText(temp, "{\"not\":\"an array\"}");
            accepted = (bool)inspect.Invoke(null, new object[] { temp, 0, null });
            Check(!accepted, "A JSON object was accepted where an array is required");

            File.WriteAllText(temp, "[ {\"key\":\"a\"}, {\"key\":\"b\"}, ");
            accepted = (bool)inspect.Invoke(null, new object[] { temp, 0, null });
            Check(!accepted, "Truncated JSON was accepted for import");

            File.WriteAllText(temp, "[]");
            var args = new object[] { temp, 0, null };
            accepted = (bool)inspect.Invoke(null, args);
            Check(accepted && (int)args[1] == 0, "Empty array should be accepted with zero entries");

            File.WriteAllText(temp, "[{\"key\":\"a\",\"dump\":\"x\",\"pttl\":-1},{\"key\":\"b\"},{\"key\":\"c\"}]");
            args = new object[] { temp, 0, null };
            accepted = (bool)inspect.Invoke(null, args);
            Check(accepted && (int)args[1] == 3, $"Expected 3 entries, counted {(int)args[1]}");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        // Entry-level messages must name the key so a report row is actionable.
        var import = typeof(FormMain).GetMethod("ImportEntry", BindingFlags.NonPublic | BindingFlags.Static);
        var db = DispatchProxy.Create<IDatabase, RecordingDatabase>();
        var messages = new List<string>();
        var cases = new[]
        {
            new Dictionary<string, object> { ["key"] = "k1", ["dump"] = "!!!not base64!!!", ["pttl"] = -1L },
            new Dictionary<string, object> { ["key"] = "k2", ["dump"] = "AAAA" },
            new Dictionary<string, object> { ["key"] = "k3", ["dump"] = "AAAA", ["pttl"] = "abc" },
            new Dictionary<string, object> { ["key"] = "k4" },
            new Dictionary<string, object> { ["key"] = "k5", ["type"] = "Nope", ["value"] = "x" },
            new Dictionary<string, object> { ["key"] = "k6", ["type"] = "Hash", ["value"] = new[] { new { value = "no field here" } } },
            new Dictionary<string, object> { ["key"] = "k7", ["type"] = "List", ["value"] = new string[0] },
        };

        foreach (var testCase in cases)
        {
            try { import.Invoke(null, new object[] { db, testCase }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
            {
                messages.Add(ex.InnerException.Message);
            }
        }

        Check(messages.Count == cases.Length, $"Expected all {cases.Length} malformed entries to be rejected, got {messages.Count}");
        foreach (string message in messages)
        {
            Check(!message.Contains("Exception"), $"Import error leaked an exception type: {message}");
        }
        Check(messages.Any(m => m.Contains("k1")), "Base64 error did not name the key");
        Check(messages.Any(m => m.Contains("pttl") && m.Contains("k3")), "Non-numeric pttl error did not name the field and key");
        Check(messages.Any(m => m.Contains("k4")), "Entry without dump or value did not name the key");
        Check(messages.Any(m => m.Contains("Nope")), "Unsupported type error did not name the type");
        Check(messages.Any(m => m.Contains("field") && m.Contains("k6")), "Hash item error did not name the missing field");
    }

// The string-tools panel is built in code and docks to the bottom; make sure it does not swallow
    // the editor above it or spill outside its own group box.
    static void CheckStringToolsLayout()
    {
        using var editor = new StringValueControl();
        var group = editor.Controls.OfType<GroupBox>().FirstOrDefault();
        Check(group != null, "String tools panel missing");
        Check(group.Dock == DockStyle.Bottom, "String tools panel is not docked to the bottom");

        var list = group.Controls.OfType<ListBox>().FirstOrDefault();
        Check(list != null, "String tools results list missing");

        var buttons = group.Controls.OfType<Button>().ToArray();
        Check(buttons.Length >= 6, $"Expected the bit/HyperLogLog/LCS buttons, found {buttons.Length}");

        // Buttons must sit below the results list, not on top of it.
        int listBottom = list.Bottom;
        foreach (var button in buttons)
        {
            Check(button.Top >= listBottom,
                $"Button '{button.Text}' overlaps the results list (button top {button.Top}, list bottom {listBottom})");
            Check(button.Right <= group.ClientSize.Width,
                $"Button '{button.Text}' spills outside the panel");
        }
    }

    static void CheckFeatures()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var settings = new RedisSettings { host = "127.0.0.1", port = 6379, username = "inspector", read_only = true };
        var client = new RedisClient(settings);
        var config = (ConfigurationOptions)typeof(RedisClient).GetField("config", flags).GetValue(client);
        Check(config.User == "inspector", "ACL username not passed to Redis");
        var copy = JsonConvert.DeserializeObject<RedisSettings>(JsonConvert.SerializeObject(settings));
        Check(copy.username == "inspector" && copy.read_only, "ACL/readonly settings not persisted");
        CheckGroupEditing();
        CheckKeyspaceChannelParsing();
        CheckStringToolsLayout();
        using (var root = new Panel())
        using (var editor = new ValueControl())
        {
            var save = new Button { Name = "button_save" };
            root.Controls.Add(editor); root.Controls.Add(save);
            client.ApplyReadOnly(root);
            Check(!save.Enabled && editor.ConnectionReadOnly, "Read-only UI not enforced");
            editor.SetValue("original");
            var text = (TextBoxBase)editor.Controls.Find("textBox_value", true)[0];
            text.Text = "edited";
            Check(editor.IsDirty && editor.OriginalValue == "original", "Dirty state lost original snapshot");
            editor.AcceptChanges();
            Check(!editor.IsDirty, "Dirty state not reset after successful save");
        }
        using (var page = new PageNavigator())
        {
            var values = page.Read(Enumerable.Range(0, 2000), CancellationToken.None);
            Check(values.Length == PageNavigator.PageSize + 1 && values[0] == 0, "Unbounded collection page read");
            bool canceled = false;
            try { page.Read(Enumerable.Range(0, 100), new CancellationToken(true)); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Page cancellation ignored");

            var navigate = typeof(PageNavigator).GetMethod("NavigateAsync",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // No handler yet: the offset must roll back because nothing committed the page.
            navigate.Invoke(page, new object[] { 500 });
            Check(page.Offset == 0, "Failed/canceled navigation changed page");

            Func<Task> commitNow = () => { page.UpdatePage(true); return Task.CompletedTask; };
            page.PageChanged += commitNow;
            ((Task)navigate.Invoke(page, new object[] { 500 })).GetAwaiter().GetResult();
            Check(page.Offset == 500, "Committed navigation did not advance the offset");
            Check(page.Read(Enumerable.Range(0, 2000), CancellationToken.None)[0] == 500, "Next page has wrong offset");

            page.CanNavigate = () => false;
            ((Task)navigate.Invoke(page, new object[] { 1000 })).GetAwaiter().GetResult();
            Check(page.Offset == 500, "Unsaved-edit veto ignored during paging");

            // Swap in a handler that commits late; the navigator must await it before the offset sticks.
            page.CanNavigate = null;
            page.PageChanged -= commitNow;
            var gate = new TaskCompletionSource<bool>();
            page.PageChanged += async () => { await gate.Task; page.UpdatePage(true); };
            var pending = (Task)navigate.Invoke(page, new object[] { 1000 });
            Check(!pending.IsCompleted, "NavigateAsync did not await the async page handler");
            // A second navigation while one is in flight must be ignored, not interleaved.
            ((Task)navigate.Invoke(page, new object[] { 1500 })).GetAwaiter().GetResult();
            gate.SetResult(true);
            pending.GetAwaiter().GetResult();
            Check(page.Offset == 1000, "Re-entrant navigation interleaved two page loads");
        }

        // Cursor retention: deep paging must reuse server-side cursors instead of replaying.
        var cursors = new CursorStack<string>();
        cursors.Reset("0");
        Check(cursors.HasCursor(0) && cursors.Get(0) == "0", "Cursor stack did not seed page 0");
        Check(!cursors.HasCursor(1), "Cursor stack invented a cursor for an unvisited page");
        cursors.Set(1, "42");
        Check(cursors.Get(1) == "42", "Cursor stack lost a recorded cursor");
        Check(cursors.Get(0) == "0", "Reading page 1 disturbed the page 0 cursor");
        bool threw = false;
        try { cursors.Get(5); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "Cursor stack returned a cursor for a page it never visited");
        // Walking forward then trimming must forget the future but keep the visited prefix.
        cursors.Set(2, "99");
        cursors.TrimTo(2);
        Check(cursors.Get(1) == "42" && !cursors.HasCursor(2), "TrimTo discarded the visited prefix");
        cursors.Reset("7");
        Check(cursors.Get(0) == "7" && cursors.Count == 1, "Reset did not clear retained cursors");
        // Going backwards after Reset must not resurrect stale cursors.
        Check(!cursors.HasCursor(1), "Reset left stale cursors behind");

        // Read-only console gate: must refuse anything that can mutate, hijack the connection,
        // or execute writes server-side, while still allowing the common inspection commands.
        string[] allowed = { "GET", "get key", "HKEYS h", "ZRANGEBYSCORE z 0 1", "SMISMEMBER s m",
            "XRANGE s - +", "SCAN 0", "INFO", "DBSIZE", "CONFIG GET maxmemory", "CLIENT LIST",
            "XINFO STREAM s", "OBJECT ENCODING k", "MEMORY USAGE k", "COMMAND DOCS" };
        string[] denied = { "SET k v", "DEL k", "HSET h f v", "FLUSHALL", "EVAL \"return 1\" 0",
            "SCRIPT LOAD x", "SUBSCRIBE ch", "PSUBSCRIBE ch*", "MONITOR", "CONFIG SET maxmemory 1",
            "CLIENT KILL ID 1", "SORT k", "RESTORE k 0 p d", "MIGRATE h 1", "GEORADIUS g 0 0 1 km STORE d",
            "XREADGROUP GROUP g c COUNT 1 STREAMS s >", "XACK s g 1-1", "FLUSHDB", "SHUTDOWN" };
        foreach (string cmd in allowed)
        {
            Check(Utils.IsReadOnlyCommandAllowed(cmd), $"Read-only mode blocked a safe command: {cmd}");
        }
        foreach (string cmd in denied)
        {
            Check(!Utils.IsReadOnlyCommandAllowed(cmd), $"Read-only mode allowed a write/blocking command: {cmd}");
        }

        var database = DispatchProxy.Create<IDatabase, RecordingDatabase>();
        var recording = (RecordingDatabase)(object)database;
        recording.StreamEntries = Enumerable.Range(1, 1200).Select(i => new StreamEntry($"{i}-0", Array.Empty<NameValueEntry>())).ToArray();
        var entries = client.ScanStream(database, "stream").ToArray();
        Check(entries.Length == 1200 && entries.Select(e => e.Id.ToString()).Distinct().Count() == 1200, "Stream pagination duplicated or dropped boundary entries");
        var import = typeof(FormMain).GetMethod("ImportEntry", BindingFlags.NonPublic | BindingFlags.Static);
        var entry = new Dictionary<string, object> { ["key"] = "test", ["type"] = "Stream", ["value"] = new[] { new { id = "0-0", fields = new[] { new { name = "f", value = "v" } } } } };
        bool rejected = false;
        try { import.Invoke(null, new object[] { database, entry }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
        Check(rejected && recording.Writes == 0, "Invalid stream import wrote partial data");
        entry = new Dictionary<string, object> { ["key"] = "test", ["dump"] = Convert.ToBase64String(new byte[] { 1 }), ["pttl"] = 0L };
        Check(!(bool)import.Invoke(null, new object[] { database, entry }) && recording.Writes == 0, "Expired import became permanent");
        recording.Exists = true;
        entry["pttl"] = 1000L;
        Check(!(bool)import.Invoke(null, new object[] { database, entry }) && recording.Writes == 0, "Import overwrote existing key");

        // An export record carries a best-effort readable "value" alongside the authoritative
        // "dump". The dump must win, otherwise a truncated or binary value would corrupt a restore.
        recording.Exists = false;
        var writesBeforeMixed = recording.Writes;
        entry = new Dictionary<string, object>
        {
            ["key"] = "test",
            ["type"] = "String",
            ["value"] = "readable-but-possibly-truncated",
            ["dump"] = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
            ["pttl"] = -1L
        };
        Check((bool)import.Invoke(null, new object[] { database, entry }), "Import refused a record carrying both dump and value");
        Check(recording.Writes == writesBeforeMixed + 1,
            "Import did not restore via the dump when a readable value was also present");
        Check(recording.UsedKeyRestore,
            "Readable value was used for restore instead of the dump payload");

        // The readable-only path must still work for the legacy format.
        recording.Exists = false;
        entry = new Dictionary<string, object> { ["key"] = "legacy", ["type"] = "String", ["value"] = "plain" };
        Check((bool)import.Invoke(null, new object[] { database, entry }), "Legacy readable-only import failed");
        Check(recording.UsedKeyRestore == false, "Legacy import did not rebuild the value from commands");
        CheckImportFileInspection();
        var firstDb = DispatchProxy.Create<IDatabase, RecordingDatabase>();
        var secondDb = DispatchProxy.Create<IDatabase, RecordingDatabase>();
        ((RecordingDatabase)(object)firstDb).StringValue = "original";
        var boundClient = new RedisClient(new RedisSettings { host = "127.0.0.1", port = 6379 }) { Redis = firstDb };
        using (var editor = new StringValueControl())
        {
            editor.SetNewKey(boundClient, "bound-key").GetAwaiter().GetResult();
            boundClient.Redis = secondDb;
            ((TextBoxBase)editor.Controls.Find("textBox_value", true)[0]).Text = "edited";
            typeof(StringValueControl).GetMethod("button_save_Click", flags).Invoke(editor, new object[] { null, EventArgs.Empty });

            // button_save_Click is an async void handler, so give the save a moment to land.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (((RecordingDatabase)(object)firstDb).Writes == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }

            Check(((RecordingDatabase)(object)firstDb).Writes == 1 && ((RecordingDatabase)(object)secondDb).Writes == 0, "Editor saved into a different selected database");
        }
        using (var form = new FormRedisAdd(settings))
        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
        {
            var username = (TextBox)typeof(FormRedisAdd).GetField("usernameInput", flags).GetValue(form);
            Check(username.Bottom < form.ClientSize.Height, "ACL input clipped outside dialog");
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-30000, -30000);
            form.Show();
            Application.DoEvents();
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "connection-settings.png"));
        }
        Console.WriteLine("PASS: ACL, readonly UI, dirty tracking, bounded pages, cancellation, safe import validation");
    }

    static void CheckRedisIfConfigured()
    {
        string endpoint = Environment.GetEnvironmentVariable("REDIS_CHECK_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint)) { Console.WriteLine("SKIP: live Redis checks (set REDIS_CHECK_ENDPOINT for a disposable test server)"); return; }
        using var connection = StackExchange.Redis.ConnectionMultiplexer.Connect(endpoint);
        var db = connection.GetDatabase(0);
        var server = connection.GetServer(connection.GetEndPoints()[0]);
        Console.WriteLine($"CONNECTED: {server.EndPoint}, Redis {server.Version}, PING {db.Ping().TotalMilliseconds:F0} ms");
        string key = "RedisGuiManager-check:{" + Guid.NewGuid().ToString("N") + "}";
        string restoredKey = key + ":restored";
        var redisKey = new StackExchange.Redis.RedisKey[] { key };
        string Script(string relativePath)
        {
            string source = File.ReadAllText(Path.Combine("RedisGuiManager", relativePath));
            return System.Text.RegularExpressions.Regex.Match(source, "ScriptEvaluate\\(\\s*\"([^\"]+)\"").Groups[1].Value;
        }
        try
        {
            var unsupported = new List<string>();
            byte[] binary = { 0, 0xff, 0xfe, 65 };
            db.StringSet(key, binary, TimeSpan.FromMinutes(5));
            var import = typeof(FormMain).GetMethod("ImportEntry", BindingFlags.NonPublic | BindingFlags.Static);
            var entry = new Dictionary<string, object> { ["key"] = restoredKey, ["keyBytes"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(restoredKey)), ["dump"] = Convert.ToBase64String(db.KeyDump(key)), ["pttl"] = 300000L };
            Check((bool)import.Invoke(null, new object[] { db, entry }), "Snapshot import failed");
            Check(((byte[])db.StringGet(restoredKey)).SequenceEqual(binary) && db.KeyTimeToLive(restoredKey).HasValue, "Snapshot import changed binary data or lost TTL");
            Check(!(bool)import.Invoke(null, new object[] { db, entry }), "Snapshot import did not skip existing key");
            Check(!db.KeyRename(key, restoredKey, When.NotExists) && db.KeyExists(key), "Rename overwrote existing target");
            Check(server.Keys(0, key + "*", 100).Distinct().Count() == 2, "Key scanning missed test keys");
            db.KeyDelete(key);
            var pagingClient = new RedisClient(new RedisSettings { host = "127.0.0.1", port = 6379 }) { Redis = db };
            try
            {
                for (int i = 1; i <= 1200; i++) db.StreamAdd(key, "field", "value", $"{i}-0");
                var stream = pagingClient.ScanStream(db, key).ToArray();
                Check(stream.Length == 1200 && stream.Select(e => e.Id).Distinct().Count() == 1200, "Live stream pagination duplicated or lost entries");
                Console.WriteLine("PASS: live Redis 1200-entry stream pagination");
            }
            catch (RedisServerException ex)
            {
                unsupported.Add("Stream: " + ex.Message);
                Console.WriteLine("BLOCKED: Stream: " + ex.Message);
            }
            db.KeyDelete(key);
            db.HashSet(key, Enumerable.Range(0, 1200).Select(i => new HashEntry($"f{i}", $"v{i}")).ToArray());
            using (var page = new PageNavigator())
            {
                var members = page.Read(db.HashScan(key, pageSize: 500), CancellationToken.None);
                Check(members.Length == 501 && members.Select(e => e.Name).Distinct().Count() == 501, "Live hash page has wrong size or duplicate fields");
            }
            db.KeyDelete(key);
            Console.WriteLine("PASS: live Redis binary snapshot import, TTL, existing-key protection, key/hash scanning");
            try { db.ScriptEvaluate("return 1"); }
            catch (RedisServerException ex)
            {
                unsupported.Add("Lua: " + ex.Message);
                Console.WriteLine("BLOCKED: Lua: " + ex.Message);
                throw new InvalidOperationException("Server capabilities blocked checks: " + string.Join("; ", unsupported));
            }
            db.SetAdd(key, "old"); db.KeyExpire(key, TimeSpan.FromMinutes(5));
            db.ScriptEvaluate(Script("SetKey/SetValueControl.cs"), redisKey, new StackExchange.Redis.RedisValue[] { "old", "new" });
            Check(db.SetContains(key, "new") && !db.SetContains(key, "old") && db.KeyTimeToLive(key).HasValue, "Set replacement lost value or TTL");
            db.KeyDelete(key);
            db.SortedSetAdd(key, "old", 1); db.KeyExpire(key, TimeSpan.FromMinutes(5));
            db.ScriptEvaluate(Script("ZSetKey/ZSetValueControl.cs"), redisKey, new StackExchange.Redis.RedisValue[] { "old", "new", 2, 1 });
            Check(db.SortedSetScore(key, "new") == 2 && db.KeyTimeToLive(key).HasValue, "Sorted set replacement lost score or TTL");
            db.KeyDelete(key);
            db.ListRightPush(key, new StackExchange.Redis.RedisValue[] { "first", "last" });
            string listScript = Script("ListKey/ListValueControl.cs");
            bool rejected = false;
            try { db.ScriptEvaluate(listScript, redisKey, new StackExchange.Redis.RedisValue[] { 0, "changed", "marker" }); }
            catch (StackExchange.Redis.RedisServerException) { rejected = true; }
            Check(rejected && db.ListGetByIndex(key, 0) == "first", "Concurrent list change was not protected");
            db.ScriptEvaluate(listScript, redisKey, new StackExchange.Redis.RedisValue[] { 0, "first", "marker" });
            Check(db.ListLength(key) == 1 && db.ListGetByIndex(key, 0) == "last", "List deletion failed");
            db.KeyDelete(key);
            db.StringSet(key, "old", TimeSpan.FromMinutes(5));
            var client = new RedisClient(new RedisSettings { host = "127.0.0.1", port = 6379 }) { Redis = db };
            using var control = new StringValueControl();
            control.SetNewKey(client, key);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(StringValueControl).GetMethod("button_save_Click", flags).Invoke(control, new object[] { null, EventArgs.Empty });
            Check(db.KeyTimeToLive(key).HasValue, "String save removed TTL");
            string SavingScript(string relativePath)
            {
                string source = File.ReadAllText(Path.Combine("RedisGuiManager", relativePath));
                int start = source.IndexOf("private void button_save_Click", StringComparison.Ordinal);
                return System.Text.RegularExpressions.Regex.Match(source.Substring(start), "ScriptEvaluate\\(\\s*\"([^\"]+)\"").Groups[1].Value;
            }
            db.KeyDelete(key);
            db.StringSet(key, "server-new", TimeSpan.FromMinutes(5));
            rejected = false;
            try { db.ScriptEvaluate(SavingScript("StringKey/StringValueControl.cs"), redisKey, new RedisValue[] { "old", "overwrite" }); }
            catch (RedisServerException) { rejected = true; }
            Check(rejected && db.StringGet(key) == "server-new" && db.KeyTimeToLive(key).HasValue, "String conflict overwrote newer value");
            db.KeyDelete(key);
            db.HashSet(key, "field", "server-new");
            rejected = false;
            try { db.ScriptEvaluate(SavingScript("HashKey/HashValueControl.cs"), redisKey, new RedisValue[] { "field", "old", "overwrite" }); }
            catch (RedisServerException) { rejected = true; }
            Check(rejected && db.HashGet(key, "field") == "server-new", "Hash conflict overwrote newer value");
            db.KeyDelete(key);
            db.ListRightPush(key, "server-new");
            rejected = false;
            try { db.ScriptEvaluate(SavingScript("ListKey/ListValueControl.cs"), redisKey, new RedisValue[] { 0, "old", "overwrite" }); }
            catch (RedisServerException) { rejected = true; }
            Check(rejected && db.ListGetByIndex(key, 0) == "server-new", "List conflict overwrote newer value");
            Console.WriteLine("PASS: live Redis atomic set/zset/list operations and String TTL");
            Check(unsupported.Count == 0, "Server capabilities blocked checks: " + string.Join("; ", unsupported));
        }
        finally
        {
            db.KeyDelete(new StackExchange.Redis.RedisKey[] { key, restoredKey });
            Console.WriteLine("CLEANUP: removed test keys");
        }
    }

}

public class RecordingDatabase : DispatchProxy
{
    public bool Exists;
    public int Writes;
    public RedisValue StringValue = "";
    public StreamEntry[] StreamEntries = Array.Empty<StreamEntry>();

    /// <summary>True when the import restored via RESTORE rather than rebuilding from commands.</summary>
    public bool UsedKeyRestore;

    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "KeyRestore") { UsedKeyRestore = true; Writes++; return true; }
        if (method.Name == "KeyExists") return Exists;
        if (method.Name == "KeyExists") return Exists;
        if (method.Name == "StreamRange")
        {
            string min = ((RedisValue)args[1]).ToString();
            long first = min == "-" ? 0 : long.Parse(min.Split('-')[0]);
            return StreamEntries.Where(e => long.Parse(e.Id.ToString().Split('-')[0]) >= first).Take(Convert.ToInt32(args[3])).ToArray();
        }
        if (method.Name == "StringGet") return StringValue;
        if (method.Name == "StringGetAsync") return Task.FromResult(StringValue);
        if (method.Name == "Execute") { if ((string)args[0] == "RESTORE") Writes++; return RedisResult.Create((RedisValue)5); }
        if (method.Name == "ScriptEvaluate" || method.Name == "ScriptEvaluateAsync")
        {
            Writes++;
            UsedKeyRestore = false;
            if (((string)args[0]).Contains("redis.call('GET'")) StringValue = ((RedisValue[])args[2])[1];
            RedisResult result = RedisResult.Create((RedisValue)1);
            return method.Name == "ScriptEvaluateAsync" ? Task.FromResult(result) : (object)result;
        }
        throw new NotSupportedException(method.Name);
    }
}
