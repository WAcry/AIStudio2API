using AIStudio2OpenAI.Helpers;
using AIStudio2OpenAI.Models;
using AIStudio2OpenAI.Options;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Text;
using System.Text.RegularExpressions;

namespace AIStudio2OpenAI.Services
{
    public class GeminiService : IGeminiService
    {
        private readonly IOptions<ChromeAutomationOptions> _chromeAutomationOptions;
        private readonly IOptions<GeminiOptions> _geminiOptions;
        private readonly ILogger<GeminiService> _logger;
        private string DebuggingUrl => $"http://localhost:{_chromeAutomationOptions.Value.DebuggingPort}";
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private readonly HttpClient _httpClient = new();
        private int _currentAccountIndex;
        private readonly int _maxAccounts;
        private static readonly Random _random = new();

        private static readonly Dictionary<string, string> ModelIdToDisplayName = new()
        {
            { "gemini-2.5-pro", "Gemini 2.5 Pro" },
            { "gemini-2.5-flash", "Gemini 2.5 Flash" }
        };

        public GeminiService(
            IOptions<ChromeAutomationOptions> chromeAutomationOptions,
            IOptions<GeminiOptions> geminiOptions,
            ILogger<GeminiService> logger)
        {
            _chromeAutomationOptions = chromeAutomationOptions;
            _geminiOptions = geminiOptions;
            _logger = logger;
            _maxAccounts = chromeAutomationOptions.Value.MaxAccounts;
            _currentAccountIndex = new Random().Next(_maxAccounts);
        }

        public async Task InitializeAsync()
        {
            var playwright = await Playwright.CreateAsync();
            _browser = await playwright.Chromium.ConnectOverCDPAsync(DebuggingUrl);
            _context = _browser.Contexts.FirstOrDefault() ?? await _browser.NewContextAsync();

            if (_context == null)
            {
                throw new InvalidOperationException(
                    "Could not find or create a browser context. Ensure Chrome is running and accessible.");
            }
        }

        public async Task<(string, Usage)> GetResponseAsync(ChatCompletionRequest? request)
        {
            if (_context == null)
            {
                throw new InvalidOperationException("Browser context is not initialized. Call InitializeAsync first.");
            }

            if (request?.Messages is null || request.Messages.Count == 0 || string.IsNullOrEmpty(request.Model))
            {
                throw new ArgumentException("Invalid request: Messages and Model are required.");
            }

            var page = await _context.NewPageAsync();
            var tempFiles = new List<string>();

            try
            {
                await page.GotoAsync(GetNextAccountUrl(),
                    new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90_000 });

                await ModelSelectorHelper.SelectModelAsync(page, request, _geminiOptions.Value, _logger);

                var promptInput = page.GetByLabel("Start typing a prompt")
                    .Or(page.GetByLabel(new Regex("type something", RegexOptions.IgnoreCase)));

                var runButton = page.Locator("run-button button:has-text('Run')");
                var stopButton = page.Locator("run-button button:has-text('Stop')");

                var promptBuilder = new StringBuilder();
                foreach (var message in request.Messages)
                {
                    await HandleContentPartAsync(message, promptBuilder, tempFiles);
                }

                if (tempFiles.Any())
                {
                    var insertButton = page.GetByLabel("Insert assets such as images, videos, files, or audio");
                    await ClickWithRandomDelayAsync(insertButton);
                    await Task.Delay(1000);

                    var uploadMenuItem = page.GetByRole(AriaRole.Menuitem, new() { Name = "Upload File" });
                    var fileInput = uploadMenuItem.Locator("input[type='file']");

                    await fileInput.SetInputFilesAsync(tempFiles);
                    await page.WaitForTimeoutAsync(1000); // Wait for UI to update
                    await page.Keyboard.PressAsync("Escape");
                }

                var fullPrompt = promptBuilder.ToString().Trim();

                await promptInput.FillAsync(fullPrompt);
                await page.EvaluateAsync("""
                                         () => {
                                                     const textarea = document.querySelector('ms-autosize-textarea textarea');
                                                     if (textarea) {
                                                         textarea.style.display = 'none';
                                                     }
                                                 }
                                         """);
                await page.EvaluateAsync("""
                                         () => {
                                                     const div = document.querySelector('.very-large-text-container');
                                                     if (div) {
                                                         div.style.display = 'none';
                                                     }
                                                 }
                                         """);
                await Task.Delay(1000);
 
                var inputTokenCount = await GetCurrentTokenCountAsync(page);

                await Assertions.Expect(runButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
                await ClickWithRandomDelayAsync(runButton);
                await Task.Delay(1000);

                await Assertions.Expect(stopButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
                await Assertions.Expect(stopButton).ToBeHiddenAsync(new() { Timeout = 1_200_000 });
                await Task.Delay(1000);

                var warnings = await PostResponseVerificationAsync(page, request.Model);

                string responseText = await GetResponseMarkdownAsync(page);

                var finalResponse = new StringBuilder(responseText.Trim());
                if (warnings.Any())
                {
                    finalResponse.Append("\n\n--- System Warning ---\n");
                    finalResponse.Append(string.Join("\n", warnings));
                }

                var outputTokenCount = await GetCurrentTokenCountAsync(page);

                return (finalResponse.ToString(), new Usage
                {
                    PromptTokens = inputTokenCount,
                    CompletionTokens = outputTokenCount,
                    TotalTokens = inputTokenCount + outputTokenCount
                });
            }
            finally
            {
                if (_context.Pages.Count > 1)
                {
                    await page.CloseAsync();
                }

                foreach (var file in tempFiles)
                {
                    if (File.Exists(file))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete temporary file: {FilePath}", file);
                        }
                    }
                }
            }
        }

