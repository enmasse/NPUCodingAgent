namespace NPUCodingAgent.Services;

public class FileEditService
{
    private const int MaxFileSizeBytes = 1024 * 1024; // 1 MB

    public bool WriteFile(string relativePath, string content)
    {
        try
        {
            var fullPath = Path.GetFullPath(relativePath);

            if (content.Length > MaxFileSizeBytes)
            {
                Console.WriteLine($"Content too large ({content.Length} bytes). Max: {MaxFileSizeBytes} bytes");
                return false;
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var backupPath = fullPath + ".backup";
            if (File.Exists(fullPath))
            {
                File.Copy(fullPath, backupPath, overwrite: true);
                Console.WriteLine($"Backup created: {backupPath}");
            }

            File.WriteAllText(fullPath, content);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing file: {ex.Message}");
            return false;
        }
    }
}
