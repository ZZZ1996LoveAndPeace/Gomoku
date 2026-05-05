namespace GomokuApp.Minesweeper;

public sealed class MinesweeperGame
{
    private const int MaxGenerationAttempts = 3500;
    private readonly Random random = new();
    private MinesweeperDifficulty difficulty;
    private MinesweeperCell[,] cells = new MinesweeperCell[0, 0];

    public MinesweeperGame()
        : this(MinesweeperDifficulty.Beginner)
    {
    }

    public MinesweeperGame(MinesweeperDifficulty difficulty)
    {
        this.difficulty = difficulty;
        Reset(difficulty);
    }

    public int Rows => difficulty.Rows;
    public int Columns => difficulty.Columns;
    public int MineCount => difficulty.Mines;
    public int FlaggedCount { get; private set; }
    public int RevealedCount { get; private set; }
    public int SafeCellCount => (Rows * Columns) - MineCount;
    public int GenerationAttempts { get; private set; }
    public MinesweeperGameStatus Status { get; private set; }
    public MinesweeperDifficulty Difficulty => difficulty;

    public void Reset(MinesweeperDifficulty newDifficulty)
    {
        difficulty = newDifficulty;
        cells = new MinesweeperCell[Rows, Columns];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                cells[row, column] = new MinesweeperCell();
            }
        }

        FlaggedCount = 0;
        RevealedCount = 0;
        GenerationAttempts = 0;
        Status = MinesweeperGameStatus.WaitingForFirstReveal;
    }

    public MinesweeperCell GetCell(int row, int column) => cells[row, column];

    public bool IsInside(int row, int column)
        => row >= 0 && row < Rows && column >= 0 && column < Columns;

    public MinesweeperRevealResult Reveal(int row, int column)
    {
        if (!IsInside(row, column))
        {
            return new MinesweeperRevealResult(false, false, false, [], "位置超出棋盘。");
        }

        if (Status is MinesweeperGameStatus.Won or MinesweeperGameStatus.Lost)
        {
            return new MinesweeperRevealResult(false, false, false, [], "本局已经结束。");
        }

        var cell = cells[row, column];
        if (cell.IsFlagged)
        {
            return new MinesweeperRevealResult(false, false, false, [], "已插旗的格子不能直接打开。");
        }

        var generated = false;
        if (Status == MinesweeperGameStatus.WaitingForFirstReveal)
        {
            if (!GenerateSolvableBoard(row, column))
            {
                return new MinesweeperRevealResult(false, false, false, [], "暂时没有生成出可纯推理解答的棋盘，请再点一次。");
            }

            generated = true;
            cell = cells[row, column];
            Status = MinesweeperGameStatus.Playing;
        }

        if (cell.IsRevealed)
        {
            return new MinesweeperRevealResult(false, generated, false, [], null);
        }

        if (cell.IsMine)
        {
            cell.IsRevealed = true;
            Status = MinesweeperGameStatus.Lost;
            return new MinesweeperRevealResult(true, generated, true, [new MinesweeperPosition(row, column)]);
        }

        var revealed = RevealSafeRegion(row, column);
        if (RevealedCount == SafeCellCount)
        {
            Status = MinesweeperGameStatus.Won;
            FlagAllMines();
        }

        return new MinesweeperRevealResult(revealed.Count > 0, generated, false, revealed);
    }

    public bool ToggleFlag(int row, int column, out string? message)
    {
        message = null;
        if (!IsInside(row, column))
        {
            message = "位置超出棋盘。";
            return false;
        }

        if (Status is MinesweeperGameStatus.Won or MinesweeperGameStatus.Lost)
        {
            message = "本局已经结束。";
            return false;
        }

        if (Status == MinesweeperGameStatus.WaitingForFirstReveal)
        {
            message = "请先打开第一格，再插旗。";
            return false;
        }

        var cell = cells[row, column];
        if (cell.IsRevealed)
        {
            message = "已经打开的格子不能插旗。";
            return false;
        }

        cell.IsFlagged = !cell.IsFlagged;
        FlaggedCount += cell.IsFlagged ? 1 : -1;
        return true;
    }

    public void RevealAllMines()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (cells[row, column].IsMine)
                {
                    cells[row, column].IsRevealed = true;
                }
            }
        }
    }

    private bool GenerateSolvableBoard(int firstRow, int firstColumn)
    {
        var excluded = GetOpeningArea(firstRow, firstColumn).ToHashSet();
        if ((Rows * Columns) - excluded.Count < MineCount)
        {
            excluded = [new MinesweeperPosition(firstRow, firstColumn)];
        }

        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            ClearMinesAndNumbers();
            PlaceMines(excluded);
            CalculateAdjacentMineCounts();
            GenerationAttempts = attempt;

            if (cells[firstRow, firstColumn].AdjacentMines != 0)
            {
                continue;
            }

            if (MinesweeperSolvabilityVerifier.CanSolve(cells, firstRow, firstColumn))
            {
                return true;
            }
        }

        ClearMinesAndNumbers();
        return false;
    }

    private void PlaceMines(HashSet<MinesweeperPosition> excluded)
    {
        var available = new List<MinesweeperPosition>(Rows * Columns);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var position = new MinesweeperPosition(row, column);
                if (!excluded.Contains(position))
                {
                    available.Add(position);
                }
            }
        }

        for (var mine = 0; mine < MineCount; mine++)
        {
            var index = random.Next(available.Count);
            var position = available[index];
            available[index] = available[^1];
            available.RemoveAt(available.Count - 1);
            cells[position.Row, position.Column].IsMine = true;
        }
    }

    private void ClearMinesAndNumbers()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                cells[row, column].IsMine = false;
                cells[row, column].AdjacentMines = 0;
                cells[row, column].IsRevealed = false;
                cells[row, column].IsFlagged = false;
            }
        }
    }

    private void CalculateAdjacentMineCounts()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (cells[row, column].IsMine)
                {
                    continue;
                }

                var count = 0;
                foreach (var neighbor in GetNeighbors(row, column))
                {
                    if (cells[neighbor.Row, neighbor.Column].IsMine)
                    {
                        count++;
                    }
                }

                cells[row, column].AdjacentMines = count;
            }
        }
    }

    private IReadOnlyList<MinesweeperPosition> RevealSafeRegion(int startRow, int startColumn)
    {
        var revealed = new List<MinesweeperPosition>();
        var queue = new Queue<MinesweeperPosition>();
        queue.Enqueue(new MinesweeperPosition(startRow, startColumn));

        while (queue.Count > 0)
        {
            var position = queue.Dequeue();
            var cell = cells[position.Row, position.Column];
            if (cell.IsRevealed || cell.IsFlagged || cell.IsMine)
            {
                continue;
            }

            cell.IsRevealed = true;
            RevealedCount++;
            revealed.Add(position);

            if (cell.AdjacentMines != 0)
            {
                continue;
            }

            foreach (var neighbor in GetNeighbors(position.Row, position.Column))
            {
                var neighborCell = cells[neighbor.Row, neighbor.Column];
                if (!neighborCell.IsRevealed && !neighborCell.IsFlagged && !neighborCell.IsMine)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return revealed;
    }

    private IEnumerable<MinesweeperPosition> GetOpeningArea(int row, int column)
    {
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var nextRow = row + dr;
                var nextColumn = column + dc;
                if (IsInside(nextRow, nextColumn))
                {
                    yield return new MinesweeperPosition(nextRow, nextColumn);
                }
            }
        }
    }

    private IEnumerable<MinesweeperPosition> GetNeighbors(int row, int column)
    {
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0)
                {
                    continue;
                }

                var nextRow = row + dr;
                var nextColumn = column + dc;
                if (IsInside(nextRow, nextColumn))
                {
                    yield return new MinesweeperPosition(nextRow, nextColumn);
                }
            }
        }
    }

    private void FlagAllMines()
    {
        FlaggedCount = 0;
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                cells[row, column].IsFlagged = cells[row, column].IsMine;
                if (cells[row, column].IsFlagged)
                {
                    FlaggedCount++;
                }
            }
        }
    }
}
