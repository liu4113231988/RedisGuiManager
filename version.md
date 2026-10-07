# Redis Gui Manager 版本更新说明

---

## v1.2.0 — 2026-08-07

### 新增功能

#### 一、TTL 管理（查看 / 设置 / 移除过期时间）

- **查看 TTL**：右键点击任意 Key，选择「View TTL」即可查看剩余过期时间，支持格式化显示（天/时/分/秒 + 总秒数）。
- **设置 TTL**：右键选择「Set TTL」，输入秒数即可设置过期时间。支持 `-1`（永久）和 `0`（立即删除）快捷操作。
- **移除 TTL**：右键选择「Remove TTL」，确认后 Key 变为永久有效。

---

#### 二、Stream 数据类型支持

- **Stream 浏览**：在树状视图中点击 Stream 类型的 Key，自动加载当前页（每页 500 条）的 Entry（ID + Field-Value 对），底部可翻页。
- **Stream 添加**：支持通过 `StreamValueInsertForm` 弹窗添加新的 Stream Entry，可指定自定义 ID 或使用自动生成。
- **Stream 删除**：支持删除单个 Entry。
- **消费者组**：Stream 编辑器新增「Groups/pending」按钮，可查看 XINFO STREAM / GROUPS / CONSUMERS 与 XPENDING，并执行 XACK、XCLAIM、XTRIM。
- **查询窗口集成**：SQL 查询窗口已新增 Stream 类型分支，支持结构化查询。

---

#### 三、SSL/TLS 加密连接支持

- 在「Add / Edit server」对话框中新增 **SSL/TLS** 复选框。
- 启用后，连接将通过 TLS 加密传输，适用于云上 Redis（如 Azure Cache for Redis、AWS ElastiCache）。
- 支持 SSL 与 SSH 隧道组合使用。

---

#### 四、服务器信息仪表盘

- 右键 Redis 服务器节点 →「Server Info」，弹出仪表盘窗体。
- 展示内容包含：
  - Redis 版本号、运行模式（standalone / cluster）
  - 已连接客户端数、已用/峰值内存
  - 内存碎片率、Key 命中率（命中率/未命中率）
  - 总连接数、每秒操作数
  - 持久化（RDB / AOF）状态
- 支持「刷新」按钮实时获取最新信息。
- **Server tools**：右键 Redis 服务器节点 →「Server tools」，提供六个选项卡：
  - **Configuration**：`CONFIG GET` 浏览，`CONFIG SET` 修改（修改前确认，并提示不可撤销）
  - **Clients**：`CLIENT LIST` 列表与 `CLIENT KILL`
  - **Memory**：`MEMORY USAGE` / `MEMORY STATS` / `MEMORY DOCTOR`
  - **Persistence**：RDB / AOF 状态与 `LASTSAVE`，以及 `SAVE` / `BGSAVE` / `BGREWRITEAOF`
  - **Cluster**：`CLUSTER NODES` 拓扑与节点汇总
  - **Diagnostics**：按 Key 查看 `OBJECT` 属性与 `MEMORY USAGE`，以及 `SENTINEL` 集群信息

---

#### 五、Slowlog 慢查询分析

- 右键 Redis 服务器节点 →「Slowlog」，弹出慢查询分析窗体。
- 展示内容包含：查询命令、执行耗时（微秒）、客户端信息、执行时间戳。
- 支持自定义获取条数（默认 10 条）。

---

#### 六、数据导入 / 导出（JSON 格式）

- **导出**：右键 DB 节点 →「Export Data (JSON)」，将当前 DB 的所有 Key 导出为 JSON 文件。
  - 支持全部 Redis 数据类型：String、Hash、List、Set、SortedSet、Stream。
  - 每条记录包含 `key`、`keyBytes`、`type`、`dump`、`pttl`，以及尽力而为的 `value`（便于阅读与检索）。
  - **还原以 `dump` 为准**：`dump` 是 Redis 序列化后的精确快照，能完整保留 TTL 与内部编码；二进制值也能原样还原。
  - `value` 仅供阅读：每个 Key 最多记录 500 个元素，超出时置 `valueTruncated: true`；二进制值无法用 JSON 表示时直接省略该字段。
  - 因此「导出文件可读」与「导入能精确还原」互不冲突，导入不会依赖被截断的 `value`。
