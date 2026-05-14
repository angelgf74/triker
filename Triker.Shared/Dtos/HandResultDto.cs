namespace Triker.Shared.Dtos;

public class CardGroupDto
{
    public List<CardDto> Cards { get; set; } = [];
    public int MatchingParams { get; set; }
    public int Score { get; set; }
    public string Name { get; set; } = "";
}

public class HandResultDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public List<CardDto> Hand { get; set; } = [];
    public List<CardGroupDto> Groups { get; set; } = [];
    public int TotalScore { get; set; }
    public string HandName { get; set; } = "";
    public int Rank { get; set; }
}
