namespace GomokuApp.Sudoku;

public sealed class SudokuGenerator
{
    private readonly Random random = new();

    public SudokuPuzzle Generate(SudokuDifficulty difficulty)
    {
        var solution = new int[SudokuSolver.Size, SudokuSolver.Size];
        FillBoard(solution);

        var puzzle = SudokuSolver.CloneBoard(solution);
        var cells = Enumerable.Range(0, SudokuSolver.Size * SudokuSolver.Size)
            .OrderBy(_ => random.Next())
            .ToList();
        var targetRemoved = (SudokuSolver.Size * SudokuSolver.Size) - difficulty.GivenCells;
        var removed = 0;

        foreach (var index in cells)
        {
            if (removed >= targetRemoved)
            {
                break;
            }

            var row = index / SudokuSolver.Size;
            var column = index % SudokuSolver.Size;
            var value = puzzle[row, column];
            puzzle[row, column] = 0;

            if (SudokuSolver.CountSolutions(puzzle, maxSolutions: 2) == 1)
            {
                removed++;
            }
            else
            {
                puzzle[row, column] = value;
            }
        }

        return new SudokuPuzzle(puzzle, solution);
    }

    private bool FillBoard(int[,] board)
    {
        if (!TryFindFirstEmpty(board, out var row, out var column))
        {
            return true;
        }

        foreach (var value in ShuffledDigits())
        {
            board[row, column] = value;
            if (SudokuSolver.IsPlacementValid(board, row, column, value) && FillBoard(board))
            {
                return true;
            }

            board[row, column] = 0;
        }

        return false;
    }

    private IEnumerable<int> ShuffledDigits()
    {
        var digits = Enumerable.Range(1, 9).ToArray();
        for (var index = digits.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (digits[index], digits[swap]) = (digits[swap], digits[index]);
        }

        return digits;
    }

    private static bool TryFindFirstEmpty(int[,] board, out int row, out int column)
    {
        for (row = 0; row < SudokuSolver.Size; row++)
        {
            for (column = 0; column < SudokuSolver.Size; column++)
            {
                if (board[row, column] == 0)
                {
                    return true;
                }
            }
        }

        row = -1;
        column = -1;
        return false;
    }
}
