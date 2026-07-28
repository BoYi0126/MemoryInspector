# MemoryInspector Phase Development Pack

## 專案定位

MemoryInspector 是一套以 **WPF + .NET 10 + MVVM** 建立的 Windows 記憶體分析平台。

此套文件已將整體專案拆成可逐步交付給 Agent 的多個 Phase，每個 Phase 都包含：

- 目標
- 相依階段
- 實作範圍
- 建議檔案結構
- 驗收標準
- 不在本階段處理的內容

## 建議執行方式

每次只交付一個 Phase 給 Agent。

建議 Prompt：

```text
請依照 Phase_XX_*.md 實作本階段。
開始前先閱讀：
- README.md
- Phase_00_ProjectOverview.md
- Phase_01_SolutionArchitecture.md
- 目前 Phase 文件

限制：
1. 不要提前實作後續 Phase。
2. 完成後執行 build 與 tests。
3. 更新 DevelopmentProgress.md 與 ModuleStatus.md。
4. 列出本次新增、修改檔案。
5. 說明尚未完成項目。
```

## 實際開發順序

Phase 編號用於識別需求，不代表可忽略相依階段直接依序實作。建議依下列批次交付；同一批次內的項目可在各自相依均完成後進行。

| 批次 | Phase | 交付主題 |
|---|---|---|
| 1 | 00 | 專案總覽、產品邊界與名詞 |
| 2 | 01 | Solution 與架構 |
| 3 | 02 | 共用模型與 Result Pattern |
| 4 | 03 | 設定、日誌與儲存路徑 |
| 5 | 04、26 | Process Explorer Core；Plugin Framework 基礎 |
| 6 | 05 | Process Explorer UI |
| 7 | 06 | Monitoring Session |
| 8 | 07 | Windows Memory Region Provider |
| 9 | 08、09、27 | Memory Region UI；Memory Reader；Module / Thread Viewer |
| 10 | 10 | Scanner 基礎與數值解析 |
| 11 | 11、18 | Exact Value First Scan；Binary Snapshot Storage |
| 12 | 12 | Unknown Initial Value |
| 13 | 13 | Next Scan 比對策略 |
| 14 | 14 | Duration Filter |
| 15 | 15 | Filter Pipeline |
| 16 | 16 | Scan History 與 Undo |
| 17 | 17 | Branching Scan Tree |
| 18 | 19 | Delta Snapshot 與 Reference Count |
| 19 | 20、29 | LRU Cache 與 Memory Budget；Snapshot Compare |
| 20 | 21、25 | Result Grid Virtualization；Temporary Manager |
| 21 | 22、28 | Watch Window；Hex Viewer |
| 22 | 23 | Saved Address JSON |
| 23 | 24 | Optional Memory Editor 模組骨架 |
| 24 | 30 | 整合測試與效能驗收 |
| 25 | 31 | Release、文件與封裝 |

## Phase 相依矩陣

| Phase | 內容 | 直接相依階段 |
|---|---|---|
| 00 | 專案總覽 | 無 |
| 01 | Solution 與架構 | 00 |
| 02 | 共用模型與 Result Pattern | 01 |
| 03 | 設定、日誌與儲存路徑 | 01、02 |
| 04 | Process Explorer Core | 02、03 |
| 05 | Process Explorer UI | 04 |
| 06 | Monitoring Session | 04、05 |
| 07 | Windows Memory Region Provider | 06 |
| 08 | Memory Region Viewer UI | 07 |
| 09 | Memory Reader Core | 06、07 |
| 10 | Scanner 基礎與數值解析 | 02、09 |
| 11 | First Scan - Exact Value | 07、09、10 |
| 12 | Unknown Initial Value | 11、18 |
| 13 | Next Scan 比對策略 | 11、12 |
| 14 | Duration Filter | 13 |
| 15 | Filter Pipeline | 13、14 |
| 16 | Scan History 與 Undo | 15 |
| 17 | Branching Scan Tree | 16、18 |
| 18 | Binary Snapshot Storage | 03、10 |
| 19 | Delta Snapshot 與 Reference Count | 17、18 |
| 20 | LRU Cache 與 Memory Budget | 18、19 |
| 21 | Result Grid Virtualization | 11、18、20 |
| 22 | Watch Window | 09、21 |
| 23 | Saved Address JSON | 03、22 |
| 24 | Memory Editor 模組骨架 | 06、09、22、23 |
| 25 | Temporary Manager | 18、19、20 |
| 26 | Plugin Framework | 01、03 |
| 27 | Module / Thread Viewer | 06、07 |
| 28 | Hex Viewer | 09、21 |
| 29 | Snapshot Compare | 18、19 |
| 30 | 整合測試與效能驗收 | 04–29 |
| 31 | Release、文件與封裝 | 30 |

當單一 Phase 文件與本矩陣不一致時，以單一 Phase 文件的「相依階段」為準，並在開始實作前同步修正本矩陣。

## 重要架構原則

- UI 不直接呼叫 Windows Native API。
- Core 不依賴 WPF。
- Windows Adapter 封裝平台實作。
- 所有長時間工作支援 `CancellationToken`。
- 大量候選結果不得全部建立成 ViewModel。
- Scan Tree 節點不得全部常駐 RAM。
- 預設以唯讀分析為核心。
- Memory Editor 為獨立、明確啟用的模組。
- 不實作權限繞過、保護規避、注入、Hook 或核心驅動。
