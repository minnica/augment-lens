using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaGuestRating(Tier Tier, string Reason, int Score);

internal static class ArenaGuestAdvice
{
    private static readonly StringComparer Keys = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> LateScalers = new(
        ["AurelionSol", "Belveth", "ChoGath", "Kayle", "Kindred", "Nasus", "Senna", "Smolder", "Swain", "Veigar"], Keys);
    private static readonly HashSet<string> HealthScalers = new(
        ["ChoGath", "DrMundo", "Ornn", "Sejuani", "Sett", "Shen", "Sion", "Skarner", "TahmKench", "Volibear", "Zac"], Keys);
    private static readonly HashSet<string> AugmentSynergies = new(
        ["Azir", "Belveth", "Brand", "Cassiopeia", "Jax", "Kaisa", "Katarina", "Kayle", "KogMaw", "MasterYi",
            "Samira", "Singed", "Swain", "Vayne", "Yasuo", "Yone", "Zeri"], Keys);

    internal static ArenaGuestRating?[] Evaluate(Ratings ratings, string[] guestKeys, LiveMatch match,
        int? teamHealth = null, int? round = null)
    {
        if (match.Game.Mode != Mode.Arena)
            return new ArenaGuestRating?[guestKeys.Length];

        var champions = ratings.Catalog.Champions.ToDictionary(champion => champion.Key, Keys);
        if (!champions.TryGetValue(match.Game.Champion, out var own))
            return new ArenaGuestRating?[guestKeys.Length];

        var arena = match.Arena;
        var allies = (arena?.Allies ?? [])
            .Where(ally => !Keys.Equals(ally.Champion, own.Key) && champions.ContainsKey(ally.Champion))
            .DistinctBy(ally => ally.Champion, Keys).ToArray();
        var team = allies.Select(ally => champions[ally.Champion]).Prepend(own).ToArray();
        var opponents = arena?.LobbyOpponents is { Length: > 0 } lobby ? lobby : arena?.Opponents ?? [];
        var rivals = opponents.Select(opponent => champions.GetValueOrDefault(opponent.Champion)).OfType<Champion>().ToArray();

        bool ownDamage = CanDealDamage(own);
        int damage = team.Count(CanDealDamage);
        bool protection = team.Any(champion => Trait(champion, "ally"));
        bool frontline = team.Any(champion => Trait(champion, "durability"));
        bool control = team.Any(champion => Trait(champion, "control"));
        bool canFinish = damage >= 2 && control;
        bool burst = team.Any(champion => Trait(champion, "burst"));
        bool earlyPressure = canFinish && burst;
        bool closeRange = !CanFightAtRange(own) && team.Count(CanFightAtRange) * 2 < team.Length;
        bool rangedTeam = CanFightAtRange(own) && team.Count(CanFightAtRange) * 2 >= team.Length;
        bool mobile = Trait(own, "mobility") && team.Count(champion => Trait(champion, "mobility")) * 2 >= team.Length;
        bool sustained = team.Count(champion => Trait(champion, "sustain")) * 2 >= team.Length;
        bool canDuel = CanDuel(own) && team.Count(CanDuel) * 2 >= team.Length;
        bool fragile = !frontline && !protection;
        bool wantsScaling = LateScalers.Contains(own.Key) || team.Count(champion => LateScalers.Contains(champion.Key)) >= 2;
        bool usesAugments = AugmentSynergies.Contains(own.Key) ||
            team.Count(champion => AugmentSynergies.Contains(champion.Key)) >= 2;
        bool healthBuild = HealthScalers.Contains(own.Key) ||
            team.Count(champion => HealthScalers.Contains(champion.Key)) >= 2 ||
            arena is { InventoryKnown: true } && arena.Items.Any(Heartsteel) ||
            allies.Count(ally => ally.Items.Any(Heartsteel)) >= 2;
        bool underPressure = teamHealth is > 0 and <= 30;
        var equipment = (ratings.Catalog.ArenaLegendaries ?? []).Concat(ratings.Catalog.ArenaPrismatics ?? [])
            .Concat(ratings.Catalog.ArenaEquipment ?? []).DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        bool buildComplete = arena is { InventoryKnown: true } && Equipment(arena.Items).Count() >= 6;
        int? itemEdge = EquippedItemEdge(arena, allies, items => Equipment(items).Count(item => item.Categories?.Contains("Boots") != true));
        bool gearBehind = itemEdge is <= -1;
        bool canSnowball = !underPressure && !gearBehind && (earlyPressure || canFinish && itemEdge is >= 1);
        bool needsItems = gearBehind || (round is >= 8 && !buildComplete && arena is { InventoryKnown: true });
        bool rivalFrontline = Majority(rivals, champion => Trait(champion, "durability"));
        bool rivalSustain = Majority(rivals, champion => Trait(champion, "sustain") || Trait(champion, "ally"));
        bool rivalRanged = Majority(rivals, CanFightAtRange);
        bool goodRevives = protection || frontline && rangedTeam;
        return guestKeys.Select(ScoreFor).ToArray();

        // Effects match the 16.23 client tooltips (cherry_goh_tooltiplong_*). Scores estimate fit for our team.
        // The vote changes the whole lobby. Generic effects stay neutral unless the evidence suggests an edge.
        ArenaGuestRating? ScoreFor(string guestKey) => guestKey.ToLowerInvariant() switch
        {
            "alistar" => underPressure ? Rate(6, "Otra ronda antes de la eliminación") :
                wantsScaling ? Rate(5, "Otra ronda para escalar") : Rate(3, "Todos sobreviven a una eliminación"),
            "ambessa" => fragile || underPressure ? Rate(2, "El sacrificio te deja expuesto") :
                sustained && usesAugments ? Rate(6, "Aguante para canjear vida por poder") : Rate(4, "Sacrifica vida por un aumento"),
            "annie" => mobile ? Rate(5, "Movilidad para esquivar el muro") : Rate(3, "El muro cambia cómo rotar"),
            "atakhan" => canSnowball ? Rate(7, "Derribos para encadenar recompensas") :
                canFinish ? Rate(5, "Control y daño para sumar derribos") : Rate(2, "Cuesta cobrar sin presión ofensiva"),
            "briar" => canSnowball && !wantsScaling ? Rate(6, "Presión para forzar eliminaciones") :
                underPressure && canFinish ? Rate(4, "Ganar en riesgo recupera vida") : Rate(2, "Más daño de ronda para todos"),
            "cassiopeia" => own.Key == "Cassiopeia" ? Rate(7, "Ronda legendaria sin perder botas") :
                rangedTeam && !mobile ? Rate(2, "Sin botas cuesta mantener distancia") : Rate(4, "Una ronda de legendarios sin botas"),
            "darius" => teamHealth is > 0 and < 50 ? Rate(7, "Tu equipo sube a 50 de vida") :
                teamHealth is > 75 && wantsScaling ? Rate(1, "Acorta la partida de tu equipo") :
                canSnowball ? Rate(5, "Los 50 de vida favorecen tu presión") : Rate(3, "Deja a todos con 50 de vida"),
            "draven" => canSnowball ? Rate(6, "Tus rachas pueden dar más yunques") : Rate(3, "Los yunques dependen de tu racha"),
            "elise" => canFinish && rivalSustain ? Rate(7, "Derribos para dificultar reanimaciones") :
                earlyPressure ? Rate(6, "Enfoca al rival y protege la telaraña") : Rate(3, "Debes defender las telarañas"),
            "evelynn" => teamHealth is > 0 and < 75 && usesAugments ? Rate(8, "Recuperas vida y ganas aumentos") :
                teamHealth is > 0 and < 75 ? Rate(7, "Tu equipo sube a 75 de vida") :
                teamHealth is > 75 && wantsScaling && !usesAugments ? Rate(2, "Recorta tiempo para escalar") :
                usesAugments ? Rate(canSnowball ? 7 : 6, "Más aumentos para tus sinergias") :
                canSnowball ? Rate(5, "Aumentos extra para presionar temprano") : Rate(4, "Aumento extra; todos a 75 de vida"),
            "fiddlesticks" => usesAugments ? Rate(5, "Posibles mejoras; vigila las maldiciones") :
                Rate(3, "Aumentos con premios o penalizaciones"),
            "garen" => teamHealth is > 0 and <= 30 ? Rate(8, "20 de vida para seguir en juego") :
                teamHealth is > 0 and <= 50 ? Rate(6, "20 de vida extra para tu equipo") :
                teamHealth is > 50 ? Rate(2, "Solo cura a los equipos con 50 o menos") : Rate(4, "Cura a los equipos con 50 o menos"),
            "hwei" => wantsScaling || usesAugments ? Rate(5, "Estadísticas al armar tus sinergias") : Rate(4, "Estadísticas al elegir aumentos marcados"),
            "ivern" => own.Key is "Rengar" or "Ivern" || team.Any(champion => champion.Key == "Rengar") ?
                Rate(6, "La maleza favorece a tu equipo") : Rate(3, "Maleza al perder una ronda"),
            "jarvaniv" => own.Key == "JarvanIV" ? Rate(6, "Jarvan aprovecha mejor la corona") :
                underPressure && frontline ? Rate(6, "Puedes retener la corona con aguante") : Rate(4, "El equipo con menos vida recibe la corona"),
            "katarina" => usesAugments && wantsScaling ? Rate(3, "Puedes perder piezas de tu sinergia") :
                needsItems ? Rate(5, "Aumentos de mayor rareza para remontar") : Rate(4, "Canjea un aumento por mayor rareza"),
            "kayle" => wantsScaling && !underPressure ? Rate(7, "Protección durante tu escalado") :
                canSnowball ? Rate(6, "Tu presión ayuda a conservar el escudo") : Rate(5, "El escudo evita la pérdida de una ronda"),
            "kindred" => goodRevives && !fragile ? Rate(7, "Tu equipo puede proteger otra reanimación") :
                canFinish ? Rate(6, "La ejecución ayuda a cerrar combates") : Rate(4, "Elige más reanimación o ejecución"),
            "kled" => Rate(3, "Intervención tras terminar tu combate"),
            "leblanc" => burst && mobile ? Rate(6, "Movilidad y ráfaga desde invisibilidad") :
                rangedTeam ? Rate(2, "La invisibilidad corta tu alcance efectivo") : Rate(4, "Todos se vuelven invisibles por momentos"),
            "locke" => canSnowball ? Rate(7, "Con rachas puedes consolidar aumentos") :
                underPressure || gearBehind ? Rate(2, "Dos derrotas rompen el aumento") : Rate(4, "Mayor rareza con riesgo de romperse"),
            "lux" => rangedTeam && mobile ? Rate(7, "Puedes salir y renovar el escudo") :
                rangedTeam ? Rate(6, "Distancia para renovar escudos") : Rate(4, "Escudos al salir del combate"),
            "mel" => rangedTeam && mobile ? Rate(6, "Portales para abrir distancia") :
                closeRange && !mobile ? Rate(2, "Los portales facilitan que escapen") : Rate(4, "Más portales para reposicionarse"),
            "mordekaiser" => canDuel ? Rate(6, "Tu equipo aguanta un duelo aislado") :
                protection ? Rate(2, "Aislarte rompe la protección del equipo") : Rate(4, "Un aliado aleatorio entra al duelo"),
            "morgana" => closeRange && control ? Rate(5, "Control cerca de las zonas de daño") :
                Rate(3, "Deja una zona de daño al caer"),
            "nilah" => canSnowball && !buildComplete ? Rate(6, "Yunques más baratos y rerolls por racha") :
                buildComplete ? Rate(2, "Poco valor con seis objetos") : Rate(4, "Yunques legendarios más baratos"),
            "nocturne" => closeRange && control ? Rate(6, "La oscuridad favorece tus entradas") :
                rangedTeam ? Rate(2, "Pierdes visión y espacio para el daño") : Rate(4, "Oscuridad y paso a través de muros"),
            "ornn" => buildComplete ? Rate(3, "Tu build ya ocupa sus espacios") :
                needsItems ? Rate(5, "Puedes buscar un prismático concreto") : Rate(4, "Tienda prismática rotativa"),
            "poppy" => needsItems ? Rate(6, "Prismático inmediato para cerrar la brecha") : Rate(4, "Prismático ahora; luego no se compra"),
            "rell" => buildComplete ? Rate(2, "Poco valor con seis objetos") :
                needsItems ? Rate(6, "Abarata la compra de prismáticos") : Rate(4, "Descuento de yunques prismáticos"),
            "riven" => canSnowball ? Rate(5, "Conservas el ritmo de tu equipo") : Rate(3, "Las reglas de la partida siguen igual"),
            "sahnuzal" => goodRevives ? Rate(2, "Pierdes la opción de reanimar protegido") :
                earlyPressure ? Rate(6, "El daño rápido asegura eliminaciones") : Rate(3, "La partida pierde las reanimaciones"),
            "samira" => canSnowball ? Rate(7, "Tu ráfaga compite por ganar jugos") :
                burst && ownDamage ? Rate(5, "La ráfaga ayuda a cobrar jugos") : Rate(3, "Recompensa al primero que baja vida"),
            "senna" => canDuel && mobile ? Rate(6, "Movilidad para separar y rematar") :
                protection ? Rate(2, "Separarse debilita tu protección") : Rate(4, "Portales al Reino de la Muerte"),
            "shaco" => gearBehind && !usesAugments ? Rate(5, "La rareza aleatoria abre una remontada") :
                usesAugments ? Rate(2, "La rareza al azar altera tus sinergias") : Rate(3, "Rareza de aumentos aleatoria"),
            "sion" => closeRange && sustained && control ? Rate(7, "Aguante y control en poco espacio") :
                rangedTeam && !mobile ? Rate(2, "El fuego reduce el espacio para kitear") : Rate(4, "El círculo de fuego empieza de inmediato"),
            "swain" => underPressure ? Rate(2, "Tienes que sobrevivir a medio lobby") :
                canSnowball || wantsScaling ? Rate(6, "Yunque prismático si tu equipo sobrevive") : Rate(4, "Yunque al quedar la mitad de equipos"),
            "tahmkench" => healthBuild ? Rate(sustained ? 3 : 2, "Tu build depende de la vida máxima") :
                needsItems && !fragile ? Rate(7, "Tratos de oro para cerrar la build") :
                canSnowball && sustained ? Rate(6, "Tu aguante da margen para negociar") :
                fragile ? Rate(3, "El costo de vida expone a tu equipo") : Rate(4, "Los tratos cuestan vida máxima"),
            "talon" => control && burst ? Rate(6, "Control y ráfaga para cazar al marcado") :
                fragile ? Rate(2, "Tu marcado queda expuesto") : Rate(4, "Más daño y recompensa por el marcado"),
            "taric" => control && frontline ? Rate(6, "Control para disputar la invulnerabilidad") :
                rivalRanged && !mobile ? Rate(3, "Te cuesta llegar a la invulnerabilidad") : Rate(4, "Zonas breves de invulnerabilidad"),
            "thresh" => goodRevives ? Rate(6, "La linterna facilita cubrir reanimaciones") : Rate(4, "Escudos y movilidad al caer un aliado"),
            "trundle" => buildComplete ? Rate(4, "Ya tienes tu build antes del bloqueo") :
                usesAugments || rivalFrontline ? Rate(2, "Los yunques limitan compras específicas") : Rate(3, "Legendarios por yunque y más rerolls"),
            "vayne" => rivalSustain || rivalFrontline ? Rate(7, "Herramientas contra curación y tanques") :
                canFinish ? Rate(5, "Herramientas para enfocar al rival") : Rate(4, "Elige una herramienta contra rivales"),
            "vladimir" => Rate(3, "Un aumento compartido con el lobby"),
            "xinzhao" => teamHealth is >= 60 and <= 70 ? Rate(6, "Cerca del umbral del yunque") :
                earlyPressure ? Rate(3, "Los yunques premian perder vida") : Rate(4, "Yunques al bajar tu vida de equipo"),
            "yone" => canSnowball ? Rate(7, "Control y daño para cazar equipos") :
                usesAugments && !underPressure ? Rate(6, "Mejoras al eliminarse cazadores") :
                needsItems && canFinish ? Rate(5, "Oro al vencer a equipos demonio") : Rate(4, "Oro o mejoras según tu bando"),
            _ => null
        };

        bool Trait(Champion champion, string trait) => ratings.HasTrait(champion.Key, trait);
        static bool CanDealDamage(Champion champion) => champion.Roles.FirstOrDefault() is "Marksman" or "Assassin" or "Mage" or "Fighter";
        bool CanFightAtRange(Champion champion) => champion.Roles.FirstOrDefault() == "Marksman" || Trait(champion, "range");
        bool CanDuel(Champion champion) => CanDealDamage(champion) && !Trait(champion, "ally") &&
            (Trait(champion, "sustain") || Trait(champion, "burst") && Trait(champion, "mobility"));
        static bool Heartsteel(int id) => id is 3084 or 223084;
        IEnumerable<ArenaItem> Equipment(int[] items) => items.Select(id => equipment.GetValueOrDefault(id) ??
            (id is > 0 and < 10000 ? equipment.GetValueOrDefault(id + 220000) : null))
            .OfType<ArenaItem>().DistinctBy(item => item.Id);
    }

    private static ArenaGuestRating Rate(int score, string reason) =>
        new(score switch { >= 7 => Tier.S, >= 5 => Tier.A, >= 3 => Tier.B, _ => Tier.C }, reason, score);

    private static bool Majority(Champion[] champions, Func<Champion, bool> predicate) =>
        champions.Length >= 2 && champions.Count(predicate) * 2 > champions.Length;

    private static int? EquippedItemEdge(ArenaMatch? match, ArenaParticipant[] allies, Func<int[], int> itemCount)
    {
        if (match is not { InventoryKnown: true, Opponents.Length: >= 2 } ||
            allies.Length == 0 || allies.Any(ally => ally.Items.Length == 0))
            return null;

        // The event API may expose several partial rival teams. Treat these as observed opponents, not the next pairing.
        var rivals = match.Opponents.Where(rival => rival.Items.Length > 0).ToArray();
        if (rivals.Length < allies.Length + 1)
            return null;

        double own = allies.Select(ally => itemCount(ally.Items)).Prepend(itemCount(match.Items)).Average();
        double enemy = rivals.Average(rival => itemCount(rival.Items));
        return (int)Math.Truncate(own - enemy);
    }
}
