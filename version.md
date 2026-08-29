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

- **Stream 浏览**：在树状视图中点击 Stream 类型的 Key，自动加载并展示其全部 Entry（ID + Field-Value 对）。
- **Stream 添加**：支持通过 `StreamValueInsertForm` 弹窗添加新的 Stream Entry，可指定自定义 ID 或使用自动生成。
- **Stream 删除**：支持删除单个 Entry。
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
  - Redis 版本号、运行模式（standalone / cluster / sentinel）
  - 已连接客户端数、已用/峰值内存
  - 内存碎片率、Key 命中率（命中率/未命中率）
  - 总连接数、每秒操作数
  - 持久化（RDB / AOF）状态
- 支持「刷新」按钮实时获取最新信息。

---

#### 五、Slowlog 慢查询分析

- 右键 Redis 服务器节点 →「Slowlog」，弹出慢查询分析窗体。
- 展示内容包含：查询命令、执行耗时（微秒）、客户端信息、执行时间戳。
- 支持自定义获取条数（默认 10 条）。

---

#### 六、数据导入 / 导出（JSON 格式）

- **导出**：右键 DB 节点 →「Export Data (JSON)」，将当前 DB 的所有 Key（含类型、值、TTL）导出为 JSON 文件。
  - 支持全部 Redis 数据类型：String、Hash、List、Set、SortedSet、Stream。
  - 保留 TTL 信息，导入时可恢复。
- **导入**：右键 DB 节点 →「Import Data (JSON)」，从 JSON 文件批量导入 Key。
  - 导入前弹窗确认条数，支持中断。
  - 支持全部类型反序列化，自动恢复 TTL。

---

#### 七、Pub/Sub 订阅监控

- 右键 Redis 服务器节点 →「Pub/Sub」，弹出订阅监控窗体。
- **Subscribe 标签页**：
  - 输入频道名订阅，支持多频道同时订阅。
  - 实时展示收到的消息（时间、频道、消息内容），自动滚动到最新行。
  - 左侧频道列表，可选中后取消订阅。
  - 支持清空消息列表。
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

### 改进与修复

| 项目 | 说明 |
|------|------|
| **序列化类替换** | 将 `BinaryFormatter`（已标记弃用，会产生 SYSLIB0011 警告）替换为 **Newtonsoft.Json**，消除编译警告 |
| Key 复制功能增强 | 跨 DB / 跨机器复制 Key 时，新增 Stream 类型支持 |
| 右键菜单结构优化 | 新增分隔符（`ttlSeparator`、`serverToolsSeparator`）对功能项进行分组 |
| 配置序列化兼容 | 所有新增配置属性均标注 `[JsonProperty(NullValueHandling = Ignore)]`，向后兼容旧配置文件 |
