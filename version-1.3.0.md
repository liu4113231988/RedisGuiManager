# Redis Gui Manager 版本更新说明

---

## v1.3.0 — 2026-08-29

### 核心重构

#### 一、移除 Workspace 选择页，集成至首页左侧

- **移除启动弹窗**：删除 `FormMain_Load` 中 `FormLoadServerList` 弹窗流程（`Forms/FormMain.cs:44`），打开应用不再强制选择 workspace。
- **集成至首页左侧**：原 workspace 选择功能集成到 `FormMain` 左侧面板 `splitContainer1.Panel1`，与连接树同处一屏，操作路径缩短一级。
- **自动加载**：启动时自动扫描 `Application.StartupPath/connections/*.json`，聚合全部连接与分组，直接作为 `treeView_server` 根节点加载（`VirtualMachine` / `ListView_687`），标题固定为 `Redis Gui Manager`。

---

#### 二、连接树：全部连接为根节点

- **聚合加载 `LoadAllConnections`（`Forms/FormMain.cs:104`）**：
  - 若 `connections` 目录不存在则自动创建；若无任何 `*.json` 则创建 `connections/connections.json` 初始内容 `[]`。
  - 遍历全部 `*.json`，按 `type` 字段区分 `RedisGroup` / `RedisSettings`，分别为分组与独立连接，统一创建 `RedisClient` 并记录源文件映射 `_settingFileMap/_groupFileMap/_knownFiles`。
  - 分组内 `connections` 为 `null` 时置空列表，避免 `NullReferenceException`；嵌套连接继承分组所属文件。
- **按源文件分组持久化 `SaveRedisSettings`（`Forms/FormMain.cs:2061`）**：
  - 按 `_settingFileMap/_groupFileMap` 将内存中的分组与连接分组回写到各自源文件，新增连接默认落盘 `connections/connections.json`。
  - 已知文件即使当前无条目仍回写 `[]`，避免旧数据残留造成“幽灵连接”。
  - 采用临时文件原子写入（`*.tmp → Copy → Delete`），防崩溃半写导致 JSON 损坏。

---

### 功能增强

| 功能 | 说明 |
|------|------|
| **左侧刷新按钮** | 原 `button_open_server`（`Forms/FormMain.Designer.cs:136`）图标由 `folder_open` 更换为 `Activity_16xLG`，点击触发 `LoadAllConnections` 重新扫描全量文件并重建树，等价于“刷新全部连接” |
| **暗黑模式开关回补** | 原 `FormLoadServerList` 中的 `Dark mode` 复选框已迁移至首页左下角 `checkBox_darkmode`（`Forms/FormMain.Designer.cs:154`, `324x590` 树 + `624` 复选框），`CheckedChanged` 即时写入 `Config.darkmode` 并 `Config.Save()`，保留“需重启生效”提示 |
| **新增名称重复校验** | `button_add_server_Click`（`Forms/FormMain.cs:2158`）新增大小写不敏感名称校验，遍历 `redis_settings` 与 `redis_group[].connections`，重复时 `MessageBox` 提示并拒绝落盘 |

---

### 改进与修复

| 项目 | 说明 | 位置 |
|------|------|------|
| **资源泄漏修复** | `ClearAll` 原仅关闭 `treeView_server.Nodes` 顶层 `RedisClient`，分组内连接未关闭；现遍历 `redis_group`/`redis_settings` 全量 `Close()`，并兜底遍历树子节点 | `Forms/FormMain.cs:51` |
| **集合修改异常** | `RemoveServer` 原 `foreach(group.connections) Remove` 会抛 `InvalidOperationException`；改为倒序 `for` 安全删除 | `Forms/FormMain.cs:2040` |
| **重复代码与原子性** | 去除 `SaveRedisSettings` 重复 `if(!ContainsKey(defaultFull))`，写入改为原子操作 | `Forms/FormMain.cs:1980/2018` |
| **加载容错** | 空白文件视为空数组；`JArray.Parse` 单独捕获 `JsonReaderException`；`name+host` 全空条目跳过并记 `loadErrors`；错误信息汇总至 `toolStripStatusLabel1` 避免被 `Loaded X connections` 覆盖 | `Forms/FormMain.cs:131/222` |
| **空指针加固** | `remove_db_range` / `remove_keys_from_registered_dbs` / `remove_db` 增加 `additional_dbs == null` 守卫；`add_dbs_to_list` 增加 `changed` 标记与空列表处理；`treeView_server_AfterSelect` 增加 `select==null||Tag==null` 早退；`GetRedisNode/GetDbNode/GetFullpathFolder` 增加 `null/Parent==null` 终止与 `path.Length` 守卫 | `Forms/FormMain.cs:470/589/1210/447/1330/1441` |
| **路径归一化** | 新增 `GetConnectionsDir`/`GetDefaultConnectionsFile` 统一 `Path.Combine(Application.StartupPath, ...)` + `Path.GetFullPath` + `OrdinalIgnoreCase`，避免工作目录与启动目录不一致 | `Forms/FormMain.cs:94` |

---

### 兼容性

- 旧 `connections/*.json` 多 workspace 文件无需迁移，首次启动即自动聚合；后续保存按源文件回写，保持向后兼容。
- `recent.json` 不再使用，保留文件不影响运行；新逻辑不再读写该文件。
- `config.json` 中 `darkmode` 配置读写路径不变。

### 依赖更新（安全漏洞修复）

本次更新集中修复第三方 NuGet 包的已知安全漏洞，已通过 `dotnet list package --vulnerable` 验证（0 漏洞）：

| 依赖 | 旧版本 | 新版本 | 说明 |
|------|--------|--------|------|
| `Newtonsoft.Json` | 13.0.3 | **13.0.4** | 修复 `GHSA-5crp-9r3c-p9ky` 相关的反序列化拒绝服务风险 |
| `StackExchange.Redis` | 2.8.24 | **3.1.31** | 大版本升级，修复协议解析与重连逻辑中的多项 CVE，兼容 .NET 8/10，移除已弃用 API |
| `SSH.NET` | 2024.0.0 | **2026.0.0** | 修复 SSH 密钥交换与主机密钥验证相关漏洞，升级至 2026 年度主线 |
| `System.Data.SQLite.Core` | 1.0.118 | **1.0.119** | 同步 SQLite 引擎补丁，修复潜在堆溢出 |
| `WPFHexaEditor` | 3.4.5 | 3.4.5 | 保持不变，无漏洞 |

> 涉及文件：`RedisGuiManager/RedisGuiManager.csproj:29` `PackageReference` 版本号，`AssemblyVersion`/`FileVersion` 由 `1.2.0.0` 升级至 `1.3.0.0`。

---

### 已知限制

- 集群模式下仍仅展示 `db0`（与 `v1.2.0` 一致）。
