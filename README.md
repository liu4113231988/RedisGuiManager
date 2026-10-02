# Redis Gui Manager

### Redis GUI client with searching by sql query 

![](https://github.com/selo0530/RedisGuiManager/blob/media/media/demo.png)

![](https://github.com/selo0530/RedisGuiManager/blob/media/media/query.png)

## Description

Redis Gui Manager is a gui redis client.  
This tool is very fast.  
This can find key, value, hash value by sql query.  
Enjoy.  

## Runtime and operation controls

Requires .NET 10 on Windows. Connection settings include a Redis ACL username
(blank uses the default user) and an optional read-only mode. Read-only mode
blocks GUI writes and restricts console commands; server-side permissions remain
controlled by the Redis ACL user.

Key browsing loads 500 keys at a time. Hash, List, Set, Sorted Set and Stream
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