# Phase 14 - Duration Filter

## 相依階段

- Phase 13

## 目標

加入依秒數執行的 Changed / Unchanged / Increased / Decreased 篩選。

## 使用方式

```text
Mode: Unchanged
Duration: 10 seconds
```

或：

```text
Mode: Changed
Duration: 5 seconds
```

## 兩種模式

### Endpoint Compare
- 比較開始值與結束值。
- 記憶體用量低。

### Continuous Observe
- 期間定期取樣。
- 只保存旗標，不保存完整時間序列。

## 狀態旗標

- HasChanged
- HasIncreased
- HasDecreased
- ReadFailed

## 要求

- 非同步倒數。
- 可取消。
- 可暫停。
- 顯示進度。
- 不阻塞 UI。

## 驗收標準

- `Unchanged 10s` 正確保留全程未變項目。
- `Changed 5s` 正確保留至少改變一次的項目。
