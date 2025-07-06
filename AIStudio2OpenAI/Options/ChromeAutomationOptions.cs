namespace AIStudio2OpenAI.Options;

public class ChromeAutomationOptions
{
    public string? ExecutablePath { get; set; }
    public string? UserDataDir { get; set; }
    public int DebuggingPort { get; set; } = 9222;
    public int MaxAccounts { get; set; } = 1;
}