        private async Task<List<string>> PostResponseVerificationAsync(IPage page, string requestedModel)
        {
            var warnings = new List<string>();

            // 1. Verify Model
            var modelNameLocator = page.Locator("div.model-option-content span.gmat-body-medium");
            var modelName = await modelNameLocator.First.InnerTextAsync();
            var expectedModelName = ModelIdToDisplayName.GetValueOrDefault(requestedModel, requestedModel);

            if (!modelName.Contains(expectedModelName))
            {
                var warning = $"Unexpected model '{modelName}' detected. Expected '{expectedModelName}'.";
                _logger.LogWarning(warning);
                warnings.Add(warning);
            }

            // 2. Verify "Thoughts" panel
            var thoughtsPanelLocator = page.Locator("mat-panel-title:has-text('Thoughts')");
            if (await thoughtsPanelLocator.CountAsync() == 0)
            {
                var warning =
                    "No 'Thoughts' process detected for the model. The model may have been downgraded or the response is abnormal.";
                _logger.LogWarning(warning);
                warnings.Add(warning);
            }

            return warnings;
        }

        private static async Task<string> GetResponseMarkdownAsync(IPage page)
        {
            var modelResponseLocator = page.Locator(".chat-turn-container.model .turn-content").Last;
            await ClickWithRandomDelayAsync(modelResponseLocator);
            await Task.Delay(500);

            var lastMoreOptionsButton = page.GetByRole(AriaRole.Button, new() { Name = "Open options" }).Last;
            await ClickWithRandomDelayAsync(lastMoreOptionsButton);
            await Task.Delay(500);

            await ClickWithRandomDelayAsync(page.GetByRole(AriaRole.Menuitem, new() { Name = "Copy markdown" }));
            await Task.Delay(500);

            return await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        }

        private string GetNextAccountUrl() =>
            $"https://aistudio.google.com/u/{GetNextAccountIndex()}/prompts/new_chat";

        private int GetNextAccountIndex() =>
            Interlocked.Increment(ref _currentAccountIndex) % _maxAccounts;

        private async Task HandleContentPartAsync(Message message, StringBuilder promptBuilder, List<string> tempFiles)
        {
            if (message.Content is string textContent)
            {
                promptBuilder.Append(AddTag(message.Role, textContent));
            }
            else if (message.Content is List<ContentPart> contentParts)
            {
                var textParts = new List<string>();
                foreach (var part in contentParts)
                {
                    if (part.Type == "text" && !string.IsNullOrEmpty(part.Text))
                    {
                        textParts.Add(part.Text);
                    }
                    else if (part.Type == "image_url" && part.ImageUrl != null)
                    {
                        var imagePath = await DownloadOrDecodeImageAsync(part.ImageUrl.Url);
                        tempFiles.Add(imagePath);
                    }
                }

                if (textParts.Any())
                {
                    promptBuilder.Append(AddTag(message.Role, string.Join("\n", textParts)));
                }
            }
        }

        private async Task<string> DownloadOrDecodeImageAsync(string url)
        {
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

            if (url.StartsWith("data:image"))
            {
                var match = Regex.Match(url, @"data:image/(?<type>.+?);base64,(?<data>.+)");
                if (match.Success)
                {
                    var base64Data = match.Groups["data"].Value;
                    var bytes = Convert.FromBase64String(base64Data);
                    await File.WriteAllBytesAsync(tempFilePath, bytes);
                }
                else
                {
                    throw new InvalidOperationException("Invalid base64 image format.");
                }
            }
            else
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tempFilePath, imageBytes);
            }

            return tempFilePath;
        }

        private string AddTag(string? role, string? content)
        {
            if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(content))
            {
                return "";
            }

            return $"\n{role}: {content}\n";
        }

        private async Task<int> GetCurrentTokenCountAsync(IPage page)
        {
            _logger.LogInformation("Waiting for token calculation...");
            var loadingIndicator = page.Locator("span.loading-indicator");
            await loadingIndicator.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30000 });
            _logger.LogInformation("Loading complete, extracting token count value...");

            var tokenCountElement = page.Locator("span.token-count-value").First;
            string fullText = await tokenCountElement.TextContentAsync() ?? "";

            var match = Regex.Match(fullText.Trim(), @"^([\d,]+)");
            if (match.Success)
            {
                _logger.LogInformation("Successfully extracted token-count-value: {TokenValue}", match.Value);
                var numberString = match.Value.Replace(",", "").Trim();
                int value = int.Parse(numberString);
                if (value == 0)
                {
                    _logger.LogInformation("Token count value is 0, retrying after 1 second...");
                    await Task.Delay(1000);
                    return await GetCurrentTokenCountAsync(page);
                }

                return value;
            }
            else
            {
                _logger.LogWarning("Failed to extract token count from text: '{FullText}'", fullText);
                return 0;
            }
        }
        
        private static async Task ClickWithRandomDelayAsync(ILocator locator)
        {
            await Task.Delay(_random.Next(500, 2000));
            await locator.ClickAsync();
        }
    }
}