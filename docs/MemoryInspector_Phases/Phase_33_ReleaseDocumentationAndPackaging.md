# Phase 33 - Release, Documentation and Packaging

## 相依階段

- Phase 32

## 目標

完成 Release、安裝包與使用文件。

## 文件

- README
- Architecture
- User Guide
- Scanner Guide
- Filter Pipeline Guide
- Scan Tree Guide
- Temp Storage Guide
- Plugin Guide
- Troubleshooting
- Privacy / security notes

## 封裝

- x64 Release
- Self-contained 或 Framework-dependent 策略
- Versioning
- Changelog
- License
- Symbol package
- Sample plugin
- Test target

## 驗收標準

- 全新 Windows 環境可啟動。
- 文件涵蓋主要功能。
- Release 包不包含測試暫存。

## 發佈策略

- 正式套件採 `win-x64` self-contained、folder-based portable ZIP。
- 使用者不需預先安裝 .NET；解壓縮後直接執行 `MemoryInspector.Wpf.exe`。
- 不啟用 trimming、single-file 或 ReadyToRun，以保留 WPF、DI、Plugin reflection 與故障診斷的可預期性。
- 主套件不含 PDB；PDB 另置於版本相同的 symbols ZIP。
- Sample Plugin 與自有 Test Target 隨附於獨立子目錄，不會在未經使用者操作時自動載入或啟動。
- 在程式碼簽章與安裝程式身分完成前，不提供會修改登錄或系統目錄的 MSI/MSIX；portable ZIP 是本階段的安裝／散佈格式。

## 自動化

從 repository root 執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File .\scripts\Publish-Release.ps1
```

腳本會先執行 Phase 32 Release 驗證，再建立：

```text
artifacts/release/
├─ MemoryInspector-1.0.0-win-x64/
├─ MemoryInspector-1.0.0-win-x64.zip
├─ MemoryInspector-1.0.0-win-x64.zip.sha256
├─ MemoryInspector-1.0.0-win-x64-symbols.zip
└─ MemoryInspector-1.0.0-win-x64-symbols.zip.sha256
```

主套件包含 self-contained WPF app、README、License、Changelog、release manifest、完整使用文件、Sample Plugin 與 self-contained Test Target。驗證會拒絕任何 `TestResults`、`bin`、`obj`、`.vs`、`.tmp`、`.diag` 或 `.trx` 檔案進入 ZIP。

## 文件交付

- `docs/Architecture.md`
- `docs/UserGuide.md`
- `docs/ScannerGuide.md`
- `docs/FilterPipelineGuide.md`
- `docs/ScanTreeGuide.md`
- `docs/TempStorageGuide.md`
- `docs/PluginGuide.md`
- `docs/Troubleshooting.md`
- `docs/SecurityAndPrivacy.md`
- `CHANGELOG.md`
- `LICENSE`

## 版本規則

- 產品採 Semantic Versioning：`MAJOR.MINOR.PATCH[-prerelease]`。
- `Directory.Build.props` 提供預設 `VersionPrefix`、Assembly／File version 與產品 metadata。
- 發佈腳本的 `-Version` 可覆寫 package／assembly informational version；正式版應先同步更新 `VersionPrefix` 與 `CHANGELOG.md`。
- 每個 ZIP 都產生 SHA-256 sidecar；`release-manifest.json` 另列出套件內每個檔案的大小與 SHA-256。

## 驗證結果

- Phase 32 Release build 與 402 個 tests 通過。
- `win-x64` self-contained publish 成功。
- 解壓後的 `MemoryInspector.Wpf.exe` 可在不依賴系統 .NET runtime 的模式啟動。
- 封裝後 WPF 與 Test Target smoke tests 已納入發佈腳本並通過。
- 主 ZIP 與 symbols ZIP 可重新開啟，且 SHA-256 sidecar 與實際檔案一致。
- 主 ZIP 不含 PDB、測試暫存、build intermediate 或診斷輸出。

## v1.0.0 產物

| 產物 | 大小 | SHA-256 |
|---|---:|---|
| `MemoryInspector-1.0.0-win-x64.zip` | 111,159,988 bytes | `4b03abe9c422da859d050966060cee7e9f9b74aa6e3275315f1af979e174ac0f` |
| `MemoryInspector-1.0.0-win-x64-symbols.zip` | 173,227 bytes | `fefbe99d103216277ee39b223b4f71aade948828e3e865fd57f5139c1d972327` |

主套件 manifest 列出 750 個檔案，另含 manifest 本身；主套件 PDB 數為 0，symbols package 含 8 個 PDB。
