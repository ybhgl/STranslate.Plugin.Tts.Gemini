# STranslate Google Gemini 语音合成插件

基于 Google Gemini API 的 [STranslate](https://github.com/ZGGSONG/STranslate) TTS 插件，支持 Google 最新的 Speech Generation 语音合成服务。

## 📦 安装

1. 下载最新的 `.spkg` 文件（在 [Releases](https://github.com/ybhgl/STranslate.Plugin.Tts.Gemini/releases) 页面）
2. 在 STranslate 中进入 **设置** -> **插件** -> **安装插件**
3. 选择下载的 `.spkg` 文件，然后重启或直接在 TTS 服务中启用

## 🔑 前置条件

需要注册并获取 [Google AI Studio](https://aistudio.google.com/app/apikey) 的 API Key。

## ⚙️ 配置

| 参数 | 默认值 | 说明 |
|------|--------|------|
| 接口地址 | `https://generativelanguage.googleapis.com` | Google Gemini API 地址 |
| API Key | - | 申请到的 Google Gemini API Key |
| 模型 | `gemini-2.5-flash-preview-tts` | 语音合成模型 |
| 声音 | `Aoede` | 发音音色 |

## 🚀 使用方式

安装插件后，在 STranslate 的 TTS 服务中选择 "Gemini TTS"，并在配置页填入对应的 API Key。即可在翻译或选中文本时使用 Google Gemini 进行高质量的语音合成。

## 📄 许可证

[MIT](LICENSE)
