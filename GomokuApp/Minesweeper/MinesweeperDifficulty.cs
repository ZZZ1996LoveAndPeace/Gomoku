namespace GomokuApp.Minesweeper;

public sealed record MinesweeperDifficulty(string Name, int Rows, int Columns, int Mines)
{
    public static readonly MinesweeperDifficulty Beginner = new("初级", 9, 9, 10);
    public static readonly MinesweeperDifficulty Classic = new("经典", 12, 16, 30);
    public static readonly MinesweeperDifficulty Expert = new("专家", 16, 24, 70);

    public static IReadOnlyList<MinesweeperDifficulty> All { get; } =
    [
        Beginner,
        Classic,
        Expert,
    ];

    public override string ToString() => $"{Name}：{Rows} x {Columns}，{Mines} 雷";
}
