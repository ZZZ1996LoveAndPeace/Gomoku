using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GomokuApp.AI;
using GomokuApp.Core;
using GomokuApp.Minesweeper;
using GomokuApp.Models;
using GomokuApp.Sudoku;

namespace GomokuApp;

public partial class MainWindow : Window
{
    private enum AppGame
    {
        Gomoku,
        Minesweeper,
        Sudoku,
    }

    private const double CellSize = 38;
    private const double BoardMargin = 24;
    private static readonly Lazy<byte[]> StoneSoundWav = new(GenerateStoneSoundWav);
    private readonly GameSession session = new();
    private readonly AiEngine aiEngine = new();
    private readonly MinesweeperGame minesweeper = new();
    private readonly SudokuGame sudoku = new();
    private readonly DispatcherTimer minesweeperTimer;
    private readonly DispatcherTimer sudokuTimer;
    private bool isAiThinking;
    private bool isMinesweeperGenerating;
    private bool isSudokuGenerating;
    private (int Row, int Column)? _pendingMove;
    private SudokuPosition? selectedSudokuCell;
    private AppGame currentGame = AppGame.Gomoku;
    private DateTime minesweeperStartedAt;
    private DateTime sudokuStartedAt;

    public MainWindow()
    {
        InitializeComponent();
        minesweeperTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        minesweeperTimer.Tick += (_, _) => UpdateMinesweeperCounters();
        sudokuTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        sudokuTimer.Tick += (_, _) => UpdateSudokuCounters();
        InitializeSelections();
        session.StartNewGame(Stone.White);
        RefreshUi();
    }

    private async void BoardCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (currentGame != AppGame.Gomoku)
        {
            return;
        }

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

        // Two-click confirmation: second click on the same pending cell confirms the move.
        if (_pendingMove is { } pending && pending.Row == row && pending.Column == column)
        {
            _pendingMove = null;
            if (!session.TryHumanMove(row, column, out var error))
            {
                StatusTextBlock.Text = error;
                RefreshUi();
                return;
            }

            PlayStoneSound();
            RefreshUi();
            if (session.IsGameOver)
            {
                ShowGameOutcome();
                return;
            }

            await RunAiTurnAsync();
            return;
        }

        // First click (or different cell): update pending marker.
        if (!session.IsGameOver
            && session.Mode == GameMode.Playing
            && session.CurrentTurn != session.AiSide
            && session.Board.GetStone(row, column) == Stone.None)
        {
            _pendingMove = (row, column);
        }
        else
        {
            _pendingMove = null;
        }

