using AIStudio2OpenAI.Models;
using AIStudio2OpenAI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio2OpenAI.Controllers;

[ApiController]
[Route("v1/chat")]
public class ChatController : ControllerBase
{
    private readonly IGeminiService _geminiService;
    private readonly ILogger<ChatController> _logger;
    private static readonly HashSet<string> SupportedModels = ["gemini-2.5-pro", "gemini-2.5-flash"];

    public ChatController(IGeminiService geminiService, ILogger<ChatController> logger)
    {
        _geminiService = geminiService;
        _logger = logger;
    }

    [HttpPost("completions")]
    public async Task<IActionResult> GetChatCompletion([FromBody] ChatCompletionRequest? request)
    {
        if (request?.Messages == null || request.Messages.Count == 0)
        {
            return BadRequest("Invalid request payload: 'messages' is required.");
        }

        if (string.IsNullOrEmpty(request.Model) || !SupportedModels.Contains(request.Model))
        {
            return BadRequest(
                $"Invalid or unsupported model. Supported models are: {string.Join(", ", SupportedModels)}");
        }

        try
        {
            var (content, usage) = await _geminiService.GetResponseAsync(request);

            if (request.Stream)
            {
                return await HandleStreamingResponse(request.Model, content, usage);
            }

            var response = new ChatCompletionResponse
            {
                Id = $"cmpl-{Guid.NewGuid()}",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Model = request.Model,
                Choices =
                [
                    new Choice
                    {
                        Index = 0,
                        Message = new Message
                        {
                            Role = "assistant",
                            Content = content
                        }
                    }
                ],
                Usage = usage
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the chat completion request.");
            return StatusCode(500, new { error = new { message = $"An internal error occurred: {ex.Message}" } });
        }
    }

    private async Task<IActionResult> HandleStreamingResponse(string model, string content, Usage usage)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.AccessControlAllowOrigin = "*";

        var completionId = $"cmpl-{Guid.NewGuid()}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        try
        {
            // Send content chunk
            var streamResponse = new ChatCompletionStreamResponse
            {
                Id = completionId,
                Created = created,
                Model = model,
                Choices =
                [
                    new StreamChoice
                    {
                        Index = 0,
                        Delta = new StreamDelta { Role = "assistant", Content = content }
                    }
                ]
            };
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(streamResponse, jsonOptions)}\n\n");
            await Response.Body.FlushAsync();

            var finalStreamResponse = new ChatCompletionStreamResponse
            {
                Id = completionId,
                Created = created,
                Model = model,
                Choices =
                [
                    new StreamChoice
                    {
                        Index = 0,
                        Delta = new StreamDelta(),
                        FinishReason = "stop"
                    }
                ],
                Usage = usage
            };
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(finalStreamResponse, jsonOptions)}\n\n");
            await Response.Body.FlushAsync();

            // Send DONE signal
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during streaming.");
        }

        return new EmptyResult();
    }
}