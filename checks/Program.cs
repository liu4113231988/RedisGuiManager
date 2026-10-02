using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using RedisGuiManager;
using System.Linq;
using System.Threading;
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
    static void CheckFeatures()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var settings = new RedisSettings { host = "127.0.0.1", port = 6379, username = "inspector", read_only = true };
        var client = new RedisClient(settings);
        var config = (ConfigurationOptions)typeof(RedisClient).GetField("config", flags).GetValue(client);
        Check(config.User == "inspector", "ACL username not passed to Redis");
        var copy = JsonConvert.DeserializeObject<RedisSettings>(JsonConvert.SerializeObject(settings));
        Check(copy.username == "inspector" && copy.read_only, "ACL/readonly settings not persisted");
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
            typeof(PageNavigator).GetMethod("Navigate", flags).Invoke(page, new object[] { 500 });
            Check(page.Offset == 0, "Failed/canceled navigation changed page");
            page.PageChanged += () => page.UpdatePage(true);
            typeof(PageNavigator).GetMethod("Navigate", flags).Invoke(page, new object[] { 500 });
            Check(page.Read(Enumerable.Range(0, 2000), CancellationToken.None)[0] == 500, "Next page has wrong offset");
            page.CanNavigate = () => false;
            typeof(PageNavigator).GetMethod("Navigate", flags).Invoke(page, new object[] { 1000 });
            Check(page.Offset == 500, "Unsaved-edit veto ignored during paging");
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
        var firstDb = DispatchProxy.Create<IDatabase, RecordingDatabase>();
        var secondDb = DispatchProxy.Create<IDatabase, RecordingDatabase>();
        ((RecordingDatabase)(object)firstDb).StringValue = "original";
        var boundClient = new RedisClient(new RedisSettings { host = "127.0.0.1", port = 6379 }) { Redis = firstDb };
        using (var editor = new StringValueControl())
        {
            editor.SetNewKey(boundClient, "bound-key");
            boundClient.Redis = secondDb;
            ((TextBoxBase)editor.Controls.Find("textBox_value", true)[0]).Text = "edited";
            typeof(StringValueControl).GetMethod("button_save_Click", flags).Invoke(editor, new object[] { null, EventArgs.Empty });
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
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "KeyExists") return Exists;
        if (method.Name == "StreamRange")
        {
            string min = ((RedisValue)args[1]).ToString();
            long first = min == "-" ? 0 : long.Parse(min.Split('-')[0]);
            return StreamEntries.Where(e => long.Parse(e.Id.ToString().Split('-')[0]) >= first).Take(Convert.ToInt32(args[3])).ToArray();
        }
        if (method.Name == "StringGet") return StringValue;
        if (method.Name == "Execute") { if ((string)args[0] == "RESTORE") Writes++; return RedisResult.Create((RedisValue)5); }
        if (method.Name == "ScriptEvaluate")
        {
            Writes++;
            if (((string)args[0]).Contains("redis.call('GET'")) StringValue = ((RedisValue[])args[2])[1];
            return RedisResult.Create((RedisValue)1);
        }
        throw new NotSupportedException(method.Name);
    }
}
