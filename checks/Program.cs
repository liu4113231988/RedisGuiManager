using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using RedisGuiManager;

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
        CheckRedisIfConfigured();
        Console.WriteLine("PASS: credential protection, legacy config, draft isolation, binary editing, failed-file protection");
    }
    static void CheckRedisIfConfigured()
    {
        string endpoint = Environment.GetEnvironmentVariable("REDIS_CHECK_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint)) { Console.WriteLine("SKIP: live Redis checks (set REDIS_CHECK_ENDPOINT for a disposable test server)"); return; }
        using var connection = StackExchange.Redis.ConnectionMultiplexer.Connect(endpoint);
        var db = connection.GetDatabase(0);
        string key = "RedisGuiManager-check:{" + Guid.NewGuid().ToString("N") + "}";
        var redisKey = new StackExchange.Redis.RedisKey[] { key };
        string Script(string relativePath)
        {
            string source = File.ReadAllText(Path.Combine("RedisGuiManager", relativePath));
            return System.Text.RegularExpressions.Regex.Match(source, "ScriptEvaluate\\(\\s*\"([^\"]+)\"").Groups[1].Value;
        }
        try
        {
            db.SetAdd(key, "old"); db.KeyExpire(key, TimeSpan.FromMinutes(5));
            db.ScriptEvaluate(Script("SetKey/SetValueControl.cs"), redisKey, new StackExchange.Redis.RedisValue[] { "old", "new" });
            Check(db.SetContains(key, "new") && !db.SetContains(key, "old") && db.KeyTimeToLive(key).HasValue, "Set replacement lost value or TTL");
            db.KeyDelete(key);
            db.SortedSetAdd(key, "old", 1); db.KeyExpire(key, TimeSpan.FromMinutes(5));
            db.ScriptEvaluate(Script("ZSetKey/ZSetValueControl.cs"), redisKey, new StackExchange.Redis.RedisValue[] { "old", "new", 2 });
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
            Console.WriteLine("PASS: live Redis atomic set/zset/list operations and String TTL");
        }
        finally { db.KeyDelete(key); }
    }

}
