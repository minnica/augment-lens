using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aumentos;

internal sealed class TierHexagon : Border
{
    private static readonly Dictionary<string, Lazy<ImageBrush>> Art = new(StringComparer.Ordinal)
    {
        ["S+"] = Artwork("s-plus"), ["S"] = Artwork("s"), ["A"] = Artwork("a"), ["B"] = Artwork("b"),
        ["C"] = Artwork("c"), ["D"] = Artwork("d"), ["E"] = Artwork("e")
    };

    internal string? Tier { get; }

    private TierHexagon(string? tier, double width, double height)
    {
        Tier = tier;
        Width = width;
        Height = height;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        if (tier is not null && Art.TryGetValue(tier, out var artwork))
            Background = artwork.Value;
        else
        {
            Child = new TextBlock
            {
                Text = tier ?? "—", Foreground = OverlayPanelStyle.Muted, FontFamily = OverlayPanelStyle.Body,
                FontSize = Math.Min(width * .55, height * .49),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        }

        AutomationProperties.SetName(this, tier is null ? "Sin tier" : $"Tier {tier}");
    }

    internal static TierHexagon Create(string? tier, double width, double height) => new(tier, width, height);

    protected override AutomationPeer OnCreateAutomationPeer() => new BadgePeer(this);

    private static Lazy<ImageBrush> Artwork(string name) => new(() => Read(name));

    private static ImageBrush Read(string name)
    {
        using var stream = typeof(TierHexagon).Assembly.GetManifestResourceStream($"TierIcons.{name}.png")
            ?? throw new InvalidDataException($"Falta la insignia {name}.");
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Uniform };
        brush.Freeze();
        return brush;
    }

    private sealed class BadgePeer(TierHexagon owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(TierHexagon);
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