- **导入**：右键 DB 节点 →「Import Data (JSON)」，从 JSON 文件批量导入 Key。
  - 导入前弹窗确认条数，支持中断。
  - 支持全部类型反序列化，自动恢复 TTL。
  - 三种格式均兼容：`dump` 二进制格式（当前导出）、带 `type`+`value` 的可读格式、以及更早的纯文本格式。
  - 已存在的 Key 会被跳过；解析失败的条目会中止该文件的后续导入并给出报告，已完成的部分保留。

---

#### 七、Pub/Sub 订阅监控

- 右键 Redis 服务器节点 →「Pub/Sub」，弹出订阅监控窗体。
- **Subscribe 标签页**：
  - 输入频道名订阅，支持多频道同时订阅。
  - 频道名含 `*`、`?` 或 `[` 时按**模式订阅**（PSUBSCRIBE），否则按字面频道订阅；列表中会标注 `(pattern)`。
  - 实时展示收到的消息（时间、频道、消息内容），仅在用户已浏览到列表底部时自动滚动，避免打断阅读。
  - 消息最多保留 5000 行，超出丢弃最旧的并在状态栏提示已丢弃数量。
  - 左侧频道列表，可选中后取消订阅。
  - 支持清空消息列表。
  - **键空间通知**：点击「Watch keyspace events」可订阅 `__keyspace@*__:*`，实时观察其他客户端造成的过期、淘汰、删除、重命名等事件（需要服务端开启 `notify-keyspace-events`）。
- **Publish 标签页**：
  - 输入频道名和消息内容，点击 Publish 发布。
  - 状态栏显示接收者数量。
- 窗体关闭时自动取消所有订阅，防止资源泄漏。

---

#### 八、Redis Cluster 连接支持

- 在「Add / Edit server」对话框中新增 **Cluster** 复选框。
- 启用后：
  - 显示「Nodes」多行文本框，可输入多个集群节点端点（每行一个，格式 `host:port`）。
  - 连接时自动解析所有端点，StackExchange.Redis 自动发现集群全部节点。
  - Cluster 模式下仅显示 db 0（集群模式限制），避免无效 DB 操作。
  - 自动禁用 SSH 隧道选项（与集群多节点隧道冲突）。
- 兼容 SSL/TLS 加密连接。

---

#### 九、String 工具（位图 / HyperLogLog / LCS）

Redis 中位图、HyperLogLog、地理集合都以字符串编码存储，`TYPE` 返回 `string`，因此它们在 String 编辑器中打开。新增「String tools」面板补齐解释这些结构的命令：

- **BITCOUNT**：统计区间内置位数量，可切换按字节或按位索引。
- **BITPOS**：查找区间内第一个 0 / 1 的位置。
- **GETRANGE**：截取区间内容并显示（超长自动截断显示）。
- **PFCOUNT**：估算 HyperLogLog 的不同元素数量。
- **PFMERGE**：把另一个 Key 合并进当前 Key（写操作，需确认，会同时改动两个 Key）。
- **LCS**：求两个 Key 的最长公共子序列长度（需要 Redis 7.0+）。

结果按时间倒序显示在面板内；命令失败（如 Key 类型不符）会在结果行显示原因，不弹窗打断。

---

#### 十、连接分组管理

- 右键 →「Manage groups」，可新建、重命名、删除分组，并勾选调整分组成员。
- 一个连接只能属于一个分组（与持久化结构一致）；在分组中取消勾选即把该连接移回顶层。
- 删除分组时其连接会保留并降级回顶层，确认框会提示受影响的连接数量。
- 编辑以快照方式进行，取消即真正取消。

---

### 改进与修复

| 项目 | 说明 |
|------|------|
| **序列化类替换** | 将 `BinaryFormatter`（已标记弃用，会产生 SYSLIB0011 警告）替换为 **Newtonsoft.Json**，消除编译警告 |
| Key 复制功能增强 | 跨 DB / 跨机器复制 Key 时，新增 Stream 类型支持 |
| 右键菜单结构优化 | 新增分隔符（`ttlSeparator`、`serverToolsSeparator`）对功能项进行分组 |
| 配置序列化兼容 | 所有新增配置属性均标注 `[JsonProperty(NullValueHandling = Ignore)]`，向后兼容旧配置文件 |
