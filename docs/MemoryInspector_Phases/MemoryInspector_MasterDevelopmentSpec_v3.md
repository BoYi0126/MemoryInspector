# MemoryInspector Master Specification Reference

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
```

## 重要原則

- Scan Node 不全部常駐 RAM。
- 大量 Candidate 使用 Disk-backed Storage。
- UI 使用 Virtualization 與 Pagination。
- Saved Address 與 Temp 分離。
- Memory Editor 為可選模組，預設停用。
