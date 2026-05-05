namespace GomokuApp.Sudoku;

public sealed record SudokuDifficulty(string Name, int GivenCells, int MaxMistakes)
{
    public static readonly SudokuDifficulty Easy = new("简单", 42, 5);
    public static readonly SudokuDifficulty Normal = new("普通", 36, 4);
    public static readonly SudokuDifficulty Hard = new("困难", 30, 3);
    public static readonly SudokuDifficulty Expert = new("专家", 26, 3);

    public static IReadOnlyList<SudokuDifficulty> All { get; } =
    [
        Easy,
        Normal,
        Hard,
        Expert,
    ];

    public override string ToString() => $"{Name}：{GivenCells} 个已知数";
}
