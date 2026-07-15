namespace STranslate.Plugin.Tts.Gemini;

/// <summary>
/// 插件配置模型，定义语音合成服务的各项参数
/// </summary>
/// <remarks>
/// 配置通过 <see cref="IPluginContext.LoadSettingStorage{T}"/> 持久化存储。
/// 属性变更时被 <see cref="ViewModel.SettingsViewModel"/> 自动保存。
/// </remarks>
public class Settings
{
    /// <summary>
    /// Gemini API 接口基础地址
    /// </summary>
    /// <value>
    /// 默认为 Google 官方地址。
    /// 可根据需要替换为反向代理地址。
    /// </value>
    public string Url { get; set; } = "https://generativelanguage.googleapis.com";

    /// <summary>
    /// Google Gemini API Key
    /// </summary>
    /// <value>
    /// 用于API身份验证。请前往
    /// https://aistudio.google.com/app/apikey 获取。
    /// </value>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// 语音合成模型标识名
    /// </summary>
    /// <value>
    /// 指定使用的TTS模型。
    /// </value>
    public string Model { get; set; } = "gemini-2.5-flash-preview-tts";

    /// <summary>
    /// 语音音色标识
    /// </summary>
    /// <value>
    /// 指定合成语音的音色。可选值包括：
    /// <list type="bullet">
    ///   <item><c>Puck</c> - 默认音色</item>
    ///   <item><c>Charon</c></item>
    ///   <item><c>Kore</c></item>
    ///   <item><c>Fenrir</c></item>
    ///   <item><c>Aoede</c></item>
    /// </list>
    /// </value>
    public string Voice { get; set; } = "Aoede";
}
