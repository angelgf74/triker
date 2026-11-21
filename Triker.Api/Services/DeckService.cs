using Triker.Shared.Models;

namespace Triker.Api.Services;

public class DeckService
{
    public List<Card> GenerateDeck(int n)
    {
        var letters = Enum.GetValues<Letter>().Take(n).ToArray();
        var figures = Enum.GetValues<Figure>().Take(n).ToArray();
        var colors = Enum.GetValues<CardColor>().Take(n).ToArray();

        var deck = new List<Card>();
        foreach (var letter in letters)
            foreach (var figure in figures)
                foreach (var color in colors)
                    deck.Add(new Card(Guid.NewGuid(), letter, figure, color));

        return Shuffle(deck);
    }

    public List<Card> Shuffle(List<Card> cards)
        => [.. cards.OrderBy(_ => Random.Shared.Next())];

    public (List<Card> drawn, List<Card> remaining) Draw(List<Card> deck, List<Card> discardPile, int count)
    {
        if (deck.Count < count)
        {
            var reshuffled = Shuffle(discardPile);
            deck = [.. deck, .. reshuffled];
            // Clear y no "discardPile = []": lo segundo solo cambia la variable
            // de aqui y la pila de la partida seguia llena, con lo que al
            // siguiente rebarajado esas cartas entrarian otra vez. Hoy no se
            // llega: un mazo de 512 da para la ronda mas larga (160 cartas).
            discardPile.Clear();
        }

        var drawn = deck.Take(count).ToList();
        var remaining = deck.Skip(count).ToList();
        return (drawn, remaining);
    }
}
