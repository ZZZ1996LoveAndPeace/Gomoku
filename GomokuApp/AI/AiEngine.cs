using GomokuApp.Core;
using GomokuApp.Models;

namespace GomokuApp.AI;

public readonly record struct AiMove(int Row, int Column, int Score);

public sealed class AiEngine
{
    private const int CenterIndex = BoardState.Size / 2;
    private const int WinScore = 10_000_000;
    private const int ThreatForkBonus = 800_000;
    private const int SingleThreatBonus = 90_000;
    private const int UnsafeMovePenalty = 1_100_000;
    private const int DefensiveThreatBonus = 140_000;
    private const int MaxTranspositionEntries = 220_000;
    private static readonly ulong[,,] ZobristTable = CreateZobristTable();
    private readonly Dictionary<PositionKey, TranspositionEntry> transpositionTable = new();
    private readonly Dictionary<ForcedSearchKey, bool> forcedSearchCache = new();

    public AiMove FindBestMove(BoardState board, Stone aiSide, AiDifficulty difficulty)
    {
        if (board.StoneCount == 0)
        {
            return new AiMove(CenterIndex, CenterIndex, 0);
        }

        var settings = GetSettings(difficulty);
        var depth = settings.GetEffectiveDepth(board.StoneCount);
        var boardHash = ComputeBoardHash(board);
        transpositionTable.Clear();
        forcedSearchCache.Clear();

        var candidates = GetCandidateMoves(board, aiSide, settings.MaxCandidates);
        if (candidates.Count == 0)
        {
            return new AiMove(CenterIndex, CenterIndex, 0);
        }

        var forcedWin = FindForcedWinMove(board, boardHash, aiSide, settings, candidates);
        if (forcedWin is { } forcingMove)
        {
            return forcingMove;
        }

        var bestMove = candidates[0];
        var bestScore = int.MinValue;

        foreach (var candidate in candidates)
        {
            board.SetStone(candidate.Row, candidate.Column, aiSide);
            var childHash = boardHash ^ GetStoneHash(candidate.Row, candidate.Column, aiSide);

            int score;
            if (RulesEvaluator.IsWinningMove(board, candidate.Row, candidate.Column))
            {
                score = WinScore;
            }
            else
            {
                score = Search(
                    board,
                    childHash,
                    aiSide.Opponent(),
                    aiSide,
                    depth - 1,
                    settings.QuiescenceDepth,
                    int.MinValue + 1,
                    int.MaxValue - 1,
                    settings.MaxCandidates);
            }

            board.SetStone(candidate.Row, candidate.Column, Stone.None);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = candidate with { Score = score };
            }
        }

