using System;
using System.IO;
using System.Text.Json;

namespace Aumentos;

internal sealed class BuildSizePreference(string path, int maximum = BuildSizePreference.Maximum)
{
    internal const int Default = 100;
    internal const int Minimum = 75;
    internal const int Maximum = 150;

    internal int Load()
    {
        try
        {
            int percent = Ratings.Read<int>(path);
            return percent >= Minimum && percent <= maximum ? percent : Default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    internal bool Save(int percent) => StatsCache.Write(path, Math.Clamp(percent, Minimum, maximum));
}