        RefreshUi();
    }

    private void DifficultyCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        session.Difficulty = GetSelectedDifficulty();
        RefreshUi();
    }

    private void GameCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        currentGame = GetSelectedGame();
        _pendingMove = null;
        RefreshUi();
    }

    private void MinesweeperDifficultyCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MinesweeperDifficultyCombo.SelectedItem is not MinesweeperDifficulty difficulty)
        {
            return;
        }

        StopMinesweeperTimer();
        minesweeper.Reset(difficulty);
        RefreshUi();
    }

    private async void SudokuDifficultyCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SudokuDifficultyCombo.SelectedItem is not SudokuDifficulty difficulty)
        {
            return;
        }

        await ResetSudokuAsync(difficulty);
    }

    private async void NewSudokuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SudokuDifficultyCombo.SelectedItem is not SudokuDifficulty difficulty)
        {
            difficulty = SudokuDifficulty.Easy;
        }

        await ResetSudokuAsync(difficulty);
    }

    private void SudokuNoteModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        sudoku.IsNoteMode = SudokuNoteModeButton.IsChecked == true;
        RefreshUi();
    }

    private void SudokuNumberButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int value })
        {
            sudoku.SelectedNumber = value;
            ApplySudokuValue(value);
        }
    }

    private void ClearSudokuCellButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplySudokuValue(0);
    }

    private void SudokuHintButton_OnClick(object sender, RoutedEventArgs e)
    {
        StartSudokuTimerIfNeeded();
        var result = sudoku.RevealHint();
        RefreshUi();
        if (result.Message is not null)
        {
            StatusTextBlock.Text = result.Message;
        }
    }

    private async Task ResetSudokuAsync(SudokuDifficulty difficulty)
    {
        isSudokuGenerating = true;
        selectedSudokuCell = null;
        StopSudokuTimer(resetDisplay: true);
        RefreshUi();

        await Task.Run(() => sudoku.Reset(difficulty));

        isSudokuGenerating = false;
        SudokuNoteModeButton.IsChecked = false;
        RefreshUi();
    }

    private void SudokuCell_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SudokuPosition position } || isSudokuGenerating)
        {
            return;
        }

        selectedSudokuCell = position;
        RefreshUi();
    }

    private void NewMinesweeperButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MinesweeperDifficultyCombo.SelectedItem is not MinesweeperDifficulty difficulty)
        {
            difficulty = MinesweeperDifficulty.Beginner;
        }

        isMinesweeperGenerating = false;
        StopMinesweeperTimer();
        minesweeper.Reset(difficulty);
        RefreshUi();
    }

    private async void MinesweeperCell_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MinesweeperPosition position } || isMinesweeperGenerating)
        {
            return;
        }

        MinesweeperRevealResult result;
        if (minesweeper.Status == MinesweeperGameStatus.WaitingForFirstReveal)
        {
            isMinesweeperGenerating = true;
            RefreshUi();
            result = await Task.Run(() => minesweeper.Reveal(position.Row, position.Column));
            isMinesweeperGenerating = false;
        }
        else
        {
            result = minesweeper.Reveal(position.Row, position.Column);
        }

        if (result.GeneratedBoard)
        {
            StartMinesweeperTimer();
        }

        if (result.HitMine)
        {
            StopMinesweeperTimer();
            minesweeper.RevealAllMines();
        }
        else if (minesweeper.Status == MinesweeperGameStatus.Won)
        {
            StopMinesweeperTimer();
        }

        RefreshUi();
        if (result.Message is not null)
        {
            StatusTextBlock.Text = result.Message;
        }
    }

    private void MinesweeperCell_OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: MinesweeperPosition position } || isMinesweeperGenerating)
        {
            return;
        }

        if (!minesweeper.ToggleFlag(position.Row, position.Column, out var message) && message is not null)
        {
            StatusTextBlock.Text = message;
            return;
        }

        RefreshUi();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (currentGame != AppGame.Sudoku || isSudokuGenerating)
        {
            return;
        }

        var value = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => 0,
        };

        if (value != 0)
        {
            sudoku.SelectedNumber = value;
            ApplySudokuValue(value);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Back or Key.Delete or Key.D0 or Key.NumPad0)
        {
            ApplySudokuValue(0);
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            sudoku.IsNoteMode = !sudoku.IsNoteMode;
            SudokuNoteModeButton.IsChecked = sudoku.IsNoteMode;
            RefreshUi();
            e.Handled = true;
        }
    }

    private void EnterSetupModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        _pendingMove = null;
        session.EnterSetupMode();
        RefreshUi();
    }

    private void ClearBoardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        _pendingMove = null;
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

        _pendingMove = null;
        RefreshUi();
    }

    private async void RestartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isAiThinking)
        {
            return;
        }

        _pendingMove = null;
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

        _pendingMove = null;
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

        _pendingMove = null;
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

        _pendingMove = null;
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

        PlayStoneSound();
        RefreshUi();
        if (session.IsGameOver)
        {
            ShowGameOutcome();
        }
    }

    private void InitializeSelections()
    {
        GameCombo.SelectedIndex = 0;
        DifficultyCombo.SelectedIndex = 1;
        EditStoneCombo.SelectedIndex = 0;
        NextTurnCombo.SelectedIndex = 0;
        SetupAiSideCombo.SelectedIndex = 1;
        MinesweeperDifficultyCombo.ItemsSource = MinesweeperDifficulty.All;
        MinesweeperDifficultyCombo.SelectedIndex = 0;
        SudokuDifficultyCombo.ItemsSource = SudokuDifficulty.All;
        SudokuDifficultyCombo.SelectedIndex = 0;
        InitializeSudokuNumberPad();
        session.Difficulty = GetSelectedDifficulty();
    }

    private void InitializeSudokuNumberPad()
    {
        SudokuNumberPad.Children.Clear();
        for (var value = 1; value <= 9; value++)
        {
            var button = new Button
            {
                Tag = value,
                Content = value.ToString(),
                Margin = new Thickness(3),
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
            };
            button.Click += SudokuNumberButton_OnClick;
            SudokuNumberPad.Children.Add(button);
        }
    }

    private void RefreshUi()
    {
        var showingGomoku = currentGame == AppGame.Gomoku;
        var showingMinesweeper = currentGame == AppGame.Minesweeper;
        var showingSudoku = currentGame == AppGame.Sudoku;
        GomokuBoardViewbox.Visibility = showingGomoku ? Visibility.Visible : Visibility.Collapsed;
        MinesweeperBoardViewbox.Visibility = showingMinesweeper ? Visibility.Visible : Visibility.Collapsed;
        SudokuBoardViewbox.Visibility = showingSudoku ? Visibility.Visible : Visibility.Collapsed;
        GomokuControlsPanel.Visibility = showingGomoku ? Visibility.Visible : Visibility.Collapsed;
        GomokuSetupPanel.Visibility = showingGomoku ? Visibility.Visible : Visibility.Collapsed;
        MinesweeperControlsPanel.Visibility = showingMinesweeper ? Visibility.Visible : Visibility.Collapsed;
        SudokuControlsPanel.Visibility = showingSudoku ? Visibility.Visible : Visibility.Collapsed;

        if (showingGomoku)
        {
            GameTitleTextBlock.Text = "五子棋";
            GameSubtitleTextBlock.Text = "支持从空局开始对战，也支持手动摆残局后指定 AI 执黑或执白继续下。";
            DrawBoard();
            UpdateStatus();
            UndoButton.IsEnabled = !isAiThinking && session.CanUndo;
            RestartButton.IsEnabled = !isAiThinking && session.Mode == GameMode.Playing;
            return;
        }

        if (showingMinesweeper)
        {
            GameTitleTextBlock.Text = "扫雷";
            GameSubtitleTextBlock.Text = "左键开格，右键插旗；第一步会打开空白区域，棋盘生成后已通过无猜验证。";
            DrawMinesweeperBoard();
            UpdateMinesweeperStatus();
            UpdateMinesweeperCounters();
            return;
        }

        GameTitleTextBlock.Text = "数独";
        GameSubtitleTextBlock.Text = "选中格子后输入数字；可切换笔记模式记录候选数，题目保证唯一解。";
        DrawSudokuBoard();
        UpdateSudokuStatus();
        UpdateSudokuCounters();
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
        var baseStatus = $"对战模式：轮到{actor}（{session.CurrentTurn.ToDisplayName()}）落子。当前 AI 执{session.AiSide.ToDisplayName()}，难度为{GetDifficultyDisplayName(session.Difficulty)}。";
        if (_pendingMove.HasValue && session.CurrentTurn != session.AiSide)
        {
            StatusTextBlock.Text = baseStatus + "已预选落点，再次点击同一位置确认，或点击其他空位重选。";
        }
        else
        {
            StatusTextBlock.Text = baseStatus;
        }
    }

    private void UpdateMinesweeperStatus()
    {
        if (isMinesweeperGenerating)
        {
            StatusTextBlock.Text = "正在生成可纯推理解答的棋盘...";
            return;
        }

        StatusTextBlock.Text = minesweeper.Status switch
        {
            MinesweeperGameStatus.WaitingForFirstReveal => "扫雷：点击任意格开始。",
            MinesweeperGameStatus.Playing => $"扫雷：{minesweeper.Difficulty.Name}，已打开 {minesweeper.RevealedCount}/{minesweeper.SafeCellCount} 个安全格。",
            MinesweeperGameStatus.Won => $"扫雷：胜利。该棋盘生成验证用了 {minesweeper.GenerationAttempts} 次尝试。",
            MinesweeperGameStatus.Lost => "扫雷：踩雷，本局结束。",
            _ => "扫雷",
        };
    }

    private void UpdateMinesweeperCounters()
    {
        MinesRemainingTextBlock.Text = Math.Max(0, minesweeper.MineCount - minesweeper.FlaggedCount).ToString("000");
        var elapsed = minesweeperTimer.IsEnabled
            ? DateTime.UtcNow - minesweeperStartedAt
            : TimeSpan.Zero;

        if (minesweeper.Status is MinesweeperGameStatus.Playing)
        {
            MinesweeperTimerTextBlock.Text = Math.Min(999, (int)elapsed.TotalSeconds).ToString("000");
        }
        else if (minesweeper.Status == MinesweeperGameStatus.WaitingForFirstReveal)
        {
            MinesweeperTimerTextBlock.Text = "000";
        }
    }

    private void StartMinesweeperTimer()
    {
        minesweeperStartedAt = DateTime.UtcNow;
        minesweeperTimer.Start();
        UpdateMinesweeperCounters();
    }

    private void StopMinesweeperTimer()
    {
        minesweeperTimer.Stop();
        UpdateMinesweeperCounters();
    }

    private void UpdateSudokuStatus()
    {
        if (isSudokuGenerating)
        {
            StatusTextBlock.Text = "正在生成唯一解数独...";
            return;
        }

        if (sudoku.Status == SudokuGameStatus.Won)
        {
            StatusTextBlock.Text = $"数独：完成。难度 {sudoku.Difficulty.Name}，使用了 {sudoku.HintCount} 次提示。";
            return;
        }

        var selected = selectedSudokuCell is { } position
            ? $"已选第 {position.Row + 1} 行第 {position.Column + 1} 列。"
            : "请选择一个格子。";
        var mode = sudoku.IsNoteMode ? "笔记模式" : "填数模式";
        StatusTextBlock.Text = $"数独：{sudoku.Difficulty.Name}，{mode}。{selected}";
    }

    private void UpdateSudokuCounters()
    {
        SudokuMistakesTextBlock.Text = $"{sudoku.Mistakes}/{sudoku.Difficulty.MaxMistakes}";
        if (sudoku.Status == SudokuGameStatus.Playing && sudokuTimer.IsEnabled)
        {
            var elapsed = DateTime.UtcNow - sudokuStartedAt;
            SudokuTimerTextBlock.Text = Math.Min(999, (int)elapsed.TotalSeconds).ToString("000");
        }
        else if (!sudokuTimer.IsEnabled && sudoku.Status != SudokuGameStatus.Won)
        {
            SudokuTimerTextBlock.Text = "000";
        }
    }

    private void StartSudokuTimerIfNeeded()
    {
        if (sudokuTimer.IsEnabled || sudoku.Status == SudokuGameStatus.Won)
        {
            return;
        }

        sudokuStartedAt = DateTime.UtcNow;
        sudokuTimer.Start();
        UpdateSudokuCounters();
    }

    private void StopSudokuTimer(bool resetDisplay)
    {
        sudokuTimer.Stop();
        if (resetDisplay)
        {
            SudokuTimerTextBlock.Text = "000";
        }
    }

    private void ApplySudokuValue(int value)
    {
        if (selectedSudokuCell is not { } position || isSudokuGenerating)
        {
            StatusTextBlock.Text = "请先选择一个数独格子。";
            return;
        }

        StartSudokuTimerIfNeeded();
        var result = sudoku.SetValue(position.Row, position.Column, value);
        if (sudoku.Status == SudokuGameStatus.Won)
        {
            sudokuTimer.Stop();
        }

        RefreshUi();
        if (result.Message is not null)
        {
            StatusTextBlock.Text = result.Message;
        }
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

        if (_pendingMove is { } pendingMove
            && session.Mode == GameMode.Playing
            && !session.IsGameOver
            && session.CurrentTurn != session.AiSide
            && session.Board.GetStone(pendingMove.Row, pendingMove.Column) == Stone.None)
        {
            DrawPendingMarker(pendingMove.Row, pendingMove.Column);
        }

        if (session.LastMove is { } lastMove)
        {
            DrawLastMoveMarker(lastMove.Row, lastMove.Column);
        }
    }

    private void DrawMinesweeperBoard()
    {
        MinesweeperGrid.Children.Clear();
        MinesweeperGrid.Rows = minesweeper.Rows;
        MinesweeperGrid.Columns = minesweeper.Columns;

        var cellSize = Math.Floor(580.0 / Math.Max(minesweeper.Rows, minesweeper.Columns));
        for (var row = 0; row < minesweeper.Rows; row++)
        {
            for (var column = 0; column < minesweeper.Columns; column++)
            {
                var position = new MinesweeperPosition(row, column);
                var cell = minesweeper.GetCell(row, column);
                var button = new Button
                {
                    Tag = position,
                    Width = cellSize,
                    Height = cellSize,
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    Margin = new Thickness(1),
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = Math.Max(12, Math.Min(22, cellSize * 0.48)),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    IsEnabled = !isMinesweeperGenerating && minesweeper.Status is not (MinesweeperGameStatus.Won or MinesweeperGameStatus.Lost),
                    Focusable = false,
                };

                button.Click += MinesweeperCell_OnClick;
                button.PreviewMouseRightButtonDown += MinesweeperCell_OnRightButtonDown;
                StyleMinesweeperCell(button, cell);
                MinesweeperGrid.Children.Add(button);
            }
        }
    }

    private void StyleMinesweeperCell(Button button, MinesweeperCell cell)
    {
        if (cell.IsRevealed)
        {
            button.Background = cell.IsMine
                ? new SolidColorBrush(Color.FromRgb(198, 78, 72))
                : new SolidColorBrush(Color.FromRgb(233, 238, 240));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(168, 180, 186));
            button.Foreground = cell.IsMine
                ? Brushes.White
                : GetMinesweeperNumberBrush(cell.AdjacentMines);
            button.Content = cell.IsMine
                ? "*"
                : cell.AdjacentMines == 0
                    ? ""
                    : cell.AdjacentMines.ToString();
            return;
        }

        button.Background = cell.IsFlagged
            ? new SolidColorBrush(Color.FromRgb(245, 214, 118))
            : new SolidColorBrush(Color.FromRgb(119, 143, 153));
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(81, 102, 111));
        button.Foreground = cell.IsFlagged
            ? new SolidColorBrush(Color.FromRgb(74, 54, 12))
            : Brushes.White;
        button.Content = cell.IsFlagged ? "F" : "";
    }

    private static Brush GetMinesweeperNumberBrush(int adjacentMines) => adjacentMines switch
    {
        1 => new SolidColorBrush(Color.FromRgb(36, 94, 181)),
        2 => new SolidColorBrush(Color.FromRgb(45, 130, 78)),
        3 => new SolidColorBrush(Color.FromRgb(188, 64, 55)),
        4 => new SolidColorBrush(Color.FromRgb(87, 71, 153)),
        5 => new SolidColorBrush(Color.FromRgb(144, 74, 43)),
        6 => new SolidColorBrush(Color.FromRgb(36, 135, 146)),
        7 => new SolidColorBrush(Color.FromRgb(39, 43, 48)),
        8 => new SolidColorBrush(Color.FromRgb(106, 113, 119)),
        _ => Brushes.Transparent,
    };

    private void DrawSudokuBoard()
    {
        SudokuGrid.Children.Clear();
        SudokuGrid.RowDefinitions.Clear();
        SudokuGrid.ColumnDefinitions.Clear();

        for (var index = 0; index < SudokuSolver.Size; index++)
        {
            SudokuGrid.RowDefinitions.Add(new RowDefinition());
            SudokuGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var row = 0; row < SudokuSolver.Size; row++)
        {
            for (var column = 0; column < SudokuSolver.Size; column++)
            {
                var position = new SudokuPosition(row, column);
                var cell = sudoku.GetCell(row, column);
                var button = new Button
                {
                    Tag = position,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    BorderThickness = GetSudokuBorderThickness(row, column),
                    FontSize = 30,
                    FontWeight = cell.IsGiven ? FontWeights.Bold : FontWeights.SemiBold,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Focusable = false,
                    IsEnabled = !isSudokuGenerating,
                };

                button.Click += SudokuCell_OnClick;
                StyleSudokuCell(button, cell, row, column);
                Grid.SetRow(button, row);
                Grid.SetColumn(button, column);
                SudokuGrid.Children.Add(button);
            }
        }

        UpdateSudokuNumberPad();
        SudokuNoteModeButton.IsChecked = sudoku.IsNoteMode;
    }

    private void StyleSudokuCell(Button button, SudokuCell cell, int row, int column)
    {
        var isSelected = selectedSudokuCell is { } selected && selected.Row == row && selected.Column == column;
        var isPeer = selectedSudokuCell is { } peer
            && (peer.Row == row
                || peer.Column == column
                || (peer.Row / 3 == row / 3 && peer.Column / 3 == column / 3));
        var sameValue = selectedSudokuCell is { } selectedPosition
            && sudoku.GetCell(selectedPosition.Row, selectedPosition.Column).DisplayValue is var selectedValue
            && selectedValue != 0
            && cell.DisplayValue == selectedValue;

        button.BorderBrush = new SolidColorBrush(Color.FromRgb(83, 91, 75));
        button.Foreground = cell.IsGiven
            ? new SolidColorBrush(Color.FromRgb(39, 45, 37))
            : cell.HasWrongValue || sudoku.ConflictsWithPeers(row, column)
                ? new SolidColorBrush(Color.FromRgb(190, 58, 52))
                : new SolidColorBrush(Color.FromRgb(42, 91, 161));

        button.Background = isSelected
            ? new SolidColorBrush(Color.FromRgb(244, 213, 136))
            : sameValue
                ? new SolidColorBrush(Color.FromRgb(222, 236, 209))
                : isPeer
                    ? new SolidColorBrush(Color.FromRgb(231, 236, 224))
                    : cell.IsGiven
                        ? new SolidColorBrush(Color.FromRgb(213, 219, 205))
                        : new SolidColorBrush(Color.FromRgb(248, 249, 244));

        if (cell.DisplayValue != 0)
        {
            button.Content = cell.DisplayValue.ToString();
            return;
        }

        button.Content = CreateSudokuNotesContent(cell);
    }

    private static Grid CreateSudokuNotesContent(SudokuCell cell)
    {
        var grid = new Grid { Margin = new Thickness(3) };
        for (var index = 0; index < 3; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var value = 1; value <= 9; value++)
        {
            var text = new TextBlock
            {
                Text = cell.Notes[value] ? value.ToString() : "",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(91, 105, 113)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(text, (value - 1) / 3);
            Grid.SetColumn(text, (value - 1) % 3);
            grid.Children.Add(text);
        }

        return grid;
    }

    private static Thickness GetSudokuBorderThickness(int row, int column)
    {
        var left = column % 3 == 0 ? 2.2 : 0.7;
        var top = row % 3 == 0 ? 2.2 : 0.7;
        var right = column == SudokuSolver.Size - 1 ? 2.2 : 0.7;
        var bottom = row == SudokuSolver.Size - 1 ? 2.2 : 0.7;
        return new Thickness(left, top, right, bottom);
    }

    private void UpdateSudokuNumberPad()
    {
        foreach (var child in SudokuNumberPad.Children)
        {
            if (child is not Button { Tag: int value } button)
            {
                continue;
            }

            var isSelected = sudoku.SelectedNumber == value;
            button.Background = isSelected
                ? new SolidColorBrush(Color.FromRgb(244, 213, 136))
                : new SolidColorBrush(Color.FromRgb(238, 241, 231));
            button.Foreground = new SolidColorBrush(Color.FromRgb(39, 45, 37));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(156, 168, 146));
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

    private void DrawPendingMarker(int row, int column)
    {
        var center = ToCanvasPoint(row, column);
        var brush = new RadialGradientBrush();
        if (session.CurrentTurn == Stone.Black)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(97, 97, 97), 0.1));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(18, 18, 18), 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0.1));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(218, 218, 218), 1));
        }

        var shape = new Ellipse
        {
            Width = CellSize - 6,
            Height = CellSize - 6,
            Fill = brush,
            Stroke = session.CurrentTurn == Stone.Black
                ? new SolidColorBrush(Color.FromRgb(15, 15, 15))
                : new SolidColorBrush(Color.FromRgb(122, 122, 122)),
            StrokeThickness = 1.2,
            Opacity = 0.42,
        };

        Canvas.SetLeft(shape, center.X - (shape.Width / 2));
        Canvas.SetTop(shape, center.Y - (shape.Height / 2));
        BoardCanvas.Children.Add(shape);
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

    private AppGame GetSelectedGame()
    {
        if (GameCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && Enum.TryParse<AppGame>(tag, out var game))
        {
            return game;
        }

        return AppGame.Gomoku;
    }

    private static string GetDifficultyDisplayName(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => "简单",
        AiDifficulty.Normal => "普通",
        AiDifficulty.Hard => "困难",
        AiDifficulty.Master => "大师",
        _ => "普通",
    };

    private static void PlayStoneSound()
    {
        try
        {
            var ms = new MemoryStream(StoneSoundWav.Value, writable: false);
            var player = new SoundPlayer(ms);
            player.Play();
        }
        catch
        {
            // Sound is not critical — ignore any playback failure.
        }
    }

    private static byte[] GenerateStoneSoundWav()
    {
        const int sampleRate = 44100;
        const int durationMs = 75;
        const double baseFreq = 700.0;
        const int sampleCount = sampleRate * durationMs / 1000;

        var samples = new short[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            var decay = Math.Exp(-t * 55.0);
            var wave = Math.Sin(2 * Math.PI * baseFreq * t) * 0.65
                     + Math.Sin(2 * Math.PI * baseFreq * 1.48 * t) * 0.25
                     + Math.Sin(2 * Math.PI * baseFreq * 2.1 * t) * 0.10;
            samples[i] = (short)Math.Clamp(wave * decay * short.MaxValue, short.MinValue, short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + sampleCount * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(sampleCount * 2);
        foreach (var s in samples)
        {
            w.Write(s);
        }

        return ms.ToArray();
    }

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
