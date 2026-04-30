using GomokuApp.Core;
using GomokuApp.Models;

namespace GomokuApp.AI;

public static class PatternScorer
{
    private static readonly (int RowDelta, int ColumnDelta)[] Directions =
    {
        (1, 0),
        (0, 1),
        (1, 1),
        (1, -1),
    };

    public const int FiveInRowScore = 1_000_000;
    private const int OpenFourScore = 260_000;
    private const int SimpleFourScore = 78_000;
    private const int OpenThreeScore = 24_000;
    private const int BrokenThreeScore = 6_500;
    private const int DoubleThreatBonus = 220_000;

    public readonly record struct MoveAnalysis(
        int Score,
        bool IsWinningMove,
        int WinningThreats,
        int OpenFours,
        int SimpleFours,
        int OpenThrees,
        int BrokenThrees);

    public static int EvaluateBoard(BoardState board, Stone perspective)
    {
        var selfScore = EvaluateForStone(board, perspective);
        var opponentScore = EvaluateForStone(board, perspective.Opponent());
        var selfPotential = EvaluateMovePotential(board, perspective);
        var opponentPotential = EvaluateMovePotential(board, perspective.Opponent());
        return selfScore + selfPotential - (int)((opponentScore + opponentPotential) * 1.16);
    }

    public static int EvaluateMove(BoardState board, int row, int column, Stone stone)
    {
        return AnalyzeMove(board, row, column, stone).Score;
    }

    public static MoveAnalysis AnalyzeMove(BoardState board, int row, int column, Stone stone)
    {
        if (!board.IsInside(row, column) || board.GetStone(row, column) != Stone.None)
        {
            return new MoveAnalysis(int.MinValue, false, 0, 0, 0, 0, 0);
        }

        board.SetStone(row, column, stone);
        var analysis = AnalyzePlacedMove(board, row, column, stone);
        board.SetStone(row, column, Stone.None);
        return analysis;
    }

    private static MoveAnalysis AnalyzePlacedMove(BoardState board, int row, int column, Stone stone)
    {
        var score = 0;
        var isWinningMove = false;
        var winningThreats = 0;
        var openFours = 0;
        var simpleFours = 0;
        var openThrees = 0;
        var brokenThrees = 0;

        foreach (var (rowDelta, columnDelta) in Directions)
        {
            var forward = CountOneSide(board, row, column, rowDelta, columnDelta, stone);
            var backward = CountOneSide(board, row, column, -rowDelta, -columnDelta, stone);
            var length = forward + backward + 1;
            var openEnds = CountOpenEnds(board, row, column, rowDelta, columnDelta, stone, forward, backward);
            score += ScorePattern(length, openEnds);

            if (length >= 5)
            {
                isWinningMove = true;
                continue;
            }

            var directionWinningThreats = CountWinningPointsInDirection(board, row, column, rowDelta, columnDelta, stone);
            winningThreats += directionWinningThreats;

            if (directionWinningThreats >= 2)
            {
                openFours++;
                score += OpenFourScore;
                continue;
            }

            if (directionWinningThreats == 1)
            {
                simpleFours++;
                score += SimpleFourScore;
                continue;
            }

            var (openThreeExtensions, brokenThreeExtensions) = CountThreeExtensionsInDirection(board, row, column, rowDelta, columnDelta, stone);
            if (openThreeExtensions > 0)
            {
                openThrees++;
                score += OpenThreeScore;
            }
            else if (brokenThreeExtensions > 0)
            {
                brokenThrees++;
                score += BrokenThreeScore;
            }
        }

        var forcingThreats = openFours + simpleFours + openThrees;
        if (winningThreats >= 2 || forcingThreats >= 2)
        {
            score += DoubleThreatBonus;
        }

        if (openFours > 0 && openThrees > 0)
        {
            score += DoubleThreatBonus;
        }

        return new MoveAnalysis(score, isWinningMove, winningThreats, openFours, simpleFours, openThrees, brokenThrees);
    }

    private static int EvaluateForStone(BoardState board, Stone stone)
    {
        var total = 0;

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != stone)
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

                    var length = 1 + CountOneSide(board, row, column, rowDelta, columnDelta, stone);
                    var openEnds = 0;
                    if (board.IsInside(previousRow, previousColumn) && board.GetStone(previousRow, previousColumn) == Stone.None)
                    {
                        openEnds++;
                    }

                    var nextRow = row + (length * rowDelta);
                    var nextColumn = column + (length * columnDelta);
                    if (board.IsInside(nextRow, nextColumn) && board.GetStone(nextRow, nextColumn) == Stone.None)
                    {
                        openEnds++;
                    }

