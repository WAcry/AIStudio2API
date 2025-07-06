using AIStudio2OpenAI.Models;

namespace AIStudio2OpenAI.Services
{
    public interface IGeminiService
    {
        Task<(string, Usage)> GetResponseAsync(ChatCompletionRequest? request);
    }
}