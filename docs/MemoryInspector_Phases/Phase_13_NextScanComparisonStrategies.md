# Phase 13 - Next Scan Comparison Strategies

## 相依階段

- Phase 11
- Phase 12

## 目標

完成多種 Next Scan 比對策略。

## 模式

- Exact Value
- Changed
- Unchanged
- Increased
- Decreased
- Greater Than
- Less Than

## 規則

每次 Next Scan 只能使用上一輪 Candidate。

每個 Candidate 更新：

- Previous Value
- Current Value
- Match status
- Read status

## 測試

- 整數增減。
- 浮點 tolerance。
- 失效地址。
- Signed / unsigned。
- Changed / Unchanged。

## 驗收標準

- 所有模式均有單元測試。
- 不重新掃描整個 Process。
