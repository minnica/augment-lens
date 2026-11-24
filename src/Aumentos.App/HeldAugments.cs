using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace Aumentos;

internal sealed class HeldAugments(Catalog catalog)
{
    private (GameContext Game, Int32Rect Bounds, Calibration Layout, int?[] Cards)? offer;
    private (int Id, long Time)? choice;
    private bool wasPressed;
    internal string[] Keys { get; private set; } = [];
    internal bool HasOffer => offer is not null;

    internal void ObserveOffer(GameContext game, Int32Rect bounds, Calibration layout, int?[] cards)
    {
        if (offer is not { } current || current.Game != game || current.Bounds != bounds ||
            current.Layout != layout || !current.Cards.SequenceEqual(cards))
        {
            choice = null;
            offer = (game, bounds, layout, cards);
        }
    }

    internal void ObservePointer(bool pressed, int x, int y, long now)
    {
        bool clicked = pressed && !wasPressed;
        wasPressed = pressed;
        if (!clicked || offer is not { } view)
            return;

        choice = null;
        int relativeX = x - view.Bounds.X, relativeY = y - view.Bounds.Y;
        for (int index = 0; index < Math.Min(view.Cards.Length, view.Layout.Titles.Length); index++)
        {
            if (view.Cards[index] is not { } id)
                continue;
            var title = view.Layout.Titles[index].Pixels(view.Bounds.Width, view.Bounds.Height);
            // Use the interior of the card so a reroll or the close control cannot count as a choice.
            if (relativeX >= title.X && relativeX < title.X + title.Width &&
                relativeY >= title.Y - view.Bounds.Height * .18 && relativeY < title.Y + view.Bounds.Height * .21)
            {
                choice = (id, now);
                return;
            }
        }
    }

    internal void CloseOffer(long now)
    {
        if (offer is { } current && choice is { } selected && now - selected.Time <= 1500 &&
            catalog.Augments.FirstOrDefault(card => card.Id == selected.Id && card.Mode == current.Game.Mode.ToString()) is { } augment)
        {
            if (augment.Key is "ReplaceAugment" or "CraftingSellAugment" or "TransmuteChaos" or "ARAM_TransmuteChaos" or "UnstableTransmutation")
                Keys = [];
            else if (!Keys.Contains(augment.Id.ToString(CultureInfo.InvariantCulture)))
                Keys = [.. Keys, augment.Id.ToString(CultureInfo.InvariantCulture)];
        }
        CancelOffer();
    }

    internal void CancelOffer()
    {
        offer = null;
        choice = null;
        wasPressed = false;
    }

    internal void Reset()
    {
        CancelOffer();
        Keys = [];
    }
}
