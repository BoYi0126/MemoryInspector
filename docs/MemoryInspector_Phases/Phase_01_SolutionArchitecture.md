# Phase 01 - Solution Architecture

## 相依階段

- Phase 00

## 目標

建立 Solution、專案分層、Project Reference 與基礎 DI。

## Solution

```text
MemoryInspector.slnx

src/
├─ MemoryInspector.Common
├─ MemoryInspector.Core
├─ MemoryInspector.Application
├─ MemoryInspector.Windows
├─ MemoryInspector.Plugin
└─ MemoryInspector.Wpf

tests/
├─ MemoryInspector.Core.Tests
├─ MemoryInspector.Windows.Tests
└─ MemoryInspector.IntegrationTests
```

## 分層原則

### Common
- 共用 Result
- Error
- Guard
- 基礎工具

### Core
- 領域模型
- Scanner 規則
- Session / Tree / Snapshot 抽象

### Application
- Use Cases
- Orchestration
- View-independent services

### Windows
- Process
- Native memory information
- SafeHandle
- 平台 Adapter

### Wpf
- Views
- ViewModels
- Commands
- Converters

### Plugin
- Plugin contracts
- Module discovery

## 驗收標準

- Solution 可成功 Build。
- 專案相依方向不循環。
- WPF 不直接包含 Native API 宣告。
- 建立 `DevelopmentProgress.md` 與 `ModuleStatus.md`。
