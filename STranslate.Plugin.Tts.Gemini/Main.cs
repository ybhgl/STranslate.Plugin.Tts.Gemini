using STranslate.Plugin.Tts.Gemini.View;
using STranslate.Plugin.Tts.Gemini.ViewModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using NAudio.Wave;

namespace STranslate.Plugin.Tts.Gemini;

/// <summary>
/// Google Gemini Speech API 语音合成插件的主入口类
/// </summary>
/// <remarks>
/// 实现 <see cref="ITtsPlugin"/> 接口，提供文本转语音功能。
/// 通过调用 Google Gemini API 的 generateContent / streamGenerateContent 接口（开启 AUDIO modality），将文本转换为语音播放。
/// </remarks>
public class Main : ITtsPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;
    
    // 用于流式请求的独立 HttpClient
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// 获取插件的设置界面
    /// </summary>
    /// <returns>包含设置UI的控件</returns>
    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    /// <summary>
    /// 初始化插件，加载持久化的配置
    /// </summary>
    /// <param name="context">插件运行时上下文</param>
    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
    }

    /// <summary>
    /// 释放插件占用的资源
    /// </summary>
    public void Dispose() => _viewModel?.Dispose();

    /// <summary>
    /// 播放文本的语音合成
    /// </summary>
    /// <param name="text">要转换的文本内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task PlayAudioAsync(string text, CancellationToken cancellationToken = default)
    {
        // 验证API Key是否已配置
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            Context.Snackbar.ShowError(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_ApiKey_Empty"));
            return;
        }

        // 验证文本内容是否为空
        if (string.IsNullOrWhiteSpace(text))
        {
            Context.Snackbar.ShowWarning(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Text_Empty"));
            return;
        }

        try
        {
            // 构造 Gemini Speech Generation 对应的请求体
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = text }
                        }
                    }
                },
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new
                    {
                        voiceConfig = new
                        {
                            prebuiltVoiceConfig = new
                            {
                                voiceName = Settings.Voice
                            }
                        }
                    }
                }
            };

            var baseUrl = Settings.Url.TrimEnd('/');

            if (Settings.IsStreaming)
            {
                await PlayStreamingAsync(baseUrl, requestBody, cancellationToken);
            }
            else
            {
                await PlayNonStreamingAsync(baseUrl, requestBody, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            // 忽略因取消令牌引发的异常
        }
        catch (Exception ex)
        {
            Context.Snackbar.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// 流式获取并播放 (边下边播)
    /// </summary>
    private async Task PlayStreamingAsync(string baseUrl, object requestBody, CancellationToken cancellationToken)
    {
        var apiUrl = $"{baseUrl}/v1beta/models/{Settings.Model}:streamGenerateContent?alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Headers.Add("x-goog-api-key", Settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        // 使用 ResponseHeadersRead 确保流式获取数据
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            ExtractAndShowError(errorBody);
            return;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // 初始化 NAudio 播放器组件 (24000Hz, 16bit, Mono 是 Gemini TTS 的默认 PCM 格式)
        var waveFormat = new WaveFormat(24000, 16, 1);
        var bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromMinutes(10), // 足够大的缓冲区以防止溢出
            DiscardOnBufferOverflow = true
        };

        using var waveOut = new WaveOutEvent();
        waveOut.Init(bufferedWaveProvider);
        waveOut.Play();

        // 当触发取消操作时停止播放
        using var ctr = cancellationToken.Register(() => waveOut.Stop());

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break; // 读到流末尾
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var jsonStr = line.Substring(6).Trim();
                if (jsonStr == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var content = candidates[0].GetProperty("content");
                        if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                        {
                            var part = parts[0];
                            if (part.TryGetProperty("inlineData", out var inlineData) && inlineData.TryGetProperty("data", out var dataProp))
                            {
                                var base64Data = dataProp.GetString();
                                if (!string.IsNullOrEmpty(base64Data))
                                {
                                    byte[] audioBytes = Convert.FromBase64String(base64Data);
                                    bufferedWaveProvider.AddSamples(audioBytes, 0, audioBytes.Length);
                                }
                            }
                        }
                    }
                }
                catch (JsonException) { /* 忽略无法解析的分块 */ }
            }
        }

        // 网络流接收完毕后，等待缓冲区内已有的音频播放完毕
        while (waveOut.PlaybackState == PlaybackState.Playing && bufferedWaveProvider.BufferedBytes > 0 && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, cancellationToken);
        }

        waveOut.Stop();
    }

    /// <summary>
    /// 非流式获取并播放 (全部下载后播放)
    /// </summary>
    private async Task PlayNonStreamingAsync(string baseUrl, object requestBody, CancellationToken cancellationToken)
    {
        var apiUrl = $"{baseUrl}/v1beta/models/{Settings.Model}:generateContent";

        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                ["x-goog-api-key"] = Settings.ApiKey,
                ["Content-Type"] = "application/json"
            }
        };

        string response = string.Empty;
        int maxRetries = 3;
        int delayMs = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                response = await Context.HttpService.PostAsync(apiUrl, requestBody, option, cancellationToken);
                break;
            }
            catch (Exception)
            {
                if (i == maxRetries - 1) throw;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                Context.Snackbar.ShowError(errorMessage);
                return;
            }

            var audioData = root
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("inlineData")
                .GetProperty("data")
                .GetString();

            if (string.IsNullOrEmpty(audioData))
            {
                Context.Snackbar.ShowWarning(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Audio_Empty"));
                return;
            }

            var audioBytes = Convert.FromBase64String(audioData);

            string mimeType = "unknown";
            try
            {
                var inlineData = root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("inlineData");
                if (inlineData.TryGetProperty("mimeType", out var mimeProp))
                {
                    mimeType = mimeProp.GetString() ?? "unknown";
                }
            }
            catch { }

            // 处理纯 PCM 流
            if (mimeType.Contains("audio/pcm") || mimeType.Contains("audio/l16") || 
               (!mimeType.Contains("wav") && !mimeType.Contains("mp3") && !mimeType.Contains("ogg") && !mimeType.Contains("flac")))
            {
                int sampleRate = 24000;
                var rateMatch = System.Text.RegularExpressions.Regex.Match(mimeType, @"rate=(\d+)");
                if (rateMatch.Success && int.TryParse(rateMatch.Groups[1].Value, out int parsedRate))
                {
                    sampleRate = parsedRate;
                }

                await Task.Run(async () =>
                {
                    using var ms = new MemoryStream(audioBytes);
                    using var rawSource = new RawSourceWaveStream(ms, new WaveFormat(sampleRate, 16, 1));
                    using var waveOut = new WaveOutEvent();
                    
                    waveOut.Init(rawSource);
                    waveOut.Play();

                    using var ctr = cancellationToken.Register(() => waveOut.Stop());

                    while (waveOut.PlaybackState == PlaybackState.Playing && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }, cancellationToken);
            }
            else
            {
                // 如果是标准压缩格式，交给宿主播放器
                await Context.AudioPlayer.PlayAsync(audioBytes, cancellationToken);
            }
        }
        catch (JsonException)
        {
            Context.Snackbar.ShowError(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Parse_Error"));
        }
        catch (FormatException)
        {
            Context.Snackbar.ShowError(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Decode_Error"));
        }
    }

    /// <summary>
    /// 解析并展示API返回的错误信息
    /// </summary>
    private void ExtractAndShowError(string errorBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.TryGetProperty("error", out var errorElement) && 
                errorElement.TryGetProperty("message", out var msgElement))
            {
                Context.Snackbar.ShowError(msgElement.GetString() ?? "Unknown API Error");
            }
            else
            {
                Context.Snackbar.ShowError("API Request Failed.");
            }
        }
        catch
        {
            Context.Snackbar.ShowError("API Request Failed.");
        }
    }
}