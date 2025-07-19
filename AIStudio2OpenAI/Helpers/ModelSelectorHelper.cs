using System.Globalization;
using AIStudio2OpenAI.Models;
using AIStudio2OpenAI.Options;
using Microsoft.Playwright;

namespace AIStudio2OpenAI.Helpers
{
    public static class ModelSelectorHelper
    {
        private static readonly Random _random = new();
        /// <summary>
        /// Selects the AI model and configures advanced settings on the page.
        /// </summary>
        public static async Task SelectModelAsync(IPage page, ChatCompletionRequest? request, GeminiOptions options, ILogger logger)
        {
            logger.LogInformation("--- Starting model selection and configuration for {ModelId} ---", request?.Model);

            // Step 1: Select the model
            await SelectModelDropdownAsync(page, request?.Model!, logger);

            // Step 2: Configure advanced settings
            await ConfigureAdvancedSettingsAsync(page, request, options, logger);
            
            logger.LogInformation("--- Successfully selected and configured model: {ModelId} ---", request?.Model);
        }

        private static async Task SelectModelDropdownAsync(IPage page, string modelId, ILogger logger)
        {
            logger.LogInformation("Step 1.1: Clicking model selector dropdown...");
            var modelSelectorDropdown = page.Locator("[data-test-ms-model-selector]").Last;
            await ClickWithRandomDelayAsync(modelSelectorDropdown);

            logger.LogInformation("Step 1.2: Selecting model '{ModelId}' from list...", modelId);
            var optionLocator = page.GetByRole(AriaRole.Option).Filter(new LocatorFilterOptions { HasText = modelId }).First;
            await ClickWithRandomDelayAsync(optionLocator);
        }

        private static async Task ConfigureAdvancedSettingsAsync(IPage page, ChatCompletionRequest? request, GeminiOptions options, ILogger logger)
        {
            logger.LogInformation("Step 2: Configuring advanced settings...");

            // Set Max Output Tokens if provided
            if (request is { MaxTokens: not null })
            {
                logger.LogInformation("Setting 'Maximum output tokens' to {MaxTokens}", request.MaxTokens.Value);
                var input = page.Locator("input[aria-label='Maximum output tokens']");
                await input.FillAsync(request.MaxTokens.Value.ToString());
            }

            // Set Top-P if provided
            if (request is { TopP: not null })
            {
                logger.LogInformation("Setting 'Top P' to {TopP}", request.TopP.Value);
                var input = page.Locator("input[aria-label^='Top P']");
                await input.FillAsync(request.TopP.Value.ToString(CultureInfo.InvariantCulture));
            }
            
            // Set Temperature if provided
            if (request is { Temperature: not null })
            {
                logger.LogInformation("Setting 'Temperature' to {Temperature}", request.Temperature.Value);
                var tempSliderContainer = page.Locator("div[data-test-id='temperatureSliderContainer']");
                var numberInput = tempSliderContainer.Locator("input[type='number']");
                if(await numberInput.IsVisibleAsync())
                {
                    await numberInput.FillAsync(request.Temperature.Value.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    logger.LogWarning("Temperature number input not visible. Skipping setting temperature.");
                }
            }

            // Configure toggleable options from GeminiOptions
            if (options.SetMaxThinkingTokens)
            {
                await SetMaxThinkingBudgetAsync(page, logger);
            }

            await ToggleSwitchByLabelAsync(page, "Code execution", options.EnableCodeExecution, logger);
            await ToggleSwitchByLabelAsync(page, "Grounding with Google Search", options.EnableWebSearch, logger);
        }

        private static async Task SetMaxThinkingBudgetAsync(IPage page, ILogger logger)
        {
            logger.LogInformation("Setting max thinking budget...");
            await ToggleSwitchByLabelAsync(page, "Toggle thinking budget between auto and manual", true, logger);

            var settingWrapper = page.Locator("[data-test-id='user-setting-budget-animation-wrapper']");
            var numberInput = settingWrapper.Locator("input[type='number']");
            await numberInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });

            string? maxValueString = await numberInput.GetAttributeAsync("max");
            if (maxValueString != null)
            {
                logger.LogInformation("Max thinking budget value found: {MaxValue}. Setting it.", maxValueString);
                await numberInput.FillAsync(maxValueString);
            }
            else
            {
                logger.LogWarning("Could not find 'max' attribute for thinking budget input.");
            }
        }
        
        private static async Task ToggleSwitchByLabelAsync(IPage page, string label, bool shouldBeOn, ILogger logger)
        {
            logger.LogInformation("Operating switch '{Label}': Desired state is {ShouldBeOn}", label, shouldBeOn);
            var switchButton = page.GetByLabel(label);
            await switchButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });

            string? currentState = await switchButton.GetAttributeAsync("aria-checked");
            bool isCurrentlyOn = currentState == "true";

            if (isCurrentlyOn != shouldBeOn)
            {
                logger.LogInformation("Switch '{Label}' is currently {CurrentState}. Clicking to change.", label, isCurrentlyOn ? "ON" : "OFF");
                await ClickWithRandomDelayAsync(switchButton);
                await Assertions.Expect(switchButton).ToHaveAttributeAsync("aria-checked", shouldBeOn.ToString().ToLower());
                logger.LogInformation("Switch '{Label}' successfully set to {NewState}", label, shouldBeOn ? "ON" : "OFF");
            }
            else
            {
                logger.LogInformation("Switch '{Label}' is already in the desired state ({CurrentState}). No action needed.", label, isCurrentlyOn ? "ON" : "OFF");
            }
        }
        
        private static async Task ClickWithRandomDelayAsync(ILocator locator)
        {
            await Task.Delay(_random.Next(1000, 4000));
            await locator.ClickAsync();
        }
    }
}