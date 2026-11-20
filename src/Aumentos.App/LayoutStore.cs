using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aumentos;

internal record SavedLayout(Mode Mode, Calibration Calibration);

internal sealed class LayoutStore
{
    private readonly string path;
    private readonly object gate = new();
    private readonly List<SavedLayout> layouts;
    private readonly Dictionary<(Mode, int, int), Calibration[]> fitted = new();

    internal LayoutStore(string bundledPath, string path)
    {
        this.path = path;
        layouts = Load(bundledPath);
        foreach (var user in Load(path))
            Replace(user);
    }

    internal Calibration[] Find(Mode mode, int width, int height)
    {
        lock (gate)
        {
            var key = (mode, width, height);
            if (fitted.TryGetValue(key, out var cached))
                return cached;
            var result = layouts.Where(item => item.Mode == mode && Near(item.Calibration, width, height))
                .Select(item => item.Calibration).GroupBy(item => item.Titles.Length)
                .Select(group => group.MinBy(item => Math.Abs(item.Width - width) + Math.Abs(item.Height - height))!)
                .Select(item => Resize(item, width, height))
                .OrderByDescending(item => item.Titles.Length).ToArray();
            if (mode == Mode.Arena && result.FirstOrDefault(item => item.Titles.Length == 3) is { } regular)
            {
                var center = regular.Titles[1];
                // Kindred's rewards share the vertical layout with the standard offer, with two centered slots.
                Region RewardTitle(Region outer) => outer with { X = center.X + (outer.X - center.X) * 2 / 3 };
                result = [.. result, regular with { Titles = [RewardTitle(regular.Titles[0]), RewardTitle(regular.Titles[2])] }];
            }
            fitted[key] = result;
            return result;
        }
    }

    private static bool Near(Calibration item, int width, int height) =>
        (Math.Abs(item.Width - width) <= width * 0.03 && Math.Abs(item.Height - height) <= height * 0.03) ||
        (Math.Abs(width / (double)height - 16.0 / 9) < 0.04 && Math.Abs(item.Width / (double)item.Height - 16.0 / 9) < 0.17);

    private static Calibration Resize(Calibration item, int width, int height)
    {
        if (item.Fits(width, height))
            return item;
        var ratio = item.Width * (double)height / (item.Height * (double)width);
        if (Math.Abs(ratio - 1) < 0.03)
            return item with { Width = width, Height = height };
        Region Scale(Region region) => region with { X = 0.5 + (region.X - 0.5) * ratio, Width = region.Width * ratio };
        return item with { Width = width, Height = height, Marker = Scale(item.Marker), Titles = item.Titles.Select(Scale).ToArray() };
    }

    internal void Save(SavedLayout layout)
    {
        lock (gate)
        {
            var updated = Load(path);
            updated.RemoveAll(item => SameLayout(item, layout));
            updated.Add(layout);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(updated, Ratings.JsonOptions));
            File.Move(path + ".tmp", path, true);
            Replace(layout);
            fitted.Clear();
        }
    }

    private void Replace(SavedLayout layout)
    {
        layouts.RemoveAll(item => SameLayout(item, layout));
        layouts.Add(layout);
    }

    private static bool SameLayout(SavedLayout left, SavedLayout right) =>
        left.Mode == right.Mode && left.Calibration.Fits(right.Calibration.Width, right.Calibration.Height) &&
        left.Calibration.Titles.Length == right.Calibration.Titles.Length;

    private static List<SavedLayout> Load(string file)
    {
        if (!File.Exists(file))
            return [];
        return Ratings.Read<List<SavedLayout>>(file).Where(item =>
        {
            var calibration = item.Calibration;
            return calibration.Width > 0 && calibration.Height > 0 && calibration.Signature.Length == 720 &&
                calibration.Titles.Length is 3 or 4 && calibration.Titles.Append(calibration.Marker).All(region =>
                    region.X >= 0 && region.Y >= 0 && region.Width > 0 && region.Height > 0 &&
                    region.X + region.Width <= 1.00001 && region.Y + region.Height <= 1.00001);
        }).ToList();
    }
}
