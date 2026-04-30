# Project Memory

This file preserves the working context for future sessions, because the chat history may not be available later.

## Project

- Repository: WPF Gomoku application in `GomokuApp`.
- Target: `.NET 10.0 Windows`, WPF.
- Main UI: `GomokuApp/MainWindow.xaml` and `GomokuApp/MainWindow.xaml.cs`.
- Game core: `GomokuApp/Core`.
- AI core: `GomokuApp/AI/AiEngine.cs` and `GomokuApp/AI/PatternScorer.cs`.

## Current User Goal

The user wants a much stronger built-in Gomoku AI, especially for custom/endgame positions. They are willing to use ideas from GitHub/open-source Gomoku AI projects, but project code should remain locally maintainable and not blindly copy unknown-license code.

## Current AI State

The AI has been upgraded from a basic alpha-beta evaluator to a stronger tactical engine:

- Difficulty levels now include `Easy`, `Normal`, `Hard`, and `Master`.
- `Master` mode is available in the UI difficulty selector.
- `PatternScorer` recognizes stronger Gomoku tactical shapes:
  - direct five
  - open four
  - simple/sleeping four
  - open three
  - broken three
  - double threats
- `AiEngine` uses alpha-beta search with:
  - candidate move ordering
  - forced win pre-search
  - quiescence/tactical extension at leaf nodes
  - transposition table
  - incremental Zobrist hashing
  - in-place make/unmake move instead of cloning every branch
- The forced search is intended to approximate VCF-style forced attack sequences, especially continuous forcing moves where the defender has only one required block.

## Important Files

- `GomokuApp/AI/AiEngine.cs`
  - `FindBestMove`
  - `Search`
  - `QuiescenceSearch`
  - `FindForcedWinMove`
  - `HasForcedWin`
  - `GetForcingAttackMoves`
  - Zobrist hash helpers
- `GomokuApp/AI/PatternScorer.cs`
  - `AnalyzeMove`
  - `MoveAnalysis`
  - threat-shape scoring
- `GomokuApp/Models/Enums.cs`
  - `AiDifficulty.Master`
- `GomokuApp/MainWindow.xaml`
  - difficulty selector includes "大师"

## Validation Already Done

The project builds successfully:

```powershell
dotnet build .\GomokuApp\GomokuApp.csproj
```

Smoke tests were run with a temporary console project. These cases passed:

- finish open four
- block open four
- finish jump four
- block jump four

In one tested midgame, the forced search found a winning continuation quickly. Complex positions without forcing tactics may still take several seconds in `Master`.

## GitHub/Open-Source Algorithm Notes

Useful directions seen in stronger Gomoku AI projects and articles:

- alpha-beta / negamax with strong move ordering
- Zobrist hashing and transposition tables
- threat-space search
- VCF/VCT style forced-line search
- opening books
- MCTS/neural approaches for much larger future work

References checked during the session:

- https://github.com/JiachenRen/GomokuZero
- https://github.com/rdragon/gomoku-ai
- https://www.baeldung.com/cs/gomoku-threat-space-search

## Next Improvement Ideas

Good next steps:

1. Add a dedicated VCT search, not only VCF-like forcing.
2. Add time budgeting/cancellation for `Master`, so the UI never waits too long.
3. Add a small opening book for the first 6-10 moves.
4. Add regression tests for tactical positions instead of temporary smoke-test console apps.
5. Improve threat classification for overlines/renju禁手 if rule variants are later needed.
6. Consider a separate engine project or test project so AI can be unit-tested without WPF.

## User Preference

The user wants stronger chess strength more than instant move speed, but the app should remain usable. `Master` may think longer; `Hard` should stay responsive.
