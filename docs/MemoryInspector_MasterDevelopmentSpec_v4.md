# MemoryInspector Master Specification Reference v4

此檔案保留整體方向摘要。實際開發請依 Phase 文件執行。

## 核心架構

```text
RAM Cache
+ Binary Snapshot
+ Delta Snapshot
+ LRU Cache
+ Memory Budget
```

## 核心流程

```text
Process Explorer
→ Monitoring Session
→ Memory Region Viewer
→ First Scan / Unknown Initial Value
→ Duration Filter
→ Filter Pipeline
→ Branching Scan Tree
→ Watch / Saved Address
→ Optional Memory Editor
```

## Memory Editor 分層

```text
Phase 24: Foundation
- Models
- Serializer
- Feature Flag
- Audit
- Mock

Phase 25: Windows Writer
- Session validation
- Region validation
- Single write
- Read-back verification
- Authorized Test Target

Phase 26: WPF UI
- Editor panel
- Confirmation
- Write history
- Limited undo
```

## 重要原則

- Scan Node 不全部常駐 RAM。
- 大量 Candidate 使用 Disk-backed Storage。
- UI 使用 Virtualization 與 Pagination。
- Saved Address 與 Temp 分離。
- Memory Editor 預設停用。
- 寫入只針對自行開發或已授權目標。
- 不實作權限繞過、Protection override、注入、Hook、核心驅動或防護規避。
