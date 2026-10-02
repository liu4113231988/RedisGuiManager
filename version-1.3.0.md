# Redis Gui Manager 版本更新说明

---

## v1.3.0 — 2026-08-29

### 核心重构

#### 一、移除 Workspace 选择页，集成至首页左侧

- **移除启动弹窗**：删除 `FormMain_Load` 中 `FormLoadServerList` 弹窗流程（`Forms/FormMain.cs:44`），打开应用不再强制选择 workspace。
- **集成至首页左侧**：原 workspace 选择功能集成到 `FormMain` 左侧面板 `splitContainer1.Panel1`，与连接树同处一屏，操作路径缩短一级。
- **自动加载**：启动时自动扫描 `Application.StartupPath/connections/*.json`，聚合全部连接与分组，标题固定为 `Redis Gui Manager`。

---

#### 二、连接树：全部连接为根节点

- **聚合加载 `LoadAllConnections`（）**：
  - 若 `connections` 目录不存在则自动创建；若无任何 `*.json` 则创建 `connections/connections.json` 初始内容 `[]`。
  - 遍历全部 `*.json`，按 `type` 字段区分 `RedisGroup` / `RedisSettings`，分别为分组与独立连接，统一创建 `RedisClient` 并记录源文件映射 `_settingFileMap/_groupFileMap/_knownFiles`。
  - 分组内 `connections` 为 `null` 时置空列表，避免 `NullReferenceException`；嵌套连接继承分组所属文件。
- **按源文件分组持久化 `SaveRedisSettings`**：
  - 按 `_settingFileMap/_groupFileMap` 将内存中的分组与连接分组回写到各自源文件，新增连接默认落盘 `connections/connections.json`。
  - 已知文件即使当前无条目仍回写 `[]`，避免旧数据残留造成“幽灵连接”。
  - 采用临时文件原子写入（`*.tmp → Copy → Delete`），防崩溃半写导致 JSON 损坏。

---

### 功能增强

**左侧刷新按钮**
**暗黑模式开关回补**
**新增名称重复校验**

---

### 改进与修复

**资源泄漏修复**
**集合修改异常**
**重复代码与原子性**
**加载容错**
**空指针加固**
**路径归一化**
---

### 兼容性

- 旧 `connections/*.json` 多 workspace 文件无需迁移，首次启动即自动聚合；后续保存按源文件回写，保持向后兼容。
- `recent.json` 不再使用，保留文件不影响运行；新逻辑不再读写该文件。
- `config.json` 中 `darkmode` 配置读写路径不变。

### 依赖更新（安全漏洞修复）

本次更新集中修复第三方 NuGet 包的已知安全漏洞
