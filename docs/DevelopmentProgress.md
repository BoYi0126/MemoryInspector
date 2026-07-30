# MemoryInspector Development Progress

| Phase | 狀態 | 完成日期 | 摘要 |
|---|---|---|---|
| 00 - Project Overview | 完成 | 2026-07-28 | 已定義產品邊界、名詞、開發順序與相依矩陣。 |
| 01 - Solution Architecture | 完成 | 2026-07-28 | 已建立分層專案、單向 Project Reference、WPF composition root 與測試專案骨架。 |
| 02 - Common Models and Result Pattern | 完成 | 2026-07-28 | 已建立 Result、Error、Guard、分頁、進度與格式化基礎工具及其單元測試。 |
| 03 - Configuration, Logging and Paths | 完成 | 2026-07-28 | 已建立版本化設定、預設值、損壞復原、應用程式目錄及每日輪替檔案日誌。 |
| 04 - Process Explorer Core | 完成 | 2026-07-28 | 已建立程序摘要模型、可取消列舉、欄位級錯誤隔離、架構偵測及跨 refresh CPU 計算。 |
| 05 - Process Explorer UI | 完成 | 2026-07-29 | 已建立虛擬化 Process DataGrid、非同步／自動更新、搜尋、PID filter、排序、選取詳情與 Monitoring 命令邊界。 |
| 06 - Monitoring Session | 完成 | 2026-07-29 | 已建立完整程序身分、Session 狀態機、單一 Active Session、Windows 目標連線、存活監測、失效處理、資源釋放與 WPF Start／Stop 操作。 |
| 07 - Windows Memory Region Provider | 完成 | 2026-07-29 | 已建立 x64 Memory Region 模型、Connected Session 查詢服務、SafeHandle／VirtualQueryEx Adapter、狀態／類型／保護屬性映射及 partial failure 回報。 |
| 08 - Memory Region Viewer UI | 完成 | 2026-07-29 | 已建立 Process／Memory Regions 雙分頁 Shell、虛擬化 Region DataGrid、位址與屬性篩選、Size 排序、詳情、partial warning、非同步 Refresh 與 Session 失效同步。 |
| 09 - Memory Reader Core | 完成 | 2026-07-29 | 已建立 block／typed／batch／partial read 模型與 Connected Session service，以及 SafeHandle、ReadProcessMemory、可調 chunk size 與單一 Handle batch Windows Adapter。 |
| 10 - Scanner Foundation and Value Parsing | 完成 | 2026-07-29 | 已建立掃描型別、比對模式、對齊策略、request／result／candidate 模型、Invariant 數值解析、little-endian 編碼，以及含 tolerance 與 NaN／Infinity 規則的值比對器。 |
| 11 - First Scan - Exact Value | 完成 | 2026-07-29 | 已建立非阻塞 Exact Value First Scan、可掃描 Region policy、chunk overlap、candidate 去重、partial read／結果上限處理、chunk 級進度與取消。 |
| 12 - Unknown Initial Value | 完成 | 2026-07-29 | 已建立候選／磁碟容量估算、RAM／Snapshot threshold 判斷，以及將 address、initial value、value type 與 metadata 直接串流至 disk-backed baseline snapshot 的流程。 |
| 13 - Next Scan Comparison Strategies | 完成 | 2026-07-29 | 已建立 snapshot-to-snapshot Next Scan、previous candidate paging、batch memory read、current value snapshot，以及 Exact／Changed／Unchanged／Increased／Decreased／Greater／Less 策略。 |
| 14 - Duration Filter | 完成 | 2026-07-29 | 已建立 Endpoint Compare／Continuous Observe、Changed／Unchanged／Increased／Decreased 期間旗標、非同步有效倒數、暫停／繼續、取消、進度、batch read 與 current value snapshot。 |
| 15 - Filter Pipeline | 完成 | 2026-07-29 | 已建立單一 Active／Pending Result 流程、Keep／Discard／Continue gate、Current Candidate Count、Next／Duration orchestration、自動 snapshot node 配置及 Before／After filter summary。 |
| 16 - Scan History and Undo | 完成 | 2026-07-29 | 已建立 Round／Parent ID、名稱、Mode、Duration、Input、Before／After、Created Time、Storage Reference metadata，並提供原子 JSON 持久化、重啟復原、Undo、Redo、Rename 與 Delete pending。 |
| 17 - Branching Scan Tree | 完成 | 2026-07-29 | 已將線性 History 升級為持久化 Scan Tree，提供 Children 導覽、Branch From Here、唯一 Active Node、Rename、Pin、遞迴 Delete Branch、metadata Compare 與 path-to-root。 |
| 18 - Binary Snapshot Storage | 完成 | 2026-07-29 | 已建立版本化 fixed-record binary format、address／optional value 串流、SHA-256、atomic snapshot／index rename、incomplete recovery 與 direct-seek paging。 |
| 19 - Delta Snapshot and Reference Counting | 完成 | 2026-07-29 | 已建立 DeltaKeep／DeltaRemove binary format、latest-value overlay、parent dependency、reference count、引用安全刪除，以及每 5 層或累積 Delta 超過 parent Full 50% 時自動保留 Full Snapshot。 |
| 20 - LRU Cache and Memory Budget | 完成 | 2026-07-29 | 已建立透明 Snapshot cache decorator、LRU node eviction、RAM byte／node count 雙重預算、小節點預熱、中型節點 lazy cache、百萬筆門檻純磁碟分頁、即時預算調整與 RAM／Disk usage 查詢。 |
| 21 - Result Grid Virtualization | 完成 | 2026-07-29 | 已建立 Snapshot page service、每頁最多 1,000 筆的 Result ViewModel、lazy loading、切頁取消、當頁排序、位址複製、Watch／Save action contract、read status 與 recycling DataGrid。 |
| 22 - Watch Window | 完成 | 2026-07-29 | 已建立 session-bound Watch service、批次更新、Previous／Current Value、Delta、更新時間、Unreadable／TargetUnavailable 狀態、Add／Remove、型別切換、Pause／Resume、手動更新與可設定更新間隔，並串接 Result Grid。 |
| 23 - Saved Address JSON | 完成 | 2026-07-29 | 已建立 schema v1 Saved Address catalog、target metadata、原子 JSON store、Add／Rename／Update／Delete、Import／Export、重複 Key 確認、Result Grid 串接，以及 reconnect 後的 Address 批次可讀性驗證。 |
| 24 - Memory Editor Foundation | 完成 | 2026-07-29 | 已建立 write request／result／verification／audit／confirmation 模型、九種型別 serializer、Feature Flag 與授權聲明、Session／manual／verification gate、Mock／Denied／No-op writer、write orchestration 及獨立 Audit JSON；本階段不含 Native write API。 |
| 25 - Windows Memory Writer | 完成 | 2026-07-29 | 已建立 WindowsMemoryWriter、單次寫入 SafeHandle、Region／range／writable 驗證、完整 Session 與程序身分重驗、expected-original compare、回讀驗證、部分寫入錯誤映射及受控 Test Target；不變更頁面保護或提升權限。 |
| 26 - Memory Editor UI | 完成 | 2026-07-29 | 已建立 Memory Editor WPF 分頁、Results／Watch／Saved／Manual 入口、Decimal／Hex preview、完整確認、分類結果、局部刷新、最近成功寫入 Undo 衝突檢查，以及可篩選／複製／Retry／CSV export 的 Write History。 |
| 27 - Temporary Manager | 完成 | 2026-07-29 | 已建立 Temp inventory／統計、Current Node／Branch／Session／All Temp 安全刪除、Pinned 保留、保留期限自動清理、啟動 `.tmp` 復原／清除、Session compact／Tree 驗證、Open Folder 與 WPF 管理分頁。 |
| 28 - Plugin Framework | 完成 | 2026-07-29 | 已建立 Plugin API 1.0、manifest／版本相容驗證、五種 capability、Enable／Disable persistence、collectible loader、獨立 DI／log、失敗與逾時隔離、UI contribution、WPF Manager、範例 Analyzer 與 Plugin Guide。 |
| 29 - Module and Thread Viewer | 完成 | 2026-07-29 | 已建立 Session-bound Module／Thread models、Application service、Windows 列舉與 identity revalidation、欄位級／集合級 partial failure、WPF 虛擬化分頁、搜尋、排序與 Session 失效清除。 |
| 30 - Hex Viewer | 完成 | 2026-07-29 | 已建立唯讀 4 KiB fixed-window Hex Viewer、16-byte rows、Address／Offset／Hex／ASCII、Region／Result 入口、跳址、byte search、bounded paging、refresh、partial/unreadable 標示與 Session 失效清除。 |
| 31 - Snapshot Compare | 完成 | 2026-07-29 | 已建立雙 cursor streaming merge、Added／Removed／Changed／Unchanged summary、count／storage difference、500-row paging、Scan Tree node selection、進度、虛擬化差異列表與 atomic streaming CSV export。 |
| 32 - Integration Testing and Performance | 完成 | 2026-07-29 | 已建立 Release 全量驗證腳本、跨模組失敗／壓力場景對照、7 個效能測試與可保存的 metrics；驗證 RAM budget、Snapshot Stream、live-read Handle、Watch、Filter、paging 與 Temp cleanup。 |
| 33 - Release, Documentation and Packaging | 完成 | 2026-07-29 | 已建立 v1.0.0 win-x64 self-contained portable／symbols ZIP、版本 metadata、manifest、SHA-256、Sample Plugin、Test Target、WPF／Target smoke tests及完整操作／安全文件。 |
| 34 - Process Memory Scanner Workbench | 完成 | 2026-07-30 | 已新增 Actual-value Exact Initial Snapshot、Scan Workflow orchestration 與 WPF Scan 分頁，支援 Exact／Unknown First Scan、估算、Next Scan、進度／取消、Pending Keep／Discard 及 Results 導覽。 |

