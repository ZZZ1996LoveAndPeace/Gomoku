using GomokuApp.Models;

namespace GomokuApp.Core;

public sealed class BoardState
{
    private readonly Stone[,] cells;

    public BoardState()
    {
        cells = new Stone[Size, Size];
        StoneCount = 0;
    }

    private BoardState(Stone[,] cells, int stoneCount)
    {
        this.cells = cells;
        StoneCount = stoneCount;
    }

    public const int Size = 15;

    public int StoneCount { get; private set; }

    public bool IsInside(int row, int column) => row >= 0 && row < Size && column >= 0 && column < Size;

    public Stone GetStone(int row, int column)
    {
        ValidateCoordinates(row, column);
        return cells[row, column];
    }

    public bool PlaceStone(int row, int column, Stone stone)
    {
        if (stone == Stone.None)
        {
            return false;
        }

        ValidateCoordinates(row, column);
        if (cells[row, column] != Stone.None)
        {
            return false;
        }

        cells[row, column] = stone;
        StoneCount++;
        return true;
    }

    public void SetStone(int row, int column, Stone stone)
    {
        ValidateCoordinates(row, column);
        var previousStone = cells[row, column];
        if (previousStone == stone)
        {
            return;
        }

        if (previousStone == Stone.None && stone != Stone.None)
        {
            StoneCount++;
        }
        else if (previousStone != Stone.None && stone == Stone.None)
        {
            StoneCount--;
        }

        cells[row, column] = stone;
    }

    public void Clear()
    {
        Array.Clear(cells, 0, cells.Length);
        StoneCount = 0;
    }

    public int CountStones()
    {
        return StoneCount;
    }

    public bool IsFull()
    {
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                if (cells[row, column] == Stone.None)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool HasNeighbor(int row, int column, int radius = 2)
    {
        var startRow = Math.Max(0, row - radius);
        var endRow = Math.Min(Size - 1, row + radius);
        var startColumn = Math.Max(0, column - radius);
        var endColumn = Math.Min(Size - 1, column + radius);

        for (var currentRow = startRow; currentRow <= endRow; currentRow++)
        {
            for (var currentColumn = startColumn; currentColumn <= endColumn; currentColumn++)
            {
                if ((currentRow != row || currentColumn != column) && cells[currentRow, currentColumn] != Stone.None)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public IEnumerable<(int Row, int Column, Stone Stone)> OccupiedCells()
    {
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                if (cells[row, column] != Stone.None)
                {
                    yield return (row, column, cells[row, column]);
                }
            }
        }
    }

    public BoardState Clone()
    {
        var copy = new Stone[Size, Size];
        Array.Copy(cells, copy, cells.Length);
        return new BoardState(copy, StoneCount);
    }

    private void ValidateCoordinates(int row, int column)
    {
        if (!IsInside(row, column))
        {
            throw new ArgumentOutOfRangeException($"坐标超出棋盘范围: ({row}, {column})");
        }
    }
}