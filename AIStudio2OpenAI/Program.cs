using AIStudio2OpenAI.Options;
using AIStudio2OpenAI.Services;
using Microsoft.Extensions.Options;
using Serilog;
using System.Runtime.InteropServices;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            builder.Configuration.AddEnvironmentVariables();

            ConfigureServices(builder.Services, builder.Configuration);

            var app = builder.Build();

            ConfigureMiddleware(app);

            await StartApplicationAsync(app);

            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.Configure<ChromeAutomationOptions>(configuration.GetSection("ChromeAutomation"));
        services.Configure<GeminiOptions>(configuration.GetSection("Gemini"));

        // Use PostConfigure to set default values AFTER user config is loaded.
        // This ensures user-provided values in appsettings.json are NOT overwritten.
        services.PostConfigure<ChromeAutomationOptions>(options =>
        {
            if (string.IsNullOrEmpty(options.ExecutablePath))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    options.ExecutablePath = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    options.ExecutablePath = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
                }
            }

            if (string.IsNullOrEmpty(options.UserDataDir))
            {
                options.UserDataDir = Path.Combine(Path.GetTempPath(), "ChromeAgent");
            }
        });

        services.AddSingleton<IGeminiService, GeminiService>();
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();
        app.UseSerilogRequestLogging();
    }

    private static async Task StartApplicationAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var chromeOptions = app.Services.GetRequiredService<IOptions<ChromeAutomationOptions>>().Value;

        // Validate that the final executable path exists before trying to use it.
        if (!File.Exists(chromeOptions.ExecutablePath))
        {
            logger.LogCritical(
                "Chrome executable not found at the configured or detected path: {Path}. Please verify the 'ChromeAutomation:ExecutablePath' in appsettings.json or ensure Chrome is installed in the default location.",
                chromeOptions.ExecutablePath);
            throw new FileNotFoundException("Chrome executable not found.", chromeOptions.ExecutablePath);
        }

        DisplayChromeLaunchPrompt(logger, chromeOptions);
        Console.ReadLine();

        logger.LogInformation("Initializing Gemini Service...");
        var geminiService = app.Services.GetRequiredService<IGeminiService>();
        await ((GeminiService)geminiService).InitializeAsync();
        logger.LogInformation("Gemini Service Initialized successfully.");
    }

    private static void DisplayChromeLaunchPrompt(ILogger<Program> logger, ChromeAutomationOptions options)
    {
        var commandString = GenerateCommandString(options);

        logger.LogInformation("--- Launch Chrome for Automation ---");
        logger.LogInformation("Please ensure all other Chrome instances are closed.");
        logger.LogInformation("Open a command prompt or terminal and run the following command:");
        logger.LogInformation("Using Chrome executable path: {Path}", options.ExecutablePath);
        logger.LogInformation("Using user data directory: {Path}", options.UserDataDir);
        logger.LogInformation("Using debugging port: {Port}", options.DebuggingPort);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(commandString);
        Console.ResetColor();

        logger.LogInformation("If this is the first time, log in to your Google account in the new Chrome window.");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\nPress [Enter] to continue once Chrome is running...");
        Console.ResetColor();
    }

    private static string GenerateCommandString(ChromeAutomationOptions options)
    {
        var expandedUserDataDir = Environment.ExpandEnvironmentVariables(options.UserDataDir!);
        Directory.CreateDirectory(expandedUserDataDir);
        return
            $"\"{options.ExecutablePath}\" --remote-debugging-port={options.DebuggingPort} --user-data-dir=\"{expandedUserDataDir}\"";
    }
}