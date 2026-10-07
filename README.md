# Redis Gui Manager

### Redis GUI client with searching by sql query 

![](https://github.com/selo0530/RedisGuiManager/blob/media/media/demo.png)

![](https://github.com/selo0530/RedisGuiManager/blob/media/media/query.png)

## Description

Redis Gui Manager is a gui redis client.  
This tool is very fast.  
This can find key, value, hash value by sql query.  
Enjoy.  

## Requirements

- Windows and .NET 10 (`net10.0-windows`). The published ZIP is self-contained and needs no
  separate .NET installation.
- Some operations use optional server capabilities: Lua for atomic edits and snapshot restore,
  Stream commands for stream keys, and Redis 7.0+ for `LCS`.

## Features

- **Connections**: standalone, Redis Cluster (multiple seed endpoints) and SSL/TLS, with optional
  SSH tunnelling. Settings carry a Redis ACL username (blank uses the default user) and a read-only
  flag. Connections are stored as `connections/*.json` next to the executable and can be organised
  into groups.
- **Key browsing and editing**: String, Hash, List, Set, Sorted Set and Stream, 500 entries per page,
  and TTL view / set / remove. Keys can be renamed, copied across databases or servers, and deleted
  or migrated in batches.
- **Snapshot import / export (JSON)**: exports every key of a database with an authoritative `dump`
  payload plus a best-effort readable `type`/`value`; imports restore through the dump, skip existing
  keys and keep the TTL.
- **SQL query window**: search keys and values of a database with SQL-style queries over a bounded
  snapshot.
- **Server insight**: server info dashboard, server tools (`CONFIG`, `CLIENT`, `MEMORY`,
  persistence, `CLUSTER`, `SENTINEL`), slowlog, and stream consumer groups.
- **Pub/Sub**: subscribe to channels or patterns (`*`, `?`, `[`), publish messages, and watch
  keyspace notifications for every database.
- **String tools**: interpret string-encoded structures with `BITCOUNT`, `BITPOS`, `GETRANGE`,
  `PFCOUNT`, `PFMERGE` and `LCS`.
- **Console**: run raw commands with history. In read-only mode only non-mutating commands are
  accepted.
- **Localization**: English and Simplified Chinese (`zh-Hans`) UI text.
- **Credential protection**: saved passwords are encrypted with Windows DPAPI for the current user;
  older plaintext configuration is read and re-encrypted on the next save.

## Runtime and operation controls

Connection settings include a Redis ACL username (blank uses the default user)
and an optional read-only mode. Read-only mode
blocks GUI writes and restricts console commands; server-side permissions remain
controlled by the Redis ACL user.

Key browsing loads 500 keys at a time. Selecting a tree node only moves the
highlight; a double-click (or Enter while the tree has focus) loads its children,
and a key opens its value editor. The progress window is revealed only after an
operation has been running for 250 ms, so quick scans finish without a window
flashing on screen. Hash, List, Set, Sorted Set and Stream
viewers show 500 entries per page; their search applies to the current page.
SCAN pages reflect live data, so refreshing is advisable when records change.
SQL queries expose adjustable limits for keys per database and rows per key;
they operate on that bounded snapshot and display at most 10,000 result rows.

Long operations show progress and support cancellation at safe boundaries;
underlying Redis requests may need to finish first. Batch deletion and migration show the exact scanned key count,
connection, databases and examples before execution. Result dialogs list failures
and can save the full report. Canceled imports, deletes and migrations retain
already completed changes; canceled exports save completed keys when any exist.

Editors warn before discarding unsaved changes. String, Hash and List saves
compare the loaded value atomically, Set edits check the original member still
exists, and Sorted Set edits check its original score. Conflicts retain edits
for review rather than overwrite newer data. Use Reload server to reconnect;
failed writes keep the input available.

Settings (`config.json`) and connection files are written atomically and keep a `.bak` backup, and a
corrupt settings file falls back to its backup instead of being treated as empty.

## Build and test

```powershell
dotnet build RedisGuiManager.sln
dotnet test  tests/RedisGuiManager.Tests.csproj
dotnet run   --project checks/RegressionChecks.csproj
```

`checks/RegressionChecks.csproj` is a WinForms smoke-check runner (STA). It also exercises live Redis
when the `REDIS_CHECK_ENDPOINT` environment variable points at a disposable test server, and skips
those checks otherwise. Continuous integration builds the solution, runs the unit tests and the
smoke checks on Windows; see `.github/workflows/ci.yml`.

Publish a self-contained Windows x64 ZIP:

```powershell
./scripts/publish.ps1 -Configuration Release -Runtime win-x64
```

## LICENSE

#### MIT Licence

MIT License

Copyright (c) 2025 JeongRae Kim

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.