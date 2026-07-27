using Triker.Shared.Models;

namespace Triker.Web.Services;

// Mapeo de atributos de carta a su representación visual (símbolo/color),
// usado para generar la imagen de la jugada compartida (ver ShareService/js/cardImage.js).
// Mismos valores que Triker.Web/Components/CardView.razor.
public static class CardVisuals
{
    public static string FigureSymbol(Figure figure) => figure switch
    {
        Figure.Circle => "●",
        Figure.Square => "■",
        Figure.Star => "★",
        Figure.Triangle => "▲",
        Figure.Heart => "♥",
        Figure.Clover => "♣",
        Figure.Diamond => "♦",
        Figure.Spade => "♠",
        _ => "?"
    };

    public static string ColorHex(CardColor color) => color switch
    {
        CardColor.Red => "#e53935",
        CardColor.Green => "#43A047",
        CardColor.Blue => "#1E88E5",
        CardColor.Black => "#212121",
        CardColor.Magenta => "#9C27B0",
        CardColor.Cyan => "#00ACC1",
        CardColor.Orange => "#F57C00",
        CardColor.Brown => "#795548",
        _ => "#888888"
    };
}
