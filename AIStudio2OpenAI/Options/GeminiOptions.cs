#nullable enable

namespace AIStudio2OpenAI.Options
{
    public class GeminiOptions
    {
        public bool SetMaxThinkingTokens { get; set; } = true;
        public bool EnableCodeExecution { get; set; } = false;
        public bool EnableWebSearch { get; set; } = false;
    }
}