                    total += ScorePattern(length, openEnds);
                }
            }
        }

        return total;
    }

    private static int EvaluateMovePotential(BoardState board, Stone stone)
    {
        Span<int> topScores = stackalloc int[6];

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != Stone.None || !board.HasNeighbor(row, column))
                {
                    continue;
                }

                var score = AnalyzeMove(board, row, column, stone).Score;
                InsertTopScore(topScores, score);
            }
        }

        var total = 0;
        for (var index = 0; index < topScores.Length; index++)
        {
            total += topScores[index] / (index + 1);
        }

        return total;
    }

    private static void InsertTopScore(Span<int> topScores, int score)
    {
        for (var index = 0; index < topScores.Length; index++)
        {
            if (score <= topScores[index])
            {
                continue;
            }

            for (var shift = topScores.Length - 1; shift > index; shift--)
            {
                topScores[shift] = topScores[shift - 1];
            }

            topScores[index] = score;
            return;
        }
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

    private static int CountOpenEnds(BoardState board, int row, int column, int rowDelta, int columnDelta, Stone stone, int forward, int backward)
    {
        var openEnds = 0;
        var forwardRow = row + ((forward + 1) * rowDelta);
        var forwardColumn = column + ((forward + 1) * columnDelta);
        if (board.IsInside(forwardRow, forwardColumn) && board.GetStone(forwardRow, forwardColumn) == Stone.None)
        {
            openEnds++;
        }

        var backwardRow = row - ((backward + 1) * rowDelta);
        var backwardColumn = column - ((backward + 1) * columnDelta);
        if (board.IsInside(backwardRow, backwardColumn) && board.GetStone(backwardRow, backwardColumn) == Stone.None)
        {
            openEnds++;
        }

        return openEnds;
    }

    private static int CountWinningPointsInDirection(BoardState board, int row, int column, int rowDelta, int columnDelta, Stone stone)
    {
        var count = 0;

        for (var offset = -4; offset <= 4; offset++)
        {
            if (offset == 0)
            {
                continue;
            }

            var targetRow = row + (offset * rowDelta);
            var targetColumn = column + (offset * columnDelta);
            if (!board.IsInside(targetRow, targetColumn) || board.GetStone(targetRow, targetColumn) != Stone.None)
            {
                continue;
            }

            board.SetStone(targetRow, targetColumn, stone);
            var isWinningPoint = CountLineLength(board, targetRow, targetColumn, rowDelta, columnDelta, stone) >= 5;
            board.SetStone(targetRow, targetColumn, Stone.None);

            if (!isWinningPoint)
            {
                continue;
            }

            count++;
            if (count >= 3)
            {
                return count;
            }
        }

        return count;
    }

    private static (int OpenThreeExtensions, int BrokenThreeExtensions) CountThreeExtensionsInDirection(
        BoardState board,
        int row,
        int column,
        int rowDelta,
        int columnDelta,
        Stone stone)
    {
        var openThreeExtensions = 0;
        var brokenThreeExtensions = 0;

        for (var offset = -4; offset <= 4; offset++)
        {
            if (offset == 0)
            {
                continue;
            }

            var targetRow = row + (offset * rowDelta);
            var targetColumn = column + (offset * columnDelta);
            if (!board.IsInside(targetRow, targetColumn) || board.GetStone(targetRow, targetColumn) != Stone.None)
            {
                continue;
            }

            board.SetStone(targetRow, targetColumn, stone);
            if (CountLineLength(board, targetRow, targetColumn, rowDelta, columnDelta, stone) < 5)
            {
                var winningThreats = CountWinningPointsInDirection(board, targetRow, targetColumn, rowDelta, columnDelta, stone);
                if (winningThreats >= 2)
                {
                    openThreeExtensions++;
                }
                else if (winningThreats == 1)
                {
                    brokenThreeExtensions++;
                }
            }

            board.SetStone(targetRow, targetColumn, Stone.None);

            if (openThreeExtensions >= 2 || brokenThreeExtensions >= 2)
            {
                break;
            }
        }

        return (openThreeExtensions, brokenThreeExtensions);
    }

    private static int CountLineLength(BoardState board, int row, int column, int rowDelta, int columnDelta, Stone stone)
    {
        return 1
            + CountOneSide(board, row, column, rowDelta, columnDelta, stone)
            + CountOneSide(board, row, column, -rowDelta, -columnDelta, stone);
    }

    private static int ScorePattern(int length, int openEnds)
    {
        if (length >= 5)
        {
            return FiveInRowScore;
        }

        return (length, openEnds) switch
        {
            (4, 2) => 160_000,
            (4, 1) => 28_000,
            (3, 2) => 9_000,
            (3, 1) => 1_200,
            (2, 2) => 260,
            (2, 1) => 45,
            (1, 2) => 12,
            _ => 0,
        };
    }
}
