using GomokuApp.AI;
using GomokuApp.Core;
using GomokuApp.Minesweeper;
using GomokuApp.Models;
using GomokuApp.Sudoku;

var tests = new[]
{
    new AiMoveTest(
        Name: "Immediate win: complete an open four",
        Difficulty: AiDifficulty.Normal,
        AiSide: Stone.Black,
        BoardRows:
        [
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "......XXXX.....",
            ".....O.O.......",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
        ],
        ExpectedMoves: [new Position(7, 5), new Position(7, 10)]),

    new AiMoveTest(
        Name: "Defense: block opponent open four",
        Difficulty: AiDifficulty.Normal,
        AiSide: Stone.Black,
        BoardRows:
        [
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            ".....OOOO......",
            "......X.X......",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
        ],
        ExpectedMoves: [new Position(7, 4), new Position(7, 9)]),

    new AiMoveTest(
        Name: "VCF: create an open four that forces the win",
        Difficulty: AiDifficulty.Hard,
        AiSide: Stone.Black,
        BoardRows:
        [
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            ".....O.........",
            "......XXX......",
            ".....O.O.......",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
        ],
        ExpectedMoves: [new Position(7, 5), new Position(7, 9)]),

    new AiMoveTest(
        Name: "VCF: prefer a double winning-point fork over a quiet extension",
        Difficulty: AiDifficulty.Hard,
        AiSide: Stone.Black,
        BoardRows:
        [
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            ".......O.......",
            "......OXX......",
            ".....XXX.......",
            "......O........",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
        ],
        ExpectedMoves: [new Position(7, 8)]),

    new AiMoveTest(
        Name: "VCT: Master recognizes a double-three pressure move",
        Difficulty: AiDifficulty.Master,
        AiSide: Stone.Black,
        BoardRows:
        [
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            ".......X.......",
            "......X.X......",
            ".......X.......",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
        ],
        ExpectedMoves: [new Position(7, 7)]),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

var minesweeperFailed = 0;
foreach (var test in MinesweeperGenerationTest.CreateCases())
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        minesweeperFailed++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

var sudokuFailed = 0;
foreach (var test in SudokuRegressionTest.CreateCases())
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        sudokuFailed++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

failed += minesweeperFailed + sudokuFailed;
if (failed > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{failed} regression test(s) failed.");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"All {tests.Length + MinesweeperGenerationTest.CreateCases().Count + SudokuRegressionTest.CreateCases().Count} regression tests passed.");
return 0;

internal readonly record struct Position(int Row, int Column)
{
    public override string ToString() => $"({Row}, {Column})";
}

internal sealed record AiMoveTest(
    string Name,
    AiDifficulty Difficulty,
    Stone AiSide,
    string[] BoardRows,
    Position[] ExpectedMoves)
{
    public void Run()
    {
        var board = ParseBoard(BoardRows);
        var move = new AiEngine().FindBestMove(board, AiSide, Difficulty);
        var actual = new Position(move.Row, move.Column);

        if (ExpectedMoves.Contains(actual))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected one of: {string.Join(", ", ExpectedMoves)}{Environment.NewLine}" +
            $"Actual: {actual} with score {move.Score}{Environment.NewLine}" +
            RenderBoardWithMove(board, actual));
    }

    private static BoardState ParseBoard(IReadOnlyList<string> rows)
    {
        if (rows.Count != BoardState.Size)
        {
            throw new ArgumentException($"Board must have {BoardState.Size} rows.", nameof(rows));
        }

        var board = new BoardState();
        for (var row = 0; row < rows.Count; row++)
        {
            if (rows[row].Length != BoardState.Size)
            {
                throw new ArgumentException($"Row {row} must have {BoardState.Size} columns.", nameof(rows));
            }

            for (var column = 0; column < rows[row].Length; column++)
            {
                var stone = rows[row][column] switch
                {
                    '.' => Stone.None,
                    'X' => Stone.Black,
                    'O' => Stone.White,
                    _ => throw new ArgumentException($"Unsupported board character '{rows[row][column]}'.", nameof(rows)),
                };

                if (stone != Stone.None)
                {
                    board.SetStone(row, column, stone);
                }
            }
        }

        return board;
    }

    private static string RenderBoardWithMove(BoardState board, Position actual)
    {
        var rows = new List<string>(BoardState.Size);
        for (var row = 0; row < BoardState.Size; row++)
        {
            var cells = new char[BoardState.Size];
            for (var column = 0; column < BoardState.Size; column++)
            {
                cells[column] = board.GetStone(row, column) switch
                {
                    Stone.Black => 'X',
                    Stone.White => 'O',
                    _ => '.',
                };
            }

            if (actual.Row == row && board.IsInside(actual.Row, actual.Column))
            {
                cells[actual.Column] = '*';
            }

            rows.Add($"{row,2}: {new string(cells)}");
        }

        return string.Join(Environment.NewLine, rows);
    }
}

internal sealed record MinesweeperGenerationTest(
    string Name,
    MinesweeperDifficulty Difficulty,
    Position FirstClick)
{
    public static IReadOnlyList<MinesweeperGenerationTest> CreateCases() =>
    [
        new("Minesweeper beginner: center opens a blank area", MinesweeperDifficulty.Beginner, new Position(4, 4)),
        new("Minesweeper beginner: corner opens a blank area", MinesweeperDifficulty.Beginner, new Position(0, 0)),
        new("Minesweeper classic: center opens a blank area", MinesweeperDifficulty.Classic, new Position(6, 8)),
    ];

    public void Run()
    {
        var game = new MinesweeperGame(Difficulty);
        var result = game.Reveal(FirstClick.Row, FirstClick.Column);
        if (!result.GeneratedBoard || result.HitMine || result.Message is not null)
        {
            throw new InvalidOperationException($"First reveal failed: {result.Message ?? "unknown error"}");
        }

        var firstCell = game.GetCell(FirstClick.Row, FirstClick.Column);
        if (!firstCell.IsRevealed)
        {
            throw new InvalidOperationException("The first clicked cell was not revealed.");
        }

        if (firstCell.IsMine || firstCell.AdjacentMines != 0)
        {
            throw new InvalidOperationException("The first clicked cell was not guaranteed to be a blank zero cell.");
        }

        foreach (var neighbor in Neighbors(game, FirstClick.Row, FirstClick.Column))
        {
            if (game.GetCell(neighbor.Row, neighbor.Column).IsMine)
            {
                throw new InvalidOperationException($"Opening neighbor {neighbor} contains a mine.");
            }
        }

        if (result.RevealedCells.Count < 4)
        {
            throw new InvalidOperationException($"Opening was too small: only {result.RevealedCells.Count} cells revealed.");
        }

        if (game.GenerationAttempts <= 0)
        {
            throw new InvalidOperationException("The no-guess generator did not report attempts.");
        }
    }

    private static IEnumerable<Position> Neighbors(MinesweeperGame game, int row, int column)
    {
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var nextRow = row + dr;
                var nextColumn = column + dc;
                if (game.IsInside(nextRow, nextColumn))
                {
                    yield return new Position(nextRow, nextColumn);
                }
            }
        }
    }
}

