namespace GomokuApp.Sudoku;

public sealed record SudokuMoveResult(bool Changed, string? Message = null);

public readonly record struct SudokuPosition(int Row, int Column);
