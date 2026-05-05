namespace GomokuApp.Minesweeper;

public sealed record MinesweeperRevealResult(
    bool Changed,
    bool GeneratedBoard,
    bool HitMine,
    IReadOnlyList<MinesweeperPosition> RevealedCells,
    string? Message = null);

public readonly record struct MinesweeperPosition(int Row, int Column);
