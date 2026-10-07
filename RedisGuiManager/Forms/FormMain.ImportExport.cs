using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using RedisGuiManager.Properties;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Snapshot export and import of key data.

        private static Dictionary<string, object> BuildExportRecord(
            IDatabase database, StackExchange.Redis.RedisKey key, byte[] dump, long pttl)
        {
            var record = new Dictionary<string, object>
            {
                ["key"] = key.ToString(),
                ["keyBytes"] = Convert.ToBase64String((byte[])key),
                ["dump"] = Convert.ToBase64String(dump),
                ["pttl"] = pttl
            };

            try
            {
                StackExchange.Redis.RedisType type = database.KeyType(key);
                record["type"] = type.ToString();

                switch (type)
                {
                    case StackExchange.Redis.RedisType.String:
                        {
                            long length = database.StringLength(key);
                            if (length <= ExportValueLimit)
                            {
                                record["value"] = database.StringGet(key).ToString();
                            }
                            else
                            {
                                record["valueTruncated"] = true;
                            }
                        }
                        break;

                    case StackExchange.Redis.RedisType.Hash:
                        {
                            var entries = database.HashScan(key, pageSize: ExportValueLimit)
                                .ToArray();
                            bool truncated = database.HashLength(key) > entries.Length;
                            record["value"] = entries
                                .Select(entry => new Dictionary<string, object>
                                {
                                    ["field"] = entry.Name.ToString(),
                                    ["value"] = entry.Value.ToString()
                                })
                                .ToArray();
                            if (truncated) record["valueTruncated"] = true;
                        }
                        break;

                    case StackExchange.Redis.RedisType.List:
                        {
                            var items = database.ListRange(key, 0, ExportValueLimit - 1);
                            if (database.ListLength(key) > items.Length) record["valueTruncated"] = true;
                            record["value"] = items.Select(item => item.ToString()).ToArray();
                        }
                        break;

                    case StackExchange.Redis.RedisType.Set:
                        {
                            var members = database.SetScan(key, pageSize: ExportValueLimit).ToArray();
                            if (database.SetLength(key) > members.Length) record["valueTruncated"] = true;
                            record["value"] = members.Select(member => member.ToString()).ToArray();
                        }
                        break;

                    case StackExchange.Redis.RedisType.SortedSet:
                        {
                            var entries = database.SortedSetRangeByRankWithScores(key, 0, ExportValueLimit - 1);
                            if (database.SortedSetLength(key) > entries.Length) record["valueTruncated"] = true;
                            record["value"] = entries
                                .Select(entry => new Dictionary<string, object>
                                {
                                    ["member"] = entry.Element.ToString(),
                                    ["score"] = entry.Score
                                })
                                .ToArray();
                        }
                        break;

                    case StackExchange.Redis.RedisType.Stream:
                        {
                            var entries = database.StreamRange(key, count: ExportValueLimit);
                            if (database.StreamLength(key) > entries.Length) record["valueTruncated"] = true;
                            record["value"] = entries
                                .Select(entry => new Dictionary<string, object>
                                {
                                    ["id"] = entry.Id.ToString(),
                                    ["fields"] = entry.Values
                                        .Select(field => new Dictionary<string, object>
                                        {
                                            ["name"] = field.Name.ToString(),
                                            ["value"] = field.Value.ToString()
                                        })
                                        .ToArray()
                                })
                                .ToArray();
                        }
                        break;
                }
            }
            catch (RedisException)
            {
                // The dump is already captured, so a readable-value failure only costs the extra
                // fields. Binary keys in particular cannot round-trip through a JSON string.
            }

            return record;
        }

        private async void export_data_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selected = treeView_server.SelectedNode;
            if (selected == null || GetDbNode(selected)?.Tag is not DbSettings dbSettings) return;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            var database = client.GetDB(dbSettings.DBNumber);
            using var dialog = new SaveFileDialog { Filter = UiText.JsonFileFilter, FileName = string.Format(UiText.ExportFileNameFormat, dbSettings.DBNumber) };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string path = dialog.FileName;
            var outcome = await OperationDialog.RunAsync(this, UiText.ExportDataTitle, (token, progress) =>
            {
                var report = new OperationReport();
                string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var writer = new JsonTextWriter(new StreamWriter(temporary)) { Formatting = Formatting.Indented })
                    {
                        var serializer = new JsonSerializer();
                        writer.WriteStartArray();
                        foreach (var key in client.ScanKeys(dbSettings.DBNumber, "*", Config.scan_page_count))
                        {
                            if (token.IsCancellationRequested) { report.Canceled = true; break; }
                            try
                            {
                                // The DUMP payload stays authoritative for restore; type/value are
                                // added so the file is readable and greppable, as documented.
                                var snapshot = (RedisResult[])database.ScriptEvaluate(
                                    "local v=redis.call('DUMP',KEYS[1]); if not v then return redis.error_reply('Key disappeared') end; return {v,redis.call('PTTL',KEYS[1])}",
                                    new StackExchange.Redis.RedisKey[] { key });

                                serializer.Serialize(writer, BuildExportRecord(database, key, (byte[])snapshot[0], (long)snapshot[1]));
                                report.Success++;
                            }
                            catch (RedisException ex) { report.Errors.Add(string.Format(UiText.KeyedMessageFormat, key, ex.Message)); }
                            if ((report.Success + report.Errors.Count) % 100 == 0) progress.Report(string.Format(UiText.ExportProgressFormat, report.Success, report.Errors.Count));
                        }
                        writer.WriteEndArray();
                    }
                    if (report.Canceled && report.Success == 0) return report;
                    if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                    else File.Move(temporary, path);
                    return report;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            });
            if (!outcome.IsSuccess) return;
            var result = outcome.Value;
            result.Show(this, result.Canceled ? (result.Success > 0 ? UiText.ExportPartialSaved : UiText.ExportCanceledUnchanged) : UiText.ExportResultTitle);
        }

        private async void import_data_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            var selected = treeView_server.SelectedNode;
            if (selected == null || GetDbNode(selected)?.Tag is not DbSettings dbSettings) return;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            var database = client.GetDB(dbSettings.DBNumber);
            using var dialog = new OpenFileDialog { Filter = UiText.JsonFileFilter };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string path = dialog.FileName;

            // Check the file before touching Redis: a wrong file should not start a mutation, and
            // the confirmation is more useful when it can say how many entries are coming.
            if (TryInspectImportFile(path, out int entryCount, out string problem) == false)
            {
                MessageBox.Show(this, string.Format(UiText.ImportCannotImportFormat, Path.GetFileName(path), problem),
                    UiText.ImportInvalidTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (entryCount == 0)
            {
                MessageBox.Show(this, string.Format(UiText.ImportNoEntriesFormat, Path.GetFileName(path)),
                    UiText.ImportNothingTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, string.Format(UiText.ImportConfirmFormat, entryCount, Path.GetFileName(path), client.Settings.name, client.Settings.host, client.Settings.port, dbSettings.DBNumber), UiText.ConfirmImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var outcome = await OperationDialog.RunAsync(this, UiText.ImportDataTitle, (token, progress) =>
            {
                var report = new OperationReport();
                using var reader = new JsonTextReader(new StreamReader(path));
                var serializer = new JsonSerializer();
                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) throw new InvalidDataException(UiText.ImportExpectedArray);
                int index = 0;
                try
                {
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    if (token.IsCancellationRequested) { report.Canceled = true; break; }
                    index++;
                    // Parse a complete entry before writing; malformed JSON ends the file with a partial-result report.
                    Dictionary<string, object> entry;
                    try { entry = serializer.Deserialize<Dictionary<string, object>>(reader); }
                    catch (JsonException ex) { report.Errors.Add(string.Format(UiText.ImportEntryNotObjectFormat, index, ex.Message)); break; }
                    if (entry == null) { report.Errors.Add(string.Format(UiText.ImportEntryExpectedObjectFormat, index, reader.TokenType)); break; }

                    string keyName = entry.TryGetValue("key", out var keyValue) ? keyValue?.ToString() : null;
                    string label = keyName == null ? UiText.ImportNoKeyField : string.Format(UiText.ImportKeyLabelFormat, keyName);

                    try { if (ImportEntry(database, entry)) report.Success++; else report.Skipped++; }
                    catch (InvalidDataException ex) { report.Errors.Add(string.Format(UiText.ImportEntryErrorFormat, index, label, ex.Message)); }
                    catch (RedisException ex) { report.Errors.Add(string.Format(UiText.ImportEntryRejectedFormat, index, label, ex.Message)); }
                    catch (Exception ex) { report.Errors.Add(string.Format(UiText.ImportEntryErrorFormat, index, label, ex.Message)); }
                    if (index % 100 == 0) progress.Report(string.Format(UiText.ImportProgressFormat, index, report.Success, report.Skipped, report.Errors.Count));
                }
                    if (!report.Canceled && reader.TokenType != JsonToken.EndArray) report.Errors.Add(UiText.ImportUnexpectedEnd);
                }
                catch (JsonException ex) { report.Errors.Add(string.Format(UiText.ImportInvalidJsonFormat, ex.Message)); }
                return report;
            });
            if (!outcome.IsSuccess) return;
            outcome.Value.Show(this, UiText.ImportResultTitle);
            await RefreshDbKeysAsync(GetDbNode(selected), true);
        }

        /// <summary>
        /// Verifies the chosen file is a JSON array of entries and counts them, so the import can be
        /// refused before any key is written. Returns false with a message aimed at the user rather
        /// than a raw parser message.
        /// </summary>

        private static bool TryInspectImportFile(string path, out int entryCount, out string problem)
        {
            entryCount = 0;
            problem = null;

            try
            {
                if (File.Exists(path) == false)
                {
                    problem = UiText.ImportFileMissing;
                    return false;
                }

                if (new FileInfo(path).Length == 0)
                {
                    problem = UiText.ImportFileEmpty;
                    return false;
                }

                if (new FileInfo(path).Length > MaxImportBytes)
                {
                    problem = string.Format(UiText.ImportFileTooLargeFormat, MaxImportBytes / (1024 * 1024));
                    return false;
                }

                using var reader = new JsonTextReader(new StreamReader(path))
                {
                    // Keep strings as strings so entry values are not coerced to DateTime.
                    DateParseHandling = DateParseHandling.None
                };

                if (reader.Read() == false)
                {
                    problem = UiText.ImportFileNoJson;
                    return false;
                }

                if (reader.TokenType != JsonToken.StartArray)
                {
                    problem = string.Format(UiText.ImportFileNotArrayFormat, reader.TokenType);
                    return false;
                }

                // Count only the array's direct children. An entry's "value" may hold objects of its
                // own (Hash fields, Sorted Set members, Stream entries), and counting those would
                // inflate the total and could trip the entry limit on a perfectly valid file. Skip()
                // advances past a whole entry, so nested objects are never visited. It also keeps the
                // scan streaming instead of loading the file into memory.
                bool closed = false;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndArray) { closed = true; break; }
                    if (reader.TokenType == JsonToken.StartObject)
                    {
                        entryCount++;
                        // Refuse anything that could only have come from a runaway generator instead
                        // of loading it key by key.
                        if (entryCount > MaxImportEntries)
                        {
                            problem = string.Format(UiText.ImportFileTooManyEntriesFormat, MaxImportEntries.ToString("N0"));
                            return false;
                        }

                        reader.Skip();
                    }
                }

                if (closed == false)
                {
                    problem = UiText.ImportFileTruncated;
                    return false;
                }

                return true;
            }
            catch (JsonException ex)
            {
                problem = string.Format(UiText.ImportFileInvalidJsonFormat, ex.Message);
                return false;
            }
            catch (IOException ex)
            {
                problem = string.Format(UiText.ImportFileUnreadableFormat, ex.Message);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                problem = string.Format(UiText.ImportFileUnreadableFormat, ex.Message);
                return false;
            }
        }

        private static bool ImportEntry(IDatabase database, Dictionary<string, object> entry)
        {
            string key = entry != null && entry.TryGetValue("key", out var keyObj) ? keyObj?.ToString() : null;
            string type = entry != null && entry.TryGetValue("type", out var typeObj) ? typeObj?.ToString() : null;
            if (key == null) throw new InvalidDataException(UiText.ImportMissingKey);

            // Messages name the key so the report row is actionable without opening the file.
            string at = " (" + string.Format(UiText.ImportKeyLabelFormat, key) + ")";
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException(UiText.ImportEmptyKey);
            if (entry.ContainsKey("error")) throw new InvalidDataException(string.Format(UiText.ImportRecordedErrorFormat, at));

            // The dump is authoritative: it is the exact serialised form and preserves the TTL, so it must
            // win over the readable value even when an export carries both.
            if (entry.TryGetValue("dump", out var dump))
            {
                byte[] payload;
                try
                {
                    payload = Convert.FromBase64String(dump.ToString());
                }
                catch (FormatException)
                {
                    throw new InvalidDataException(string.Format(UiText.ImportDumpNotBase64Format, at));
                }

                if (!entry.TryGetValue("pttl", out var pttlValue))
                {
                    throw new InvalidDataException(string.Format(UiText.ImportMissingPttlFormat, at));
                }

                if (!long.TryParse(pttlValue?.ToString(), out long ttl))
                {
                    throw new InvalidDataException(string.Format(UiText.ImportPttlNotNumberFormat, at));
                }

                if (ttl < -1) throw new InvalidDataException(string.Format(UiText.ImportPttlOutOfRangeFormat, ttl, at));
                if (ttl == 0) return false;

                var restoreKey = entry.TryGetValue("keyBytes", out var encodedKey)
                    ? (StackExchange.Redis.RedisKey)Convert.FromBase64String(encodedKey.ToString()) : (StackExchange.Redis.RedisKey)key;
                // RESTORE overwrites unconditionally, so EXISTS + RESTORE as two round trips would
                // clobber a key written in between. The Lua script keeps the check and the restore
                // in one atomic step, exactly like the value branch below.
                // RESTORE takes (key, ttl, serialized-value); ARGV pairs payload, ttl in that order,
                // so the ttl must come first in the call or the payload is parsed as the TTL.
                const string restoreScript = @"
if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
redis.call('RESTORE', KEYS[1], ARGV[2], ARGV[1])
return 1";
                StackExchange.Redis.RedisValue restoreTtl = ttl < 0 ? (StackExchange.Redis.RedisValue)"0" : (StackExchange.Redis.RedisValue)(long)ttl;
                RedisResult restored = database.ScriptEvaluate(restoreScript,
                    new StackExchange.Redis.RedisKey[] { restoreKey },
                    new StackExchange.Redis.RedisValue[] { payload, restoreTtl });
                return (long)restored == 1;
            }

            if (!entry.TryGetValue("value", out var rawValue) || rawValue == null)
            {
                throw new InvalidDataException(string.Format(UiText.ImportNoDumpOrValueFormat, at));
            }

            if (database.KeyExists(key)) return false;
            var value = JToken.FromObject(rawValue);
            var commands = new List<string[]>();
            switch (type)
            {
                case "String":
                    if (value.Type != JTokenType.String) throw new InvalidDataException(string.Format(UiText.ImportStringExpectedTextFormat, value.Type, at));
                    commands.Add(new[] { "SET", key, value.ToString() });
                    break;
                case "Hash":
                    foreach (var item in (JArray)value)
                        commands.Add(new[] { "HSET", key, item["field"]?.Value<string>() ?? throw new InvalidDataException(string.Format(UiText.ImportHashMissingFieldFormat, at)), item["value"]?.Value<string>() ?? throw new InvalidDataException(string.Format(UiText.ImportHashMissingValueFormat, at)) });
                    break;
                case "List":
                case "Set":
                    foreach (var item in (JArray)value)
                    {
                        if (item.Type != JTokenType.String) throw new InvalidDataException(string.Format(UiText.ImportCollectionExpectedTextFormat, type, item.Type, at));
                        commands.Add(new[] { type == "List" ? "RPUSH" : "SADD", key, item.ToString() });
                    }
                    break;
                case "SortedSet":
                    foreach (var item in (JArray)value)
                    {
                        double score = item["score"].Value<double>();
                        if (double.IsNaN(score) || double.IsInfinity(score)) throw new InvalidDataException(string.Format(UiText.ImportScoreNotFiniteFormat, at));
                        commands.Add(new[] { "ZADD", key, score.ToString(System.Globalization.CultureInfo.InvariantCulture), item["member"]?.Value<string>() ?? throw new InvalidDataException(string.Format(UiText.ImportMissingMemberFormat, at)) });
                    }
                    break;
                case "Stream":
                    ulong previousMs = 0, previousSeq = 0;
                    foreach (var item in (JArray)value)
                    {
                        string id = item["id"]?.Value<string>() ?? throw new InvalidDataException(string.Format(UiText.ImportStreamMissingIdFormat, at));
                        var parts = id.Split('-');
                        if (parts.Length != 2 || !ulong.TryParse(parts[0], out ulong ms) || !ulong.TryParse(parts[1], out ulong seq) || ms < previousMs || (ms == previousMs && seq <= previousSeq))
                            throw new InvalidDataException(string.Format(UiText.ImportStreamIdNotIncreasingFormat, id, at));
                        previousMs = ms; previousSeq = seq;
                        var command = new List<string> { "XADD", key, id };
                        if (item["fields"] is not JArray fields)
                            throw new InvalidDataException(string.Format(UiText.ImportStreamMissingFieldsFormat, id, at));
                        foreach (var field in fields)
                        {
                            command.Add(field["name"]?.Value<string>() ?? throw new InvalidDataException(string.Format(UiText.ImportStreamFieldMissingNameFormat, at)));
                            command.Add(field["value"]?.Value<string>() ?? throw new InvalidDataException(string.Format(UiText.ImportStreamFieldMissingValueFormat, at)));
                        }
                        if (command.Count == 3) throw new InvalidDataException(string.Format(UiText.ImportStreamNoFieldsFormat, id, at));
                        commands.Add(command.ToArray());
                    }
                    break;
                default:
                    throw new InvalidDataException(type == null
                        ? string.Format(UiText.ImportMissingTypeFormat, at)
                        : string.Format(UiText.ImportUnsupportedTypeFormat, type, at));
            }
            if (commands.Count == 0) throw new InvalidDataException(string.Format(UiText.ImportEmptyValueFormat, type, at));
            long legacyTtl = -1;
            if (entry.TryGetValue("ttl", out var ttlObj) && ttlObj != null)
            {
                if (!long.TryParse(ttlObj.ToString(), out legacyTtl))
                    throw new InvalidDataException(string.Format(UiText.ImportTtlNotNumberFormat, at));
                if (legacyTtl < -1 || legacyTtl > long.MaxValue / 1000) throw new InvalidDataException(string.Format(UiText.ImportTtlOutOfRangeFormat, at));
                if (legacyTtl == 0) return false;
            }
            var imported = (long)database.ScriptEvaluate(
                "if redis.call('EXISTS',KEYS[1]) == 1 then return 0 end; local commands=cjson.decode(ARGV[1]); for _,cmd in ipairs(commands) do redis.call(unpack(cmd)) end; if tonumber(ARGV[2]) > 0 then redis.call('PEXPIRE',KEYS[1],ARGV[2]) end; return 1",
                new StackExchange.Redis.RedisKey[] { key },
                new RedisValue[] { JsonConvert.SerializeObject(commands), legacyTtl < 0 ? -1 : legacyTtl * 1000 });
            if (imported == 0) return false;

            return true;
        }

    }
}
