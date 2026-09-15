using System.Text.Json;

namespace WebSocketNotifications.Performance;

internal static class Program
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = PerformanceOptions.Parse(args);
            var result = await PerformanceRunner.RunAsync(options, CancellationToken.None);
            var json = JsonSerializer.Serialize(result, ResultJsonOptions);
            Console.WriteLine(json);

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                var outputPath = Path.GetFullPath(options.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, json).ConfigureAwait(false);
            }

            return result.Passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }
}
