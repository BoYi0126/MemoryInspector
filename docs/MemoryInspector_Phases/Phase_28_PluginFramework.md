# Phase 28 - Plugin Framework

## 相依階段

- Phase 01
- Phase 03

## 目標

建立可擴充的 Plugin Framework。

## Plugin 類型

- Analyzer
- Viewer
- Exporter
- Decoder
- Scanner Extension

## 需求

- Plugin manifest
- Version compatibility
- Enable / Disable
- Load failure isolation
- Plugin log
- DI scope
- UI contribution contract

## 第一版限制

- 不提供任意核心權限。
- Plugin API 明確版本化。
- Plugin 載入失敗不得影響主程式啟動。

## 驗收標準

- 可載入範例 Plugin。
- 禁用後不再建立 Plugin service。
