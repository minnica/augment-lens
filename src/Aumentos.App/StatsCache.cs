using System;
using System.IO;
using System.Text.Json;

namespace Aumentos;

internal static class StatsCache
{
    internal static bool Write<T>(string path, T data)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
