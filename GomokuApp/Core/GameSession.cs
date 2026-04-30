using GomokuApp.Models;

namespace GomokuApp.Core;

public sealed class GameSession
{
    private readonly List<MoveRecord> moveHistory = new();
    private BoardState initialBoard = new();
    private Stone initialTurn = Stone.Black;

    public GameSession()
    {
        Board = new BoardState();
        Mode = GameMode.Playing;
        CurrentTurn = Stone.Black;
        AiSide = Stone.White;
        Difficulty = AiDifficulty.Normal;
    }

    public BoardState Board { get; private set; }

    public GameMode Mode { get; private set; }

    public Stone CurrentTurn { get; private set; }

    public Stone AiSide { get; private set; }

    public Stone HumanSide => AiSide.Opponent();

    public AiDifficulty Difficulty { get; set; }

    public bool IsGameOver { get; private set; }

    public Stone Winner { get; private set; }

    public MoveRecord? LastMove { get; private set; }

    public bool CanAiMove => Mode == GameMode.Playing && !IsGameOver && CurrentTurn == AiSide;

    public bool CanUndo => Mode == GameMode.Playing && moveHistory.Count > 0;

    public void StartNewGame(Stone aiSide)
    {
        Board = new BoardState();
        Mode = GameMode.Playing;
        CurrentTurn = Stone.Black;
        AiSide = aiSide;
        Winner = Stone.None;
        IsGameOver = false;
        LastMove = null;
        moveHistory.Clear();
        initialBoard = Board.Clone();
        initialTurn = Stone.Black;
    }

    public void EnterSetupMode()
    {
        Mode = GameMode.Setup;
        Winner = Stone.None;
        IsGameOver = false;
    }

    public void ClearBoardForSetup()
    {
        Mode = GameMode.Setup;
        Board = new BoardState();
        Winner = Stone.None;
        IsGameOver = false;
        LastMove = null;
    }

    public void SetSetupStone(int row, int column, Stone stone)
    {
        if (Mode != GameMode.Setup)
        {
            return;
        }

        Board.SetStone(row, column, stone);
        LastMove = stone == Stone.None ? null : new MoveRecord(row, column, stone);

        var outcome = RulesEvaluator.Evaluate(Board);
        Winner = outcome.Winner;
        IsGameOver = outcome.Winner != Stone.None || outcome.IsDraw;
    }

    public bool StartGameFromCurrentPosition(Stone nextTurn, Stone aiSide, out string? error)
    {
        error = null;
        var outcome = RulesEvaluator.Evaluate(Board);
        if (outcome.Winner != Stone.None)
        {
            error = $"当前残局已经由{outcome.Winner.ToDisplayName()}获胜，不能继续开始。";
            return false;
        }

        if (outcome.IsDraw)
        {
            error = "当前棋盘已无可落子位置。";
            return false;
        }

        Mode = GameMode.Playing;
        CurrentTurn = nextTurn;
        AiSide = aiSide;
        Winner = Stone.None;
        IsGameOver = false;
        moveHistory.Clear();
        initialBoard = Board.Clone();
        initialTurn = nextTurn;
        return true;
    }

    public bool TryHumanMove(int row, int column, out string? error)
    {
        error = null;
        if (Mode != GameMode.Playing)
        {
            error = "当前是残局编辑模式，先点击“从当前残局开始对战”。";
            return false;
        }

        if (IsGameOver)
        {
            error = "对局已结束，请重新开始。";
            return false;
        }

        if (CurrentTurn != HumanSide)
        {
            error = "当前轮到 AI 落子。";
            return false;
        }

        return TryApplyMove(row, column, HumanSide, out error);
    }

    public bool TryApplyAiMove(int row, int column, out string? error)
    {
        error = null;
        if (!CanAiMove)
        {
            error = "当前不轮到 AI 落子。";
            return false;
        }

        return TryApplyMove(row, column, AiSide, out error);
    }

    public bool UndoLastRound()
    {
        if (!CanUndo)
        {
            return false;
        }

        var movesToRemove = Math.Min(2, moveHistory.Count);
        moveHistory.RemoveRange(moveHistory.Count - movesToRemove, movesToRemove);
        RebuildFromInitialPosition();
        return true;
    }

    public void Restart()
    {
        Board = initialBoard.Clone();
        Mode = GameMode.Playing;
        CurrentTurn = initialTurn;
        Winner = Stone.None;
        IsGameOver = false;
        LastMove = null;
        moveHistory.Clear();
    }

    private bool TryApplyMove(int row, int column, Stone stone, out string? error)
    {
        error = null;
        if (!Board.IsInside(row, column))
        {
            error = "落子超出棋盘范围。";
            return false;
        }

        if (!Board.PlaceStone(row, column, stone))
        {
            error = "该位置已有棋子。";
            return false;
        }

        var move = new MoveRecord(row, column, stone);
        moveHistory.Add(move);
        LastMove = move;

        var outcome = RulesEvaluator.Evaluate(Board);
        if (outcome.Winner != Stone.None)
        {
            Winner = outcome.Winner;
            IsGameOver = true;
            return true;
        }

        if (outcome.IsDraw)
        {
            Winner = Stone.None;
            IsGameOver = true;
            return true;
        }

        CurrentTurn = stone.Opponent();
        return true;
    }

    private void RebuildFromInitialPosition()
    {
        Board = initialBoard.Clone();
        Mode = GameMode.Playing;
        CurrentTurn = initialTurn;
        Winner = Stone.None;
        IsGameOver = false;
        LastMove = null;

        foreach (var move in moveHistory)
        {
            Board.PlaceStone(move.Row, move.Column, move.Stone);
            CurrentTurn = move.Stone.Opponent();
            LastMove = move;
        }

        var outcome = RulesEvaluator.Evaluate(Board);
        Winner = outcome.Winner;
        IsGameOver = outcome.Winner != Stone.None || outcome.IsDraw;
    }
}