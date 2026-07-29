# MemoryInspector Plugin Guide

[繁體中文](#繁體中文) | [English](#english)

## 繁體中文

### 第一版 API

MemoryInspector Plugin API 目前版本為 `1.0.0`。每個 Plugin 放在
`Plugins/{PluginFolder}` 獨立目錄，至少包含：

```text
PluginFolder/
├─ plugin.json
├─ MyPlugin.dll
└─ MyPlugin.deps.json       # 有額外相依套件時建議提供
```

可執行的範例位於
[`samples/MemoryInspector.SamplePlugin`](../samples/MemoryInspector.SamplePlugin)。

### Manifest

```json
{
  "schemaVersion": 1,
  "id": "company.product.plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "apiVersion": "1.0.0",
  "minimumHostVersion": "1.0.0",
  "maximumHostVersion": "1.0.0",
  "entryAssembly": "MyPlugin.dll",
  "entryType": "Company.Product.MyPluginModule",
  "capabilities": ["analyzer"],
  "description": "Optional description",
  "author": "Author",
  "enabledByDefault": false
}
```

`capabilities` 支援 `analyzer`、`viewer`、`exporter`、`decoder` 與
`scannerExtension`。Host 會先驗證 manifest schema、API major/minor、
Host version range、entry path 及 capability，再決定是否載入 assembly。

### Entry point 與 DI

Entry type 必須是 public、non-abstract、具有無參數建構子的
`IMemoryInspectorPlugin`。`ConfigureServices` 收到的是該 Plugin 專用的
`IServiceCollection`，建立出的 `IServiceProvider` 不包含主程式核心服務。
Host 只預先註冊：

- `IPluginContext`
- `IPluginLogger`
- `TimeProvider`

`IPluginContext.Services` 僅代表 Plugin 自己的 provider。Plugin 可透過
`GetUiContributions` 發布平台中立的 command/result contribution；第一版不允許
直接注入任意 WPF control。

### 啟用、停用與隔離

- 每個 Plugin 使用 collectible `AssemblyLoadContext`。
- Managed assembly 從記憶體載入，停用後可立即替換套件檔案。
- Enable/Disable 狀態原子寫入 `Plugins/.plugin-state.json`。
- Disabled Plugin 不載入 assembly，也不建立 Plugin module/service。
- 單一 manifest、assembly、初始化或關閉失敗只影響該 Plugin。
- Plugin log 位於 `Logs/Plugins/{PluginId}/yyyy-MM-dd.log`。
- 初始化上限為 10 秒；關閉與 async dispose 每個步驟上限為 5 秒。

### 安全邊界

Plugin Host 不提供 Monitoring、Memory Reader/Writer、Snapshot Storage 或主程式
root `IServiceProvider`。`AssemblyLoadContext` 是相依與卸載隔離，不是安全沙箱；
只安裝可信任的 Plugin。Plugin 仍是同一個 OS process 內執行的 .NET 程式碼。

## English

### API v1

The current MemoryInspector Plugin API is `1.0.0`. Install each plugin under
its own `Plugins/{PluginFolder}` directory with `plugin.json`, its entry
assembly, and preferably its `.deps.json` when it has private dependencies.
See
[`samples/MemoryInspector.SamplePlugin`](../samples/MemoryInspector.SamplePlugin)
for a working Analyzer example.

The manifest format is shown above. Supported capabilities are `analyzer`,
`viewer`, `exporter`, `decoder`, and `scannerExtension`. The host validates the
manifest schema, API compatibility, host-version range, entry path, and
capabilities before loading code.

The public, non-abstract entry type must implement `IMemoryInspectorPlugin` and
have a parameterless constructor. `ConfigureServices` receives a per-plugin
service collection. Only `IPluginContext`, `IPluginLogger`, and `TimeProvider`
are supplied by the host; the application root provider and memory/session
services are not exposed.

Each enabled plugin uses a collectible load context and an isolated service
provider. Managed assemblies are loaded from memory so package files can be
replaced immediately after disable. Activation state is atomically persisted,
failures are isolated per plugin, and logs are written under
`Logs/Plugins/{PluginId}`. Initialization is limited to 10 seconds and shutdown
or async-disposal steps to 5 seconds.

The UI contract is a platform-neutral command/result contribution rather than
arbitrary WPF control injection. Assembly isolation is not a security sandbox:
plugins execute as .NET code in the host OS process, so install only trusted
plugins.
