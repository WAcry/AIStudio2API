using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio2OpenAI.Models
{
    public class Model
    {
        [JsonPropertyName("id")] public string Id { get; set; }

        [JsonPropertyName("object")] public string Object { get; set; } = "model";

        [JsonPropertyName("owned_by")] public string OwnedBy { get; set; }
    }

    // Request Models
    public class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public string? Model { get; set; }

        [JsonPropertyName("messages")] public List<Message>? Messages { get; set; }

        [JsonPropertyName("stream")] public bool Stream { get; set; } = false;

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("role")] public string? Role { get; set; }

        [JsonPropertyName("content")]
        [JsonConverter(typeof(MessageContentConverter))]
        public object? Content { get; set; }
    }

    public class ContentPart
    {
        [JsonPropertyName("type")] public string Type { get; set; }

        [JsonPropertyName("text")] public string? Text { get; set; }

        [JsonPropertyName("image_url")] public ImageUrl? ImageUrl { get; set; }
    }

    public class ImageUrl
    {
        [JsonPropertyName("url")] public string Url { get; set; }
    }

    public class MessageContentConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                return JsonSerializer.Deserialize<List<ContentPart>>(ref reader, options);
            }

            throw new JsonException("Expected a string or an array for content.");
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }


    // Response Models
    public class ChatCompletionResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }

        [JsonPropertyName("object")] public string Object { get; set; } = "chat.completion";

        [JsonPropertyName("created")] public long Created { get; set; }

        [JsonPropertyName("model")] public string? Model { get; set; }

        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }

        [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    }

    public class Choice
    {
        [JsonPropertyName("index")] public int Index { get; set; }

        [JsonPropertyName("message")] public Message? Message { get; set; }

        [JsonPropertyName("finish_reason")] public string FinishReason { get; set; } = "stop";
    }

    public class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }

    // Streaming Response Models
    public class ChatCompletionStreamResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }

        [JsonPropertyName("object")] public string Object { get; set; } = "chat.completion.chunk";

        [JsonPropertyName("created")] public long Created { get; set; }

        [JsonPropertyName("model")] public string? Model { get; set; }

        [JsonPropertyName("choices")] public List<StreamChoice>? Choices { get; set; }

        [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    }

    public class StreamChoice
    {
        [JsonPropertyName("index")] public int Index { get; set; }

        [JsonPropertyName("delta")] public StreamDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    public class StreamDelta
    {
        [JsonPropertyName("role")] public string? Role { get; set; }

        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
