# Phase 12 - Unknown Initial Value

## 相依階段

- Phase 11
- Phase 18

## 目標

在不知道初始值時建立基準 Snapshot。

## 設計

Unknown Initial Value 不依賴特定輸入值。

需保存：

- Candidate address
- Initial value
- Value type
- Snapshot metadata

## 限制

- 使用者必須選擇 Value Type。
- 預估結果數量與容量。
- 超過 RAM 門檻時使用 Disk-backed storage。
- 顯示預估磁碟用量。

## 驗收標準

- 可建立 Unknown Snapshot。
- 大量資料不全數常駐 ViewModel。
