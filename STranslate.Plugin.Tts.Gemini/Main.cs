using STranslate.Plugin.Tts.Gemini.View;
using STranslate.Plugin.Tts.Gemini.ViewModel;
using System.Text.Json;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.Gemini;

/// <summary>
/// Google Gemini Speech API 语音合成插件的主入口类
/// </summary>
/// <remarks>
/// 实现 <see cref="ITtsPlugin"/> 接口，提供文本转语音功能。
/// 通过调用 Google Gemini API 的 generateContent 接口（开启 AUDIO modality），将文本转换为语音播放。
/// </remarks>
public class Main : ITtsPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

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

            // 配置HTTP请求头
            var option = new Options
            {
                Headers = new Dictionary<string, string>
                {
                    ["x-goog-api-key"] = Settings.ApiKey,
                    ["Content-Type"] = "application/json"
                }
            };

            var baseUrl = Settings.Url.TrimEnd('/');
            var apiUrl = $"{baseUrl}/v1beta/models/{Settings.Model}:generateContent";

            string response = string.Empty;
            int maxRetries = 3;
            int delayMs = 1000;

            // 带有指数退避的网络请求重试机制
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    response = await Context.HttpService.PostAsync(
                        apiUrl,
                        requestBody,
                        option,
                        cancellationToken);
                    break; // 成功则跳出重试循环
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1) throw; // 最后一次失败则向上抛出
                    
                    // 等待一段时间后重试
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2; // 指数退避
                }
            }

            // 解析JSON响应
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // 处理API返回的错误
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                Context.Snackbar.ShowError(errorMessage);
                return;
            }

            // 提取音频数据（Base64编码）
            var audioData = root
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("inlineData")
                .GetProperty("data")
                .GetString();

            // 验证音频数据是否为空
            if (string.IsNullOrEmpty(audioData))
            {
                Context.Snackbar.ShowWarning(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Audio_Empty"));
                return;
            }

            // 解码Base64
            var audioBytes = Convert.FromBase64String(audioData);

            // 尝试获取 mimeType
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

            // 如果是 raw PCM 数据，将其包装为 WAV 格式
            if (mimeType.Contains("audio/pcm") || mimeType.Contains("audio/l16") || (!mimeType.Contains("wav") && !mimeType.Contains("mp3") && !mimeType.Contains("ogg") && !mimeType.Contains("flac")))
            {
                // 默认 Gemini 返回 24000Hz, 16bit, 单声道 PCM
                int sampleRate = 24000;
                
                // 尝试从 mimeType 中提取采样率，例如 "audio/l16; rate=24000"
                var rateMatch = System.Text.RegularExpressions.Regex.Match(mimeType, @"rate=(\d+)");
                if (rateMatch.Success && int.TryParse(rateMatch.Groups[1].Value, out int parsedRate))
                {
                    sampleRate = parsedRate;
                }

                audioBytes = AddWavHeader(audioBytes, sampleRate, 1, 16);
                
                // STranslate 内置的 AudioPlayer 目前仅支持 MP3 格式。
                // 针对 WAV/PCM 数据，我们绕过 Context.AudioPlayer，直接使用 Windows 内置的 SoundPlayer 进行异步播放。
                await Task.Run(() =>
                {
                    using var ms = new System.IO.MemoryStream(audioBytes);
                    using var player = new System.Media.SoundPlayer(ms);
                    // 注册取消回调，以便在用户点击停止时停止播放
                    using var ctr = cancellationToken.Register(() => player.Stop());
                    player.PlaySync();
                }, cancellationToken);
            }
            else
            {
                // 播放音频 (例如如果是 MP3 格式，则可以使用内置的播放器)
                await Context.AudioPlayer.PlayAsync(audioBytes, cancellationToken);
            }
        }
        catch (JsonException)
        {
            // JSON解析失败（API响应格式错误）
            Context.Snackbar.ShowError(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Parse_Error"));
        }
        catch (FormatException)
        {
            // Base64解码失败（音频数据损坏）
            Context.Snackbar.ShowError(Context.GetTranslation("STranslate_Plugin_Tts_Gemini_Decode_Error"));
        }
        catch (TaskCanceledException)
        {
            // 忽略因取消令牌引发的异常
        }
        catch (Exception ex)
        {
            // 其他异常（如网络请求失败）
            Context.Snackbar.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// 为 Raw PCM 音频数据添加 WAV 文件头
    /// </summary>
    private byte[] AddWavHeader(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        int headerSize = 44;
        byte[] wavData = new byte[headerSize + pcmData.Length];

        // Chunk ID: "RIFF"
        wavData[0] = 0x52; wavData[1] = 0x49; wavData[2] = 0x46; wavData[3] = 0x46;
        
        // Chunk Size: pcmData.Length + 36
        int chunkSize = pcmData.Length + 36;
        var chunkSizeBytes = BitConverter.GetBytes(chunkSize);
        Array.Copy(chunkSizeBytes, 0, wavData, 4, 4);

        // Format: "WAVE"
        wavData[8] = 0x57; wavData[9] = 0x41; wavData[10] = 0x56; wavData[11] = 0x45;
        
        // Subchunk1 ID: "fmt "
        wavData[12] = 0x66; wavData[13] = 0x6D; wavData[14] = 0x74; wavData[15] = 0x20;
        
        // Subchunk1 Size: 16 (for PCM)
        wavData[16] = 16; wavData[17] = 0; wavData[18] = 0; wavData[19] = 0;
        
        // Audio Format: 1 (PCM)
        wavData[20] = 1; wavData[21] = 0;
        
        // Num Channels
        wavData[22] = (byte)channels; wavData[23] = (byte)(channels >> 8);
        
        // Sample Rate
        var sampleRateBytes = BitConverter.GetBytes(sampleRate);
        Array.Copy(sampleRateBytes, 0, wavData, 24, 4);
        
        // Byte Rate: SampleRate * NumChannels * BitsPerSample / 8
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        var byteRateBytes = BitConverter.GetBytes(byteRate);
        Array.Copy(byteRateBytes, 0, wavData, 28, 4);
        
        // Block Align: NumChannels * BitsPerSample / 8
        int blockAlign = channels * bitsPerSample / 8;
        wavData[32] = (byte)blockAlign; wavData[33] = (byte)(blockAlign >> 8);
        
        // Bits Per Sample
        wavData[34] = (byte)bitsPerSample; wavData[35] = (byte)(bitsPerSample >> 8);
        
        // Subchunk2 ID: "data"
        wavData[36] = 0x64; wavData[37] = 0x61; wavData[38] = 0x74; wavData[39] = 0x61;
        
        // Subchunk2 Size: pcmData.Length
        var subchunk2SizeBytes = BitConverter.GetBytes(pcmData.Length);
        Array.Copy(subchunk2SizeBytes, 0, wavData, 40, 4);
        
        // Copy PCM Data
        Array.Copy(pcmData, 0, wavData, 44, pcmData.Length);

        return wavData;
    }
}
