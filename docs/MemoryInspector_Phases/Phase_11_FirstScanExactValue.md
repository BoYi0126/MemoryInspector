# Phase 11 - First Scan: Exact Value

## 相依階段

- Phase 07
- Phase 09
- Phase 10

## 目標

完成第一個可用的 Exact Value First Scan。

## 流程

1. 取得可掃描 Region。
2. Chunk read。
3. 將輸入轉為 byte pattern。
4. 搜尋 match。
5. 建立 Candidate。
6. 回報進度。
7. 支援取消。

## 邊界處理

- Chunk overlap
- Duplicate address
- Partial read
- Max result count
- Region skip policy

## 效能原則

- 不逐筆更新 UI。
- 不為每個 Candidate 建立 ViewModel。
- 使用緊湊位址結構。

## 驗收標準

- 可在測試程式中找到 Int32 值。
- UI 不凍結。
- 可取消掃描。
