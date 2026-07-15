# 更新日志 (Changelog)

## [1.1.0]

### 新增 (Added)
- 支持 Gemini TTS 流式传输音频：
  - 使用 SSE 和 NAudio 实现流式音频播放。
  - 在设置中添加流式传输开关，以减少首字节时间（TTFB）。
  - 添加 `NAudio` 项目依赖项以用于音频处理。

## [1.0.0]

### 新增 (Added)
- 初次发布：Google Gemini 语音合成 (Speech Generation) 插件。
