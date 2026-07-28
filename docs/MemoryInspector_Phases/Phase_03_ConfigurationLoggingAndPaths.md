# Phase 03 - Configuration, Logging and Paths

## 相依階段

- Phase 01
- Phase 02

## 目標

建立設定、日誌、資料目錄與版本化機制。

## 預設目錄

```text
%LocalAppData%\MemoryInspector\
├─ Config
├─ Temp
├─ Sessions
├─ SavedAddresses
├─ Plugins
└─ Logs
```

## 設定項目

- Memory budget
- Cached node count
- Page size
- Snapshot threshold
- Temp retention days
- Process refresh interval
- Watch refresh interval
- Default numeric tolerance

## 實作內容

- `IAppPathService`
- `ISettingsService`
- `ILoggingBootstrapper`
- 設定版本欄位
- 預設值與損壞設定復原

## 驗收標準

- 第一次啟動自動建立目錄。
- 設定檔損壞時使用預設值並記錄日誌。
- 日誌可依日期輪替。
