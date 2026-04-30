namespace GomokuApp.Models;

public enum Stone
{
    None = 0,
    Black = 1,
    White = 2,
}

public enum GameMode
{
    Playing,
    Setup,
}

public enum AiDifficulty
{
    Easy,
    Normal,
    Hard,
    Master,
}

public static class StoneExtensions
{
    public static Stone Opponent(this Stone stone) => stone switch
    {
        Stone.Black => Stone.White,
        Stone.White => Stone.Black,
        _ => Stone.None,
    };

    public static string ToDisplayName(this Stone stone) => stone switch
    {
        Stone.Black => "黑方",
        Stone.White => "白方",
        _ => "无",
    };
}
