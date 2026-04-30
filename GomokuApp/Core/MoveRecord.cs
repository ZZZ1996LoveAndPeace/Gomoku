using GomokuApp.Models;

namespace GomokuApp.Core;

public readonly record struct MoveRecord(int Row, int Column, Stone Stone);