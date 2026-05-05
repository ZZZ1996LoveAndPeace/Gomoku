namespace GomokuApp.Minesweeper;

public sealed class MinesweeperCell
{
    public bool IsMine { get; internal set; }
    public int AdjacentMines { get; internal set; }
    public bool IsRevealed { get; internal set; }
    public bool IsFlagged { get; internal set; }
}
