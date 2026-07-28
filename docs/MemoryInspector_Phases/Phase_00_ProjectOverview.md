# Phase 00 - Project Overview

## 相依階段

- 無（本階段是所有後續 Phase 的基線）

## 目標

建立 MemoryInspector 的完整產品邊界、名詞與開發順序。

## 產品定位與邊界

MemoryInspector 是以 **WPF + .NET 10 + MVVM** 建立、以 x64 Windows 為優先平台的記憶體分析工具。

第一版產品範圍：

- 列舉 Process，並以 PID、啟動時間及架構識別監控目標。
- 唯讀檢視 Memory Region、Module、Thread 與 Hex 資料。
- 執行 Exact Value、Unknown Initial Value 與多輪條件篩選。
- 將大量 Candidate 與 Snapshot 存放於受記憶體預算控制的磁碟儲存。
- 提供 Scan History、Undo、Branch、Watch 與 Saved Address。
- 以可選、預設停用的 Memory Editor 和版本化 Plugin API 擴充功能。

第一版明確不處理：

- 權限繞過、保護規避、程式注入、Hook 或核心驅動。
- 非 Windows 平台的原生記憶體讀寫實作。
- 將所有 Candidate、Snapshot 或 Scan Tree Node 全部常駐 RAM。
- 由 UI 直接呼叫 Windows Native API 或持有 Process Handle。
- 未經使用者明確啟用與確認的記憶體寫入。

## 產品模組

- Process Explorer
- Monitoring Session
- Memory Region Viewer
- Memory Scanner
- Duration Filter
- Filter Pipeline
- Branching Scan Tree
- Snapshot Storage
- Watch Window
- Saved Address
- Optional Memory Editor
- Temporary Manager
- Plugin Framework
- Module / Thread Viewer
- Hex Viewer
- Snapshot Compare

## 核心名詞

| 名詞 | 定義 |
|---|---|
| Process | 作業系統中的目標程序；不可只以 PID 識別。 |
| Monitoring Session | 綁定 Process Identity 的單一監控工作階段，管理連線、生命週期與儲存位置。 |
| Memory Region | 目標 Process 虛擬位址空間中的連續區域，包含狀態、類型與保護屬性。 |
| Candidate | 掃描或篩選後仍符合條件的位址及其必要值資訊。 |
| Scan Round | 一次 First Scan 或 Next Scan 的輸入、策略、數量與輸出中繼資料。 |
| Pending Result | 尚未 Keep 的篩選結果；可 Keep 成為下一輪輸入，或 Discard 回到 Parent。 |
| Scan Node | Scan Tree 中代表一輪已保存結果與 Storage Reference 的節點。 |
| Snapshot | Candidate 集合的持久化表示，可為 Full Snapshot 或 Delta Snapshot。 |
| Branch | 從既有 Scan Node 建立、可獨立繼續篩選的結果路徑。 |
| Watch Entry | 工作階段中持續批次讀取的 Address；目標失效時可變為 unreadable。 |
| Saved Address | 與暫存 Snapshot 分離、可跨工作階段保存的具名 Address 與 Value Type。 |
| Temporary Data | 可清理的 Session、Snapshot、Index 與中間檔，不包含 Saved Address。 |
| Memory Budget | Candidate Cache 可使用的 RAM 上限；超限時依 LRU Policy flush 與 eviction。 |
| Optional Memory Editor | 與唯讀核心隔離、預設停用且每次寫入需明確確認的可選模組。 |

## 核心使用流程

```text
選擇 Process
→ 建立 Monitoring Session
→ 查看 Memory Regions
→ First Scan
→ 多輪 Filter
→ Keep / Undo / Branch
→ Watch / Save Address
```

## 開發順序

後續實作必須遵循 README 的「實際開發順序與相依矩陣」。核心交付路徑如下：

```text
00 → 01 → 02 → 03 → 04 → 05 → 06 → 07 → 09 → 10 → 11
                                              ↓
                                             18 → 12 → 13 → 14 → 15 → 16 → 17
                                                   17 + 18 → 19 → 20 → 21 → 22 → 23
```

Phase 08、24–29 是依其直接相依階段插入的功能軌；Phase 30 必須等待 Phase 04–29 全部完成，Phase 31 最後執行。不得僅因 Phase 編號較小，就在其相依階段完成前開始實作。

## 非功能需求

- WPF UI 不凍結
- 支援大量 Candidate
- 支援取消
- 支援工作階段恢復
- 清楚錯誤訊息
- 可測試、可擴充
- x64 優先

## 驗收標準

- 所有 Phase 有清楚相依關係。
- README 說明開發順序。
- 專案命名統一使用 `MemoryInspector`。

## 命名規範

- 產品、Solution 與根命名空間統一使用 `MemoryInspector`。
- 專案名稱使用 `MemoryInspector.<Layer>`，測試專案使用 `MemoryInspector.<Layer>.Tests`。
- 文件中的模組與核心名詞使用本文件定義的英文名稱，避免同一概念出現不同縮寫。
- 檔案系統資料根目錄固定為 `%LocalAppData%\MemoryInspector\`。

## 本階段不處理

- 不建立 Phase 01 定義的分層專案、Project Reference 或 DI。
- 不實作任何 UI、Process 存取、掃描、儲存或記憶體寫入功能。
