namespace NPUCodingAgent.Services;

public class WorkspaceService
{
    private readonly string _rootPath;
    private const int MaxFileSizeBytes = 1024 * 1024; // 1 MB
    private static readonly string[] AllowedExtensions = 
    {
        ".cs", ".csproj", ".sln", ".slnx", ".json", ".xml", ".txt", ".md",
        ".js", ".ts", ".html", ".css", ".py", ".java", ".cpp", ".h"
    };

    public WorkspaceService(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
    }

    public List<string> ListFiles()
    {
        var files = new List<string>();

        try
        {
            EnumerateFiles(_rootPath, files);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing files: {ex.Message}");
        }

        return files;
    }

    public string? ReadFile(string relativePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

            if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Access denied: path is outside workspace");
                return null;
            }

            if (!File.Exists(fullPath))
            {
                Console.WriteLine("File not found");
                return null;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                Console.WriteLine($"File too large ({fileInfo.Length} bytes). Max: {MaxFileSizeBytes} bytes");
                return null;
            }

            return File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading file: {ex.Message}");
            return null;
        }
    }

    private void EnumerateFiles(string directory, List<string> files)
    {
        if (ShouldSkipDirectory(directory))
            return;

        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    var relativePath = Path.GetRelativePath(_rootPath, file);
                    files.Add(relativePath);
                }
            }

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                EnumerateFiles(subDir, files);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private bool ShouldSkipDirectory(string directory)
    {
        var dirName = Path.GetFileName(directory);
        return dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals("packages", StringComparison.OrdinalIgnoreCase);
    }
}
