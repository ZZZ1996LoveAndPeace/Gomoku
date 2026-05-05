namespace GomokuApp.Sudoku;

public sealed class SudokuGame
{
    private readonly SudokuGenerator generator = new();
    private readonly SudokuCell[,] cells = new SudokuCell[SudokuSolver.Size, SudokuSolver.Size];
    private int[,] solution = new int[SudokuSolver.Size, SudokuSolver.Size];

    public SudokuGame()
    {
        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                cells[row, column] = new SudokuCell();
            }
        }

        Reset(SudokuDifficulty.Easy);
    }

    public SudokuDifficulty Difficulty { get; private set; } = SudokuDifficulty.Easy;
    public SudokuGameStatus Status { get; private set; } = SudokuGameStatus.Playing;
    public int Mistakes { get; private set; }
    public int FilledCount { get; private set; }
    public int HintCount { get; private set; }
    public int SelectedNumber { get; set; } = 1;
    public bool IsNoteMode { get; set; }

    public SudokuCell GetCell(int row, int column) => cells[row, column];
    public int GetSolutionValue(int row, int column) => solution[row, column];

    public void Reset(SudokuDifficulty difficulty)
    {
        Difficulty = difficulty;
        Status = SudokuGameStatus.Playing;
        Mistakes = 0;
        FilledCount = 0;
        HintCount = 0;
        SelectedNumber = 1;
        IsNoteMode = false;

        var puzzle = generator.Generate(difficulty);
        solution = puzzle.Solution;

        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                var cell = cells[row, column];
                cell.GivenValue = puzzle.Puzzle[row, column];
                cell.PlayerValue = 0;
                cell.HasWrongValue = false;
                cell.ClearNotes();
                if (cell.IsGiven)
                {
                    FilledCount++;
                }
            }
        }
    }

    public SudokuMoveResult SetValue(int row, int column, int value)
    {
        if (!IsInside(row, column))
        {
            return new SudokuMoveResult(false, "位置超出棋盘。");
        }

        if (Status == SudokuGameStatus.Won)
        {
            return new SudokuMoveResult(false, "本局已经完成。");
        }

        var cell = cells[row, column];
        if (cell.IsGiven)
        {
            return new SudokuMoveResult(false, "题目数字不能修改。");
        }

        if (value is < 0 or > 9)
        {
            return new SudokuMoveResult(false, "请输入 1-9，或用清除按钮删除。");
        }

        if (value == 0)
        {
            return ClearCell(row, column);
        }

        if (IsNoteMode)
        {
            cell.ToggleNote(value);
            return new SudokuMoveResult(true);
        }

        var wasFilled = cell.PlayerValue != 0;
        cell.PlayerValue = value;
        cell.ClearNotes();
        cell.HasWrongValue = value != solution[row, column];
        if (!wasFilled)
        {
            FilledCount++;
        }

        if (cell.HasWrongValue)
        {
            Mistakes++;
            return new SudokuMoveResult(true, $"这里不是 {value}。");
        }

        ClearPeerNote(row, column, value);
        UpdateWinState();
        return new SudokuMoveResult(true);
    }

    public SudokuMoveResult ClearCell(int row, int column)
    {
        if (!IsInside(row, column))
        {
            return new SudokuMoveResult(false, "位置超出棋盘。");
        }

        var cell = cells[row, column];
        if (cell.IsGiven)
        {
            return new SudokuMoveResult(false, "题目数字不能清除。");
        }

        if (cell.PlayerValue != 0)
        {
            FilledCount--;
        }

        cell.PlayerValue = 0;
        cell.HasWrongValue = false;
        cell.ClearNotes();
        return new SudokuMoveResult(true);
    }

    public SudokuMoveResult RevealHint()
    {
        if (Status == SudokuGameStatus.Won)
        {
            return new SudokuMoveResult(false, "本局已经完成。");
        }

        var candidates = new List<SudokuPosition>();
        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                var cell = cells[row, column];
                if (!cell.IsGiven && cell.PlayerValue != solution[row, column])
                {
                    candidates.Add(new SudokuPosition(row, column));
                }
            }
        }

        if (candidates.Count == 0)
        {
            UpdateWinState();
            return new SudokuMoveResult(false, "已经没有可提示的格子。");
        }

        var position = candidates[Random.Shared.Next(candidates.Count)];
        var target = cells[position.Row, position.Column];
        if (target.PlayerValue == 0)
        {
            FilledCount++;
        }

        target.PlayerValue = solution[position.Row, position.Column];
        target.HasWrongValue = false;
        target.ClearNotes();
        HintCount++;
        ClearPeerNote(position.Row, position.Column, target.PlayerValue);
        UpdateWinState();
        return new SudokuMoveResult(true, $"已填入第 {position.Row + 1} 行第 {position.Column + 1} 列。");
    }

    public bool ConflictsWithPeers(int row, int column)
    {
        var value = cells[row, column].DisplayValue;
        if (value == 0)
        {
            return false;
        }

        for (var index = 0; index < SudokuSolver.Size; index++)
        {
            if (index != column && cells[row, index].DisplayValue == value)
            {
                return true;
            }

            if (index != row && cells[index, column].DisplayValue == value)
            {
                return true;
            }
        }

        var boxRow = row / 3 * 3;
        var boxColumn = column / 3 * 3;
        for (var r = boxRow; r < boxRow + 3; r++)
        {
            for (var c = boxColumn; c < boxColumn + 3; c++)
            {
                if ((r != row || c != column) && cells[r, c].DisplayValue == value)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsInside(int row, int column)
        => row >= 0 && row < SudokuSolver.Size && column >= 0 && column < SudokuSolver.Size;

    private void UpdateWinState()
    {
        if (FilledCount < SudokuSolver.Size * SudokuSolver.Size)
        {
            return;
        }

        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                if (cells[row, column].DisplayValue != solution[row, column])
                {
                    return;
                }
            }
        }

        Status = SudokuGameStatus.Won;
    }

    private void ClearPeerNote(int row, int column, int value)
    {
        for (var index = 0; index < SudokuSolver.Size; index++)
        {
            cells[row, index].ClearNoteValue(value);
            cells[index, column].ClearNoteValue(value);
        }

        var boxRow = row / 3 * 3;
        var boxColumn = column / 3 * 3;
        for (var r = boxRow; r < boxRow + 3; r++)
        {
            for (var c = boxColumn; c < boxColumn + 3; c++)
            {
                cells[r, c].ClearNoteValue(value);
            }
        }
    }
}
