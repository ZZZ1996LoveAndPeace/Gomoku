using GomokuApp.Models;

namespace GomokuApp.Core;

public readonly record struct BoardOutcome(Stone Winner, bool IsDraw);

public static class RulesEvaluator
{
    private static readonly (int RowDelta, int ColumnDelta)[] Directions =
    {
        (1, 0),
        (0, 1),
        (1, 1),
        (1, -1),
    };

    public static BoardOutcome Evaluate(BoardState board)
    {
        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                var stone = board.GetStone(row, column);
                if (stone == Stone.None)
                {
                    continue;
                }

                foreach (var (rowDelta, columnDelta) in Directions)
                {
                    var previousRow = row - rowDelta;
                    var previousColumn = column - columnDelta;
                    if (board.IsInside(previousRow, previousColumn) && board.GetStone(previousRow, previousColumn) == stone)
                    {
                        continue;
                    }

                    var length = CountDirection(board, row, column, rowDelta, columnDelta, stone);
                    if (length >= 5)
                    {
                        return new BoardOutcome(stone, false);
                    }
                }
            }
        }

        return new BoardOutcome(Stone.None, board.IsFull());
    }

    public static bool IsWinningMove(BoardState board, int row, int column)
    {
        var stone = board.GetStone(row, column);
        if (stone == Stone.None)
        {
            return false;
        }

        foreach (var (rowDelta, columnDelta) in Directions)
        {
            var length = 1;
            length += CountOneSide(board, row, column, rowDelta, columnDelta, stone);
            length += CountOneSide(board, row, column, -rowDelta, -columnDelta, stone);
            if (length >= 5)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountDirection(BoardState board, int row, int column, int rowDelta, int columnDelta, Stone stone)
    {
        var length = 0;
        var currentRow = row;
        var currentColumn = column;

        while (board.IsInside(currentRow, currentColumn) && board.GetStone(currentRow, currentColumn) == stone)
        {
            length++;
            currentRow += rowDelta;
            currentColumn += columnDelta;
        }

        return length;
    }

    private static int CountOneSide(BoardState board, int row, int column, int rowDelta, int columnDelta, Stone stone)
    {
        var count = 0;
        var currentRow = row + rowDelta;
        var currentColumn = column + columnDelta;

        while (board.IsInside(currentRow, currentColumn) && board.GetStone(currentRow, currentColumn) == stone)
        {
            count++;
            currentRow += rowDelta;
            currentColumn += columnDelta;
        }

        return count;
    }
}