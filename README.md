# AI Studio to OpenAI API Adapter

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

This project provides a lightweight, self-hosted adapter that exposes a mock OpenAI-compatible API (
`/v1/chat/completions` and `/v1/models`) and translates the requests into automated UI interactions with Google's AI
Studio, powered by Playwright.

## Features

- **OpenAI API Compatibility**: Emulates the `v1/chat/completions` endpoint for both streaming and non-streaming
  responses.
- **Model Support**: Exposes a `/v1/models` endpoint listing available Gemini models (`gemini-2.5-pro`,
  `gemini-2.5-flash`).
- **Cross-Platform**: Works on both Windows and macOS, with automatic detection of Chrome's default installation path.
- **Highly Configurable**:
    - **API Request**: Supports `max_tokens`, `top_p`, and `temperature` on a per-request basis.
    - **Application Settings**: Configure browser automation, model capabilities (e.g., web search, code execution), and
      logging via `appsettings.json` or environment variables.
- **Multi-Account Ready**: Designed to cycle through multiple Google accounts to distribute usage.
- **Structured Logging**: Uses Serilog for structured, configurable, and easily searchable logs.
- **UI Automation**: Leverages the power of Playwright to interact with the AI Studio web interface reliably.

## How It Works

The adapter starts a local Kestrel web server that listens for incoming OpenAI API requests. When a
`/v1/chat/completions` request is received, it performs the following steps:

1. Launches or connects to a Chrome instance with remote debugging enabled.
2. Navigates to Google AI Studio in a new page, cycling through configured user accounts.
3. Selects the requested model (e.g., `gemini-2.5-pro`).
4. Configures advanced model settings based on the API request (`temperature`, `top_p`, etc.) and application
   configuration (`EnableWebSearch`, etc.).
5. Pastes the user's prompt into the input area.
6. Clicks the "Run" button and waits for the model to generate a response.
7. Copies the markdown-formatted response from the UI.
8. Transforms the response into the OpenAI ChatCompletion or ChatCompletionStream format and sends it back to the
   client.

## Prerequisites

- .NET 8.0 SDK
- Google Chrome browser

## Quick Start

### 1. Configure the Application

Edit the `AIStudio2OpenAI/appsettings.json` file to match your setup.

```json
{
  "ChromeAutomation": {
    "ExecutablePath": null,
    // your Chrome.exe executable path
    "UserDataDir": null,
    // a empty folder to store chrome data
    "DebuggingPort": 9222,
    "MaxAccounts": 1
    // maximum number of Google accounts you logged in
  },
  "Gemini": {
    "SetMaxThinkingTokens": true,
    "EnableCodeExecution": false,
    "EnableWebSearch": false
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:3060"
      }
    }
  }
}
```

### 2. Launch Chrome with Remote Debugging

You must launch a dedicated Chrome instance for the adapter to connect to. Close all other Chrome instances first.

**On Windows (Command Prompt):**

```cmd
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="C:\Users\%USERNAME%\Documents\ChromeAgent"
```

**On macOS (Terminal):**

```sh
/Applications/Google\ Chrome.app/Contents/MacOS/Google\ Chrome --remote-debugging-port=9222 --user-data-dir="$HOME/Documents/ChromeAgent"
```

In the newly opened Chrome window, log in to your Google Account(s). If using multiple accounts, log them all in.

### 3. Run the Adapter

Once Chrome is running and you are logged in, open a separate terminal and run the application:

```sh
cd AIStudio2OpenAI
dotnet run
```

The application will prompt you to press **[Enter]** to confirm Chrome is ready.

### 4. Use the API

You can now send requests to `http://localhost:3060` using any OpenAI-compatible client.

**Example using `curl`:**

```sh
curl http://localhost:3060/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemini-1.5-pro",
    "messages": [
      {
        "role": "user",
        "content": "Write a short story about a robot who learns to paint."
      }
    ],
    "temperature": 0.7,
    "max_tokens": 256
  }'
```

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
