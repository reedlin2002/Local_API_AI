namespace LocalAIAgentAPI.Infrastructure;

internal static class ContentPath
{
    /// <summary>
    /// Resolves a configured relative path against the content root first (dotnet run, tests),
    /// then the app's binary folder (published exe started from another working directory).
    /// </summary>
    public static string Resolve(IHostEnvironment environment, string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        string[] candidates =
        [
            Path.Combine(environment.ContentRootPath, path),
            Path.Combine(AppContext.BaseDirectory, path),
        ];

        return candidates.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p))
            ?? throw new FileNotFoundException(
                $"Could not find '{path}'. Looked in: {string.Join(", ", candidates)}");
    }
}
