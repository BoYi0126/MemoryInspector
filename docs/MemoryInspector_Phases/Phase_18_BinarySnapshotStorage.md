# Phase 18 - Binary Snapshot Storage

## 相依階段

- Phase 03
- Phase 10

## 目標

建立大量 Candidate 的 Disk-backed Binary Snapshot。

## 檔案結構

```text
Temp/{SessionId}/
├─ session.json
├─ tree.json
├─ node_0001.full.bin
└─ index.bin
```

## Full Snapshot

最小資料：

```text
Address: UInt64
```

需要時另存固定長度 value。

## 要求

- Stream read / write
- Async IO
- Atomic temp rename
- Checksum / version
- Incomplete file recovery
- Paging

## 驗收標準

- 百萬筆 Candidate 可寫入與讀回。
- 不需一次載入全部 RAM。
