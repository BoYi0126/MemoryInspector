# Phase 23 - Saved Address JSON

## 相依階段

- Phase 03
- Phase 22

## 目標

建立 Key → Address → ValueType 的持久化功能。

## JSON

```json
{
  "schemaVersion": 1,
  "target": {
    "processName": "Example.exe",
    "architecture": "x64"
  },
  "addresses": {
    "Counter": {
      "address": "0x0000000012345678",
      "valueType": "Int32",
      "description": "Counter value"
    }
  }
}
```

## 功能

- Add
- Rename
- Update type
- Delete
- Import
- Export
- Duplicate key confirmation

## 規則

- Saved Address 與 Temp 分離。
- 清除 Scan Temp 不得刪除 Saved Address。
- 重新連接後 Address 需重新驗證可讀性。

## 驗收標準

- JSON 可版本化。
- 損壞檔案有錯誤提示。
