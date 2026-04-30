using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GomokuApp.AI;
using GomokuApp.Core;
using GomokuApp.Models;

namespace GomokuApp;

public partial class MainWindow : Window
{
    private const double CellSize = 38;
    private const double BoardMargin = 24;
    private readonly GameSession session = new();
    private readonly AiEngine aiEngine = new();
    private bool isAiThinking;

    public MainWindow()
    {
        InitializeComponent();
        InitializeSelections();
        session.StartNewGame(Stone.White);
        RefreshUi();
    }

    private async void BoardCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        if (!TryGetBoardCoordinate(e.GetPosition(BoardCanvas), out var row, out var column))
        {
            return;
        }

        if (session.Mode == GameMode.Setup)
        {
            session.SetSetupStone(row, column, GetSelectedStone(EditStoneCombo, Stone.Black));
            RefreshUi();
            return;
        }

        if (!session.TryHumanMove(row, column, out var error))
        {
            StatusTextBlock.Text = error;
            return;
        }

        RefreshUi();
        if (session.IsGameOver)
        {
            ShowGameOutcome();
            return;
        }

        await RunAiTurnAsync();
    }

    private void DifficultyCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        session.Difficulty = GetSelectedDifficulty();
        RefreshUi();
    }

    private void EnterSetupModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        session.EnterSetupMode();
        RefreshUi();
    }

    private void ClearBoardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        session.ClearBoardForSetup();
        RefreshUi();
    }

    private async void StartFromPositionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        session.Difficulty = GetSelectedDifficulty();
        var nextTurn = GetSelectedStone(NextTurnCombo, Stone.Black);
        var aiSide = GetSelectedStone(SetupAiSideCombo, Stone.White);
        if (!session.StartGameFromCurrentPosition(nextTurn, aiSide, out var error))
        {
            StatusTextBlock.Text = error;
            return;
        }

        RefreshUi();
        if (session.CanAiMove)
        {
            await RunAiTurnAsync();
        }
    }

    private void UndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        if (!session.UndoLastRound())
        {
            StatusTextBlock.Text = "当前没有可悔棋的回合。";
            return;
        }

        RefreshUi();
    }

    private async void RestartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        session.Restart();
        RefreshUi();
        if (session.CanAiMove)
        {
            await RunAiTurnAsync();
        }
    }

    private void NewPlayerFirstButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        session.Difficulty = GetSelectedDifficulty();
        session.StartNewGame(Stone.White);
        RefreshUi();
    }

    private async void NewAiFirstButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        session.Difficulty = GetSelectedDifficulty();
        session.StartNewGame(Stone.Black);
        RefreshUi();
        await RunAiTurnAsync();
    }

    private async Task RunAiTurnAsync()
    {
        if (!session.CanAiMove)
        {
            return;
        }

        isAiThinking = true;
        RefreshUi();

        var snapshot = session.Board.Clone();
        var aiSide = session.AiSide;
        var difficulty = session.Difficulty;
        var move = await Task.Run(() => aiEngine.FindBestMove(snapshot, aiSide, difficulty));

        isAiThinking = false;
        if (!session.TryApplyAiMove(move.Row, move.Column, out var error))
        {
            StatusTextBlock.Text = error;
            RefreshUi();
            return;
        }

        RefreshUi();
        if (session.IsGameOver)
        {
            ShowGameOutcome();
        }
    }

    private void InitializeSelections()
    {
        DifficultyCombo.SelectedIndex = 1;
        EditStoneCombo.SelectedIndex = 0;
        NextTurnCombo.SelectedIndex = 0;
        SetupAiSideCombo.SelectedIndex = 1;
        session.Difficulty = GetSelectedDifficulty();
    }

    private void RefreshUi()
    {
        DrawBoard();
        UpdateStatus();
        UndoButton.IsEnabled = !isAiThinking && session.CanUndo;
        RestartButton.IsEnabled = !isAiThinking && session.Mode == GameMode.Playing;
    }

    private void UpdateStatus()
    {
        if (isAiThinking)
        {
            StatusTextBlock.Text = $"AI（{session.AiSide.ToDisplayName()}）思考中...";
            return;
        }

        if (session.Mode == GameMode.Setup)
        {
            var tool = GetSelectedStone(EditStoneCombo, Stone.Black).ToDisplayName();
            if (tool == Stone.None.ToDisplayName())
            {
                tool = "空位";
            }

            if (session.IsGameOver && session.Winner != Stone.None)
            {
                StatusTextBlock.Text = $"残局编辑模式：当前局面已经由{session.Winner.ToDisplayName()}获胜。若要继续对战，请先调整局面。";
                return;
            }

            StatusTextBlock.Text = $"残局编辑模式：点击棋盘放置“{tool}”。开始对战时，可指定下一手方和 AI 执方。";
            return;
        }

        if (session.IsGameOver)
        {
            StatusTextBlock.Text = session.Winner == Stone.None
                ? "对局结束：平局。"
                : $"对局结束：{session.Winner.ToDisplayName()}获胜。";
            return;
        }

        var actor = session.CurrentTurn == session.AiSide ? "AI" : "你";
        StatusTextBlock.Text = $"对战模式：轮到{actor}（{session.CurrentTurn.ToDisplayName()}）落子。当前 AI 执{session.AiSide.ToDisplayName()}，难度为{GetDifficultyDisplayName(session.Difficulty)}。";
    }

    private void DrawBoard()
    {
        BoardCanvas.Children.Clear();
        DrawGridLines();
        DrawStarPoints();

        foreach (var (row, column, stone) in session.Board.OccupiedCells())
        {
            DrawStone(row, column, stone);
        }

        if (session.LastMove is { } lastMove)
        {
            DrawLastMoveMarker(lastMove.Row, lastMove.Column);
        }
    }

    private void DrawGridLines()
    {
        for (var index = 0; index < BoardState.Size; index++)
        {
            var offset = BoardMargin + (index * CellSize);

            BoardCanvas.Children.Add(new Line
            {
                X1 = BoardMargin,
                Y1 = offset,
                X2 = BoardMargin + ((BoardState.Size - 1) * CellSize),
                Y2 = offset,
                Stroke = new SolidColorBrush(Color.FromRgb(96, 62, 19)),
                StrokeThickness = 1,
            });

            BoardCanvas.Children.Add(new Line
            {
                X1 = offset,
                Y1 = BoardMargin,
                X2 = offset,
                Y2 = BoardMargin + ((BoardState.Size - 1) * CellSize),
                Stroke = new SolidColorBrush(Color.FromRgb(96, 62, 19)),
                StrokeThickness = 1,
            });
        }
    }

    private void DrawStarPoints()
    {
        var points = new[]
        {
            (3, 3),
            (3, 11),
            (7, 7),
            (11, 3),
            (11, 11),
        };

        foreach (var (row, column) in points)
        {
            var center = ToCanvasPoint(row, column);
            var star = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(68, 44, 16)),
            };

            Canvas.SetLeft(star, center.X - (star.Width / 2));
            Canvas.SetTop(star, center.Y - (star.Height / 2));
            BoardCanvas.Children.Add(star);
        }
    }

    private void DrawStone(int row, int column, Stone stone)
    {
        var center = ToCanvasPoint(row, column);
        var brush = new RadialGradientBrush();
        if (stone == Stone.Black)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(97, 97, 97), 0.1));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(18, 18, 18), 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0.1));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(218, 218, 218), 1));
        }

        var stoneShape = new Ellipse
        {
            Width = CellSize - 6,
            Height = CellSize - 6,
            Fill = brush,
            Stroke = stone == Stone.Black
                ? new SolidColorBrush(Color.FromRgb(15, 15, 15))
                : new SolidColorBrush(Color.FromRgb(122, 122, 122)),
            StrokeThickness = 1.2,
        };

        Canvas.SetLeft(stoneShape, center.X - (stoneShape.Width / 2));
        Canvas.SetTop(stoneShape, center.Y - (stoneShape.Height / 2));
        BoardCanvas.Children.Add(stoneShape);
    }

    private void DrawLastMoveMarker(int row, int column)
    {
        var center = ToCanvasPoint(row, column);
        var marker = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = Brushes.IndianRed,
            StrokeThickness = 2,
        };

        Canvas.SetLeft(marker, center.X - (marker.Width / 2));
        Canvas.SetTop(marker, center.Y - (marker.Height / 2));
        BoardCanvas.Children.Add(marker);
    }

    private Point ToCanvasPoint(int row, int column)
    {
        return new Point(BoardMargin + (column * CellSize), BoardMargin + (row * CellSize));
    }

    private bool TryGetBoardCoordinate(Point point, out int row, out int column)
    {
        column = (int)Math.Round((point.X - BoardMargin) / CellSize);
        row = (int)Math.Round((point.Y - BoardMargin) / CellSize);

        if (!session.Board.IsInside(row, column))
        {
            return false;
        }

        var target = ToCanvasPoint(row, column);
        if (Math.Abs(target.X - point.X) > CellSize / 2 || Math.Abs(target.Y - point.Y) > CellSize / 2)
        {
            return false;
        }

        return true;
    }

    private static Stone GetSelectedStone(ComboBox comboBox, Stone fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && Enum.TryParse<Stone>(tag, out var value))
        {
            return value;
        }

        return fallback;
    }

    private AiDifficulty GetSelectedDifficulty()
    {
        if (DifficultyCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && Enum.TryParse<AiDifficulty>(tag, out var difficulty))
        {
            return difficulty;
        }

        return AiDifficulty.Normal;
    }

    private static string GetDifficultyDisplayName(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => "简单",
        AiDifficulty.Normal => "普通",
        AiDifficulty.Hard => "困难",
        AiDifficulty.Master => "大师",
        _ => "普通",
    };

    private void ShowGameOutcome()
    {
        var message = session.Winner == Stone.None
            ? "本局平局。"
            : session.Winner == session.AiSide
                ? $"AI 执{session.AiSide.ToDisplayName()}获胜。"
                : $"你执{session.HumanSide.ToDisplayName()}获胜。";

        MessageBox.Show(this, message, "对局结束", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshUi();
    }
}
