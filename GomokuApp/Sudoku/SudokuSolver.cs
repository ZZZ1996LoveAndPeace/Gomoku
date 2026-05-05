namespace GomokuApp.Sudoku;

public static class SudokuSolver
{
    public const int Size = 9;
    private const int BoxSize = 3;
    private const int AllCandidatesMask = 0b11_1111_1110;

    public static bool IsCompleteAndValid(int[,] board)
    {
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                var value = board[row, column];
                if (value is < 1 or > 9 || !IsPlacementValid(board, row, column, value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static int CountSolutions(int[,] puzzle, int maxSolutions = 2)
    {
        var board = CloneBoard(puzzle);
        var count = 0;
        CountSolutions(board, maxSolutions, ref count);
        return count;
    }

    public static bool TrySolve(int[,] puzzle, out int[,] solution)
    {
        solution = CloneBoard(puzzle);
        return Solve(solution);
    }

    public static int[,] CloneBoard(int[,] board)
    {
        var clone = new int[Size, Size];
        Array.Copy(board, clone, board.Length);
        return clone;
    }

    public static bool IsPlacementValid(int[,] board, int row, int column, int value)
    {
        if (value is < 1 or > 9)
        {
            return false;
        }

        for (var index = 0; index < Size; index++)
        {
            if (index != column && board[row, index] == value)
            {
                return false;
            }

            if (index != row && board[index, column] == value)
            {
                return false;
            }
        }

        var boxRow = row / BoxSize * BoxSize;
        var boxColumn = column / BoxSize * BoxSize;
        for (var r = boxRow; r < boxRow + BoxSize; r++)
        {
            for (var c = boxColumn; c < boxColumn + BoxSize; c++)
            {
                if ((r != row || c != column) && board[r, c] == value)
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static int GetCandidateMask(int[,] board, int row, int column)
    {
        if (board[row, column] != 0)
        {
            return 0;
        }

        var mask = AllCandidatesMask;
        for (var index = 0; index < Size; index++)
        {
            mask &= ~(1 << board[row, index]);
            mask &= ~(1 << board[index, column]);
        }

        var boxRow = row / BoxSize * BoxSize;
        var boxColumn = column / BoxSize * BoxSize;
        for (var r = boxRow; r < boxRow + BoxSize; r++)
        {
            for (var c = boxColumn; c < boxColumn + BoxSize; c++)
            {
                mask &= ~(1 << board[r, c]);
            }
        }

        return mask;
    }

    private static bool Solve(int[,] board)
    {
        if (!TryFindBestEmptyCell(board, out var row, out var column, out var mask))
        {
            return true;
        }

        while (mask != 0)
        {
            var bit = mask & -mask;
            var value = BitToValue(bit);
            board[row, column] = value;
            if (Solve(board))
            {
                return true;
            }

            board[row, column] = 0;
            mask &= ~bit;
        }

        return false;
    }

    private static void CountSolutions(int[,] board, int maxSolutions, ref int count)
    {
        if (count >= maxSolutions)
        {
            return;
        }

        if (!TryFindBestEmptyCell(board, out var row, out var column, out var mask))
        {
            count++;
            return;
        }

        while (mask != 0 && count < maxSolutions)
        {
            var bit = mask & -mask;
            var value = BitToValue(bit);
            board[row, column] = value;
            CountSolutions(board, maxSolutions, ref count);
            board[row, column] = 0;
            mask &= ~bit;
        }
    }

    private static bool TryFindBestEmptyCell(int[,] board, out int row, out int column, out int mask)
    {
        row = -1;
        column = -1;
        mask = 0;
        var bestCount = int.MaxValue;

        for (var r = 0; r < Size; r++)
        {
            for (var c = 0; c < Size; c++)
            {
                if (board[r, c] != 0)
                {
                    continue;
                }

                var candidateMask = GetCandidateMask(board, r, c);
                var candidateCount = CountBits(candidateMask);
                if (candidateCount == 0)
                {
                    row = r;
                    column = c;
                    mask = 0;
                    return true;
                }

                if (candidateCount < bestCount)
                {
                    bestCount = candidateCount;
                    row = r;
                    column = c;
                    mask = candidateMask;
                    if (bestCount == 1)
                    {
                        return true;
                    }
                }
            }
        }

        return row >= 0;
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private static int BitToValue(int bit)
    {
        for (var value = 1; value <= 9; value++)
        {
            if (bit == (1 << value))
            {
                return value;
            }
        }

        return 0;
    }
}