internal sealed record SudokuRegressionTest(string Name, Action Run)
{
    public static IReadOnlyList<SudokuRegressionTest> CreateCases() =>
    [
        new("Sudoku generator creates a unique-solution puzzle", GeneratorCreatesUniquePuzzle),
        new("Sudoku game accepts the solved board and wins", GameWinsAfterCorrectSolution),
        new("Sudoku game detects wrong player input", GameDetectsWrongInput),
    ];

    private static void GeneratorCreatesUniquePuzzle()
    {
        var puzzle = new SudokuGenerator().Generate(SudokuDifficulty.Easy);
        if (!SudokuSolver.IsCompleteAndValid(puzzle.Solution))
        {
            throw new InvalidOperationException("Generated solution is not a valid completed Sudoku board.");
        }

        if (SudokuSolver.CountSolutions(puzzle.Puzzle, maxSolutions: 2) != 1)
        {
            throw new InvalidOperationException("Generated puzzle does not have exactly one solution.");
        }

        var givens = 0;
        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                var value = puzzle.Puzzle[row, column];
                if (value == 0)
                {
                    continue;
                }

                givens++;
                if (value != puzzle.Solution[row, column])
                {
                    throw new InvalidOperationException("A given value does not match the solution.");
                }
            }
        }

        if (givens < SudokuDifficulty.Easy.GivenCells)
        {
            throw new InvalidOperationException($"Expected at least {SudokuDifficulty.Easy.GivenCells} givens, got {givens}.");
        }
    }

    private static void GameWinsAfterCorrectSolution()
    {
        var game = new SudokuGame();
        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                var cell = game.GetCell(row, column);
                if (!cell.IsGiven)
                {
                    game.SetValue(row, column, game.GetSolutionValue(row, column));
                }
            }
        }

        if (game.Status != SudokuGameStatus.Won)
        {
            throw new InvalidOperationException("Game did not enter the won state after filling the solution.");
        }
    }

    private static void GameDetectsWrongInput()
    {
        var game = new SudokuGame();
        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                var cell = game.GetCell(row, column);
                if (cell.IsGiven)
                {
                    continue;
                }

                var wrong = game.GetSolutionValue(row, column) % 9 + 1;
                game.SetValue(row, column, wrong);
                if (!game.GetCell(row, column).HasWrongValue || game.Mistakes != 1)
                {
                    throw new InvalidOperationException("Wrong input was not marked as a mistake.");
                }

                return;
            }
        }

        throw new InvalidOperationException("Generated puzzle did not contain any editable cells.");
    }
}