        return bestMove;
    }

    private static DifficultySettings GetSettings(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => new DifficultySettings(1, 8, 0, 0, 0, 0, 0),
        AiDifficulty.Normal => new DifficultySettings(2, 12, 1, 10, 1, 3, 4),
        AiDifficulty.Hard => new DifficultySettings(3, 12, 1, 8, 1, 5, 5),
        AiDifficulty.Master => new DifficultySettings(4, 12, 0, 0, 1, 9, 6),
        _ => new DifficultySettings(2, 10, 1, 8, 1, 3, 4),
    };

    private int Search(
        BoardState board,
        ulong boardHash,
        Stone sideToMove,
        Stone aiSide,
        int depth,
        int quiescenceDepth,
        int alpha,
        int beta,
        int maxCandidates)
    {
        if (board.IsFull())
        {
            return 0;
        }

        if (depth <= 0)
        {
            return QuiescenceSearch(board, boardHash, sideToMove, aiSide, quiescenceDepth, alpha, beta, maxCandidates);
        }

        var alphaStart = alpha;
        var betaStart = beta;
        var key = new PositionKey(boardHash, sideToMove, depth);
        var hasCachedEntry = transpositionTable.TryGetValue(key, out var cached);
        if (hasCachedEntry)
        {
            if (cached.Flag == TranspositionFlag.Exact)
            {
                return cached.Score;
            }

            if (cached.Flag == TranspositionFlag.LowerBound)
            {
                alpha = Math.Max(alpha, cached.Score);
            }
            else if (cached.Flag == TranspositionFlag.UpperBound)
            {
                beta = Math.Min(beta, cached.Score);
            }

            if (alpha >= beta)
            {
                return cached.Score;
            }
        }

        var candidates = GetCandidateMoves(board, sideToMove, Math.Max(6, maxCandidates - 2));
        if (candidates.Count == 0)
        {
            return PatternScorer.EvaluateBoard(board, aiSide);
        }

        if (hasCachedEntry && cached.BestRow >= 0)
        {
            PromotePreferredMove(candidates, cached.BestRow, cached.BestColumn);
        }

        var bestRow = candidates[0].Row;
        var bestColumn = candidates[0].Column;

        if (sideToMove == aiSide)
        {
            var value = int.MinValue;
            foreach (var candidate in candidates)
            {
                board.SetStone(candidate.Row, candidate.Column, sideToMove);
                var childHash = boardHash ^ GetStoneHash(candidate.Row, candidate.Column, sideToMove);
                var score = RulesEvaluator.IsWinningMove(board, candidate.Row, candidate.Column)
                    ? WinScore + depth
                    : Search(board, childHash, sideToMove.Opponent(), aiSide, depth - 1, quiescenceDepth, alpha, beta, maxCandidates);
                board.SetStone(candidate.Row, candidate.Column, Stone.None);

                if (score > value)
                {
                    value = score;
                    bestRow = candidate.Row;
                    bestColumn = candidate.Column;
                }

                alpha = Math.Max(alpha, value);
                if (alpha >= beta)
                {
                    break;
                }
            }

            StoreTransposition(key, value, alphaStart, betaStart, bestRow, bestColumn);
            return value;
        }

        var minValue = int.MaxValue;
        foreach (var candidate in candidates)
        {
            board.SetStone(candidate.Row, candidate.Column, sideToMove);
            var childHash = boardHash ^ GetStoneHash(candidate.Row, candidate.Column, sideToMove);
            var score = RulesEvaluator.IsWinningMove(board, candidate.Row, candidate.Column)
                ? -WinScore - depth
                : Search(board, childHash, sideToMove.Opponent(), aiSide, depth - 1, quiescenceDepth, alpha, beta, maxCandidates);
            board.SetStone(candidate.Row, candidate.Column, Stone.None);

            if (score < minValue)
            {
                minValue = score;
                bestRow = candidate.Row;
                bestColumn = candidate.Column;
            }

            beta = Math.Min(beta, minValue);
            if (alpha >= beta)
            {
                break;
            }
        }

        StoreTransposition(key, minValue, alphaStart, betaStart, bestRow, bestColumn);
        return minValue;
    }

    private int QuiescenceSearch(
        BoardState board,
        ulong boardHash,
        Stone sideToMove,
        Stone aiSide,
        int depth,
        int alpha,
        int beta,
        int maxCandidates)
    {
        var immediateWins = GetImmediateWinningMoves(board, sideToMove);
        if (immediateWins.Count > 0)
        {
            return sideToMove == aiSide ? WinScore : -WinScore;
        }

        var standPat = PatternScorer.EvaluateBoard(board, aiSide);
        if (depth <= 0)
        {
            return standPat;
        }

        var tacticalMoves = GetTacticalMoves(board, sideToMove, 4);
        if (tacticalMoves.Count == 0)
        {
            return standPat;
        }

        if (sideToMove == aiSide)
        {
            var value = standPat;
            alpha = Math.Max(alpha, value);
            foreach (var candidate in tacticalMoves)
            {
                board.SetStone(candidate.Row, candidate.Column, sideToMove);
                var childHash = boardHash ^ GetStoneHash(candidate.Row, candidate.Column, sideToMove);
                var score = RulesEvaluator.IsWinningMove(board, candidate.Row, candidate.Column)
                    ? WinScore
                    : QuiescenceSearch(board, childHash, sideToMove.Opponent(), aiSide, depth - 1, alpha, beta, maxCandidates);
                board.SetStone(candidate.Row, candidate.Column, Stone.None);

                value = Math.Max(value, score);
                alpha = Math.Max(alpha, value);
                if (alpha >= beta)
                {
                    break;
                }
            }

            return value;
        }

        var minValue = standPat;
        beta = Math.Min(beta, minValue);
        foreach (var candidate in tacticalMoves)
        {
            board.SetStone(candidate.Row, candidate.Column, sideToMove);
            var childHash = boardHash ^ GetStoneHash(candidate.Row, candidate.Column, sideToMove);
            var score = RulesEvaluator.IsWinningMove(board, candidate.Row, candidate.Column)
                ? -WinScore
                : QuiescenceSearch(board, childHash, sideToMove.Opponent(), aiSide, depth - 1, alpha, beta, maxCandidates);
            board.SetStone(candidate.Row, candidate.Column, Stone.None);

            minValue = Math.Min(minValue, score);
            beta = Math.Min(beta, minValue);
            if (alpha >= beta)
            {
                break;
            }
        }

        return minValue;
    }

    private AiMove? FindForcedWinMove(
        BoardState board,
        ulong boardHash,
        Stone aiSide,
        DifficultySettings settings,
        IReadOnlyList<AiMove> orderedCandidates)
    {
        if (settings.ForcedSearchDepth <= 0)
        {
            return null;
        }

        var searchLimit = Math.Min(settings.ForcedSearchCandidates, orderedCandidates.Count);
        for (var index = 0; index < searchLimit; index++)
        {
            var candidate = orderedCandidates[index];
            board.SetStone(candidate.Row, candidate.Column, aiSide);
            var childHash = boardHash ^ GetStoneHash(candidate.Row, candidate.Column, aiSide);

            var isForcedWin = RulesEvaluator.IsWinningMove(board, candidate.Row, candidate.Column)
                || HasForcedWin(board, childHash, aiSide.Opponent(), aiSide, settings.ForcedSearchDepth - 1, settings.ForcedSearchCandidates);
            board.SetStone(candidate.Row, candidate.Column, Stone.None);

            if (isForcedWin)
            {
                return candidate with { Score = WinScore - index };
            }
        }

        return null;
    }

    private bool HasForcedWin(BoardState board, ulong boardHash, Stone sideToMove, Stone attacker, int depth, int maxCandidates)
    {
        if (depth <= 0 || board.IsFull())
        {
            return false;
        }

        var attackerWinningMoves = GetImmediateWinningMoves(board, attacker);
        if (attackerWinningMoves.Count > 0)
        {
            if (sideToMove == attacker)
            {
                return true;
            }

            if (attackerWinningMoves.Count >= 2)
            {
                return true;
            }

            var forcedBlock = attackerWinningMoves[0];
            board.SetStone(forcedBlock.Row, forcedBlock.Column, sideToMove);
            var blockedHash = boardHash ^ GetStoneHash(forcedBlock.Row, forcedBlock.Column, sideToMove);
            var stillWinning = HasForcedWin(board, blockedHash, attacker, attacker, depth - 1, maxCandidates);
            board.SetStone(forcedBlock.Row, forcedBlock.Column, Stone.None);
            return stillWinning;
        }

        if (sideToMove != attacker)
        {
            return false;
        }

        var cacheKey = new ForcedSearchKey(boardHash, sideToMove, depth);
        if (forcedSearchCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var forcingMoves = GetForcingAttackMoves(board, attacker, maxCandidates);
        foreach (var move in forcingMoves)
        {
            board.SetStone(move.Row, move.Column, attacker);
            var childHash = boardHash ^ GetStoneHash(move.Row, move.Column, attacker);
            var wins = RulesEvaluator.IsWinningMove(board, move.Row, move.Column)
                || HasForcedWin(board, childHash, attacker.Opponent(), attacker, depth - 1, maxCandidates);
            board.SetStone(move.Row, move.Column, Stone.None);

            if (wins)
            {
                forcedSearchCache[cacheKey] = true;
                return true;
            }
        }

        forcedSearchCache[cacheKey] = false;
        return false;
    }

    private void StoreTransposition(PositionKey key, int score, int alphaStart, int betaStart, int bestRow, int bestColumn)
    {
        if (transpositionTable.Count >= MaxTranspositionEntries)
        {
            return;
        }

        var flag = score <= alphaStart
            ? TranspositionFlag.UpperBound
            : score >= betaStart
                ? TranspositionFlag.LowerBound
                : TranspositionFlag.Exact;
        transpositionTable[key] = new TranspositionEntry(score, flag, bestRow, bestColumn);
    }

    private static void PromotePreferredMove(List<AiMove> candidates, int row, int column)
    {
        for (var index = 1; index < candidates.Count; index++)
        {
            if (candidates[index].Row != row || candidates[index].Column != column)
            {
                continue;
            }

            var preferred = candidates[index];
            candidates.RemoveAt(index);
            candidates.Insert(0, preferred);
            return;
        }
    }

    private static ulong ComputeBoardHash(BoardState board)
    {
        var hash = 0UL;
        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                var stone = board.GetStone(row, column);
                if (stone != Stone.None)
                {
                    hash ^= GetStoneHash(row, column, stone);
                }
            }
        }

        return hash;
    }

    private static ulong GetStoneHash(int row, int column, Stone stone)
    {
        return ZobristTable[row, column, (int)stone];
    }

    private static ulong[,,] CreateZobristTable()
    {
        var table = new ulong[BoardState.Size, BoardState.Size, 3];
        var state = 0x9E3779B97F4A7C15UL;

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                table[row, column, (int)Stone.Black] = NextRandom(ref state);
                table[row, column, (int)Stone.White] = NextRandom(ref state);
            }
        }

        return table;
    }

    private static ulong NextRandom(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static List<AiMove> GetCandidateMoves(BoardState board, Stone sideToMove, int limit)
    {
        var winningMoves = GetImmediateWinningMoves(board, sideToMove);
        if (winningMoves.Count > 0)
        {
            return winningMoves;
        }

        var candidates = new List<AiMove>();
        var opponent = sideToMove.Opponent();
        var blockingMoves = GetImmediateWinningMoves(board, opponent);
        if (blockingMoves.Count > 0)
        {
            return RankForcedBlocks(board, sideToMove, blockingMoves, limit);
        }

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != Stone.None)
                {
                    continue;
                }

                if (board.StoneCount > 0 && !board.HasNeighbor(row, column))
                {
                    continue;
                }

                var score = EvaluateCandidate(board, row, column, sideToMove, opponent);
                candidates.Add(new AiMove(row, column, score));
            }
        }

        candidates.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        if (candidates.Count > limit)
        {
            candidates.RemoveRange(limit, candidates.Count - limit);
        }

        return candidates;
    }

    private static List<AiMove> GetTacticalMoves(BoardState board, Stone sideToMove, int limit)
    {
        var winningMoves = GetImmediateWinningMoves(board, sideToMove);
        if (winningMoves.Count > 0)
        {
            return winningMoves;
        }

        var opponent = sideToMove.Opponent();
        var blockingMoves = GetImmediateWinningMoves(board, opponent);
        if (blockingMoves.Count > 0)
        {
            return RankForcedBlocks(board, sideToMove, blockingMoves, limit);
        }

        var candidates = new List<AiMove>();
        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != Stone.None)
                {
                    continue;
                }

                if (board.StoneCount > 0 && !board.HasNeighbor(row, column))
                {
                    continue;
                }

                var attack = PatternScorer.AnalyzeMove(board, row, column, sideToMove);
                var defense = PatternScorer.AnalyzeMove(board, row, column, opponent);
                if (!IsTacticalMove(attack) && !IsTacticalMove(defense))
                {
                    continue;
                }

                var score = EvaluateCandidate(board, row, column, sideToMove, opponent);
                candidates.Add(new AiMove(row, column, score));
            }
        }

        candidates.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        if (candidates.Count > limit)
        {
            candidates.RemoveRange(limit, candidates.Count - limit);
        }

        return candidates;
    }

    private static List<AiMove> GetForcingAttackMoves(BoardState board, Stone attacker, int limit)
    {
        var opponent = attacker.Opponent();
        var moves = new List<AiMove>();

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != Stone.None)
                {
                    continue;
                }

                if (board.StoneCount > 0 && !board.HasNeighbor(row, column))
                {
                    continue;
                }

                var attack = PatternScorer.AnalyzeMove(board, row, column, attacker);
                if (!attack.IsWinningMove && attack.WinningThreats == 0 && attack.OpenFours == 0 && attack.SimpleFours == 0)
                {
                    continue;
                }

                var score = EvaluateCandidate(board, row, column, attacker, opponent);
                moves.Add(new AiMove(row, column, score));
            }
        }

        moves.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        if (moves.Count > limit)
        {
            moves.RemoveRange(limit, moves.Count - limit);
        }

        return moves;
    }

    private static bool IsTacticalMove(PatternScorer.MoveAnalysis analysis)
    {
        return analysis.IsWinningMove
            || analysis.WinningThreats > 0
            || analysis.OpenFours > 0
            || analysis.SimpleFours > 0
            || analysis.OpenThrees > 0;
    }

    private static int EvaluateCandidate(BoardState board, int row, int column, Stone sideToMove, Stone opponent)
    {
        var attack = PatternScorer.AnalyzeMove(board, row, column, sideToMove);
        var defense = PatternScorer.AnalyzeMove(board, row, column, opponent);
        var centerBias = 14 - (Math.Abs(CenterIndex - row) + Math.Abs(CenterIndex - column));

        board.SetStone(row, column, sideToMove);
        var immediateWin = RulesEvaluator.IsWinningMove(board, row, column);
        var nextWinningMoves = immediateWin ? 1 : CountImmediateWinningMoves(board, sideToMove, 2);
        var opponentWinningMoves = CountImmediateWinningMoves(board, opponent, 2);
        board.SetStone(row, column, Stone.None);

        var score = attack.Score + (int)(defense.Score * 1.18) + centerBias;

        if (immediateWin || attack.IsWinningMove)
        {
            score += WinScore;
        }

        if (defense.IsWinningMove || defense.Score >= PatternScorer.FiveInRowScore)
        {
            score += WinScore / 2;
        }

        score += ScoreThreatShape(attack, isDefensive: false);
        score += ScoreThreatShape(defense, isDefensive: true);

        score += nextWinningMoves switch
        {
            >= 2 => ThreatForkBonus,
            1 => SingleThreatBonus,
            _ => 0,
        };

        if (opponentWinningMoves > 0)
        {
            score -= UnsafeMovePenalty * opponentWinningMoves;
        }

        return score;
    }

    private static int ScoreThreatShape(PatternScorer.MoveAnalysis analysis, bool isDefensive)
    {
        var multiplier = isDefensive ? 1.2 : 1.0;
        var score = 0;

        score += (int)(analysis.OpenFours * 180_000 * multiplier);
        score += (int)(analysis.SimpleFours * 60_000 * multiplier);
        score += (int)(analysis.OpenThrees * 24_000 * multiplier);
        score += (int)(analysis.BrokenThrees * 7_000 * multiplier);

        if (analysis.WinningThreats >= 2 || analysis.OpenFours + analysis.SimpleFours + analysis.OpenThrees >= 2)
        {
            score += isDefensive ? DefensiveThreatBonus : ThreatForkBonus / 2;
        }

        return score;
    }

    private static List<AiMove> RankForcedBlocks(BoardState board, Stone sideToMove, IReadOnlyCollection<AiMove> blockingMoves, int limit)
    {
        var ranked = new List<AiMove>();
        var opponent = sideToMove.Opponent();

        foreach (var move in blockingMoves)
        {
            var score = EvaluateCandidate(board, move.Row, move.Column, sideToMove, opponent) + (WinScore / 3);
            ranked.Add(new AiMove(move.Row, move.Column, score));
        }

        ranked.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        if (ranked.Count > limit)
        {
            ranked.RemoveRange(limit, ranked.Count - limit);
        }

        return ranked;
    }

    private static List<AiMove> GetImmediateWinningMoves(BoardState board, Stone sideToMove)
    {
        var winningMoves = new List<AiMove>();

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != Stone.None)
                {
                    continue;
                }

                if (board.StoneCount > 0 && !board.HasNeighbor(row, column))
                {
                    continue;
                }

                board.SetStone(row, column, sideToMove);
                var isWinningMove = RulesEvaluator.IsWinningMove(board, row, column);
                board.SetStone(row, column, Stone.None);

                if (isWinningMove)
                {
                    var centerBias = 14 - (Math.Abs(CenterIndex - row) + Math.Abs(CenterIndex - column));
                    winningMoves.Add(new AiMove(row, column, WinScore + centerBias));
                }
            }
        }

        winningMoves.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        return winningMoves;
    }

    private static int CountImmediateWinningMoves(BoardState board, Stone sideToMove, int limit)
    {
        var count = 0;

        for (var row = 0; row < BoardState.Size; row++)
        {
            for (var column = 0; column < BoardState.Size; column++)
            {
                if (board.GetStone(row, column) != Stone.None)
                {
                    continue;
                }

                if (board.StoneCount > 0 && !board.HasNeighbor(row, column))
                {
                    continue;
                }

                board.SetStone(row, column, sideToMove);
                var isWinningMove = RulesEvaluator.IsWinningMove(board, row, column);
                board.SetStone(row, column, Stone.None);

                if (!isWinningMove)
                {
                    continue;
                }

                count++;
                if (count >= limit)
                {
                    return count;
                }
            }
        }

        return count;
    }

    private readonly record struct DifficultySettings(
        int Depth,
        int MaxCandidates,
        int ExtraDepth,
        int ExtraDepthThreshold,
        int QuiescenceDepth,
        int ForcedSearchDepth,
        int ForcedSearchCandidates)
    {
        public int GetEffectiveDepth(int stoneCount)
        {
            return stoneCount <= ExtraDepthThreshold ? Depth + ExtraDepth : Depth;
        }
    }

    private readonly record struct PositionKey(ulong Hash, Stone SideToMove, int Depth);

    private readonly record struct ForcedSearchKey(ulong Hash, Stone SideToMove, int Depth);

    private readonly record struct TranspositionEntry(int Score, TranspositionFlag Flag, int BestRow, int BestColumn);

    private enum TranspositionFlag
    {
        Exact,
        LowerBound,
        UpperBound,
    }
}