## 驗證紀錄

- Phase 00：32 份 Phase 文件皆有相依階段，且相依圖無循環。
- Phase 01：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 4 個測試全部通過；Project Reference 無循環；WPF Native API 邊界檢查通過。
- Phase 02：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 33 個測試全部通過；Common、Core 與 Application 均不依賴 WPF MessageBox。
- Phase 03：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 45 個測試全部通過；首次啟動目錄建立、設定損壞復原與跨日期日誌輪替均通過。
- Phase 04：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 53 個測試全部通過；空清單、程序結束、Access Denied、取消、CPU 差值、記憶體格式與 live process 列舉均通過。
- Phase 05：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 60 個測試全部通過；非同步 refresh、搜尋、PID filter、排序、auto refresh、選取保留、消失標示與 Monitoring 命令均通過。
- Phase 06：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 75 個測試全部通過；完整 Session Identity、單一 Active Session、同目標冪等、AccessDenied／Invalidated、手動與自動存活檢查、目標結束失效、Stop 資源釋放及 WPF Start／Stop 整合均通過。
- Phase 07：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 95 個測試全部通過；x64 位址與區域邊界、State／Type／Protection 映射、Guard／NoAccess、Connected Session gate、SafeHandle 釋放、partial warning、取消及 live process Memory Region 列舉均通過。
- Phase 08：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 104 個測試全部通過；Region 格式、十六進位位址搜尋、Protection／Type／Readable／Writable filter、Size sort、選取保留、非同步 Refresh、partial warning、Session Stop 清除及 10,000 Regions row reuse 均通過。
- Phase 09：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 121 個測試全部通過；request／chunk validation、block／typed／batch read、partial data、無效位址、取消、AccessDenied、Session 變更、batch 共用 Handle、Handle 釋放及 live process 配置記憶體讀取均通過。
- Phase 10：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 183 個測試全部通過；9 種數值型別解析、整數上下界、十六進位、Invariant 浮點格式、little-endian 編碼、NaN／Infinity、浮點 tolerance、所有比對模式、Aligned／Unaligned、x64 candidate 與無效 scan request 均通過。
- Phase 11：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 196 個測試全部通過；Int32 exact scan、chunk boundary overlap、重疊 Region 去重、Aligned／Unaligned、Region skip、partial／failed read、max results、取消、chunk progress、無效 request 與非阻塞呼叫均通過。
- Phase 18：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 206 個測試全部通過；一百萬筆串流寫入與分頁讀回、address／fixed value layout、atomic temp rename、index rebuild、checksum／version 損壞偵測、crash temp recovery、取消清理與空 snapshot 均通過。
- Phase 12：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 216 個測試全部通過；全域對齊 estimate、磁碟容量、Int32 initial value、跨 chunk、Unaligned、重疊 Region 去重、partial read、取消、Session 失效、無效型別與 100,000 筆 disk-backed capture／paging 均通過。
- Phase 13：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 232 個測試全部通過；七種 Next Scan mode、previous-only candidate paging、batch read、current value persistence、signed／unsigned、float tolerance、invalid／partial address、warning bound、取消與 Session 失效均通過。
- Phase 14：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 242 個測試全部通過；Endpoint Compare、Continuous Changed／Unchanged／Increased／Decreased、期間變更後回復、read-failed exclusion、非同步進度、暫停／繼續、取消與 Session 失效均通過。
- Phase 15：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 249 個測試全部通過；Unchanged → Changed → Increased 多輪流程、Before／After Count、Pending gate、Keep、Discard 回 Parent、Duration summary、snapshot node 避碰、無效生命週期與失敗回復均通過。
- Phase 16：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 256 個測試全部通過；Undo／Redo 不重新讀取 Process、Active／Pending 重啟復原、完整 Round metadata、Rename、Delete pending snapshot、損壞 history 拒絕與 snapshot index 重建均通過。
- Phase 17：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 261 個測試全部通過；歷史節點分支、切換後繼續篩選、唯一 Active、Children／path 導覽、共同祖先比較、Pin 持久化、遞迴分支刪除、Active／root 保護與 v1 history 相容載入均通過。
- Phase 19：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 266 個測試全部通過；DeltaKeep／DeltaRemove 選擇與讀回、latest-value overlay、共享 parent reference count、引用安全刪除、長鏈週期壓縮、累積 Delta 50% 壓縮與 schema v3 重啟復原均通過。
- Phase 20：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 273 個測試全部通過；memory-preferred 預熱、lazy cache、disk-backed bypass、LRU 次序、node count／byte budget、多分支 eviction、降低預算立即釋放、設定持久化與 RAM／Disk usage 均通過。
- Phase 21：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 279 個測試全部通過；百萬筆結果只建立當頁 1,000 個 row、first／previous／next／last 分頁、lazy load cancellation、當頁 typed value sorting、address-only read status、Clipboard 與 Watch／Save action contract 均通過。
- Phase 22：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 288 個測試全部通過；Watch 批次讀取、單一位址失敗隔離、Previous／Current Value 與 Delta、Pause／Resume、型別切換、session 綁定、程序結束自動停止、Result Grid 串接與更新間隔設定均通過。
- Phase 23：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 302 個測試全部通過；schema v1 JSON round-trip、十六進位 x64 Address、原子寫入、SavedAddresses／Temp 隔離、CRUD、重複 Key、Import／Export、target mismatch、寫入失敗不發布、損壞檔提示及 reconnect 批次可讀性驗證均通過。
- Phase 24：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 334 個測試全部通過；九種型別 little／big endian 序列化、overflow／partial input、NaN／Infinity policy、Feature 預設關閉、授權確認、Session identity、expected-original mismatch、Mock verification、Denied／No-op boundary、成功／失敗 audit 與獨立 atomic Audit JSON 均通過；Core／Application 無 Native API。
- Phase 25：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 348 個測試全部通過；同一 SafeHandle 的 Query／original read／單次 write／read-back、Int32／Float live target 寫入、expected-original mismatch、唯讀與 Guard Region、跨 Region range、partial write、verification mismatch／read failure、Session identity mismatch、取消、目標結束、Handle 釋放與 audit 均通過。
- Phase 26：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 355 個測試全部通過；Results／Watch／Saved Address 編輯入口、Feature 關閉禁寫、Decimal／Hexadecimal preview、byte order／count、完整確認內容、寫入結果與 history、failed retry、Undo conflict、Saved current value refresh、CSV export，以及經 MemoryEditorViewModel 對自有 Test Target 的 Verified live write 均通過。
- Phase 27：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 361 個測試全部通過；Temp 統計、不完整檔清理、過期 Session 自動清除、Pinned 預設保留／明確強制刪除、Saved Address 隔離，以及 Compact 移除 orphan snapshot 後 Tree／Snapshot 可重新載入均通過。
- Phase 28：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 365 個測試全部通過；範例 Plugin 載入與 UI contribution 執行、Plugin service DI、獨立 log、Disable 後卸載與重啟不建立服務、損壞 Plugin 失敗隔離，以及不相容 API 在 assembly load 前拒絕均通過。
- Phase 29：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 375 個測試全部通過；Module／Thread 完整欄位映射、個別欄位失敗保留 row、集合中途失敗 partial list、Identity gate、目前程序 live enumeration、兩類查詢獨立失敗、搜尋／排序／格式化與 Session stop 清除均通過。
- Phase 30：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 382 個測試全部通過；固定 4 KiB window、Region boundary、16-byte Hex／ASCII formatting、partial／failed read placeholder、跨列 byte search、jump validation、page navigation、Region／Result 入口與 Session stop 清除均通過。
- Phase 31：Solution build 成功（0 warnings、0 errors）；3 個測試組件共 392 個測試全部通過；四種差異分類、summary／paging、layout validation、unordered snapshot detection、1,000,000-record bounded-page merge、WPF selection／navigation／export，以及 CSV atomic replace／failure preservation 均通過。
- Phase 32：Debug 與 Release Solution build 均成功（0 warnings、0 errors）；3 個測試組件共 402 個測試全部通過，7 個 Performance 測試另行通過；涵蓋掃描中程序失效、Access Denied、百萬 Candidate、多分支與連續 Undo／Branch、Snapshot／History 損壞、disk-full error mapping、Memory budget、長時間 Watch、快速切頁取消及 Editor feature flag。Release 實測 Snapshot write／read 為 2,072,132／2,128,937 records/s、Filter 112,341 candidates/s、Watch 42,947 refreshes/s、Temp 1,000 files 清理 2,735.0 ms、live read Handle growth 0，且 Snapshot 無 Stream 鎖定殘留。
- Phase 33：Release build 成功（0 warnings、0 errors），402 tests 全數通過；`Publish-Release.ps1` 成功建立 v1.0.0 win-x64 self-contained 主 ZIP 與 symbols ZIP，逐檔 manifest、SHA-256 sidecar、Sample Plugin 與 self-contained Test Target 齊全。封裝內容無 PDB／TestResults／bin／obj／`.tmp`／`.diag`／`.trx`；Test Target 協定與封裝後 WPF 啟動 smoke tests 通過。Smoke test 另發現並修正三類 WPF display binding 對唯讀 ViewModel 屬性的 TwoWay 啟動崩潰。
- Phase 34：Release build 成功（0 warnings、0 errors），Core 126、Windows 107、Integration 175，合計 408 tests 全數通過；新增 Exact Initial Snapshot 串流保存 target actual bytes、Snapshot Node ID 配置、First／Next Scan workflow rollback，以及 Session-bound WPF Scan Workbench。Release WPF startup smoke 通過；完整 Test Target UI click-through 保留為發行前手動驗收腳本。
