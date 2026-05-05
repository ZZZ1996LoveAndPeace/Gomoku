namespace GomokuApp.Minesweeper;

internal static class MinesweeperSolvabilityVerifier
{
    private const int MaxComponentVariables = 24;

    public static bool CanSolve(MinesweeperCell[,] source, int firstRow, int firstColumn)
    {
        var rows = source.GetLength(0);
        var columns = source.GetLength(1);
        var totalMines = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (source[row, column].IsMine)
                {
                    totalMines++;
                }
            }
        }

        var revealed = new bool[rows, columns];
        var flagged = new bool[rows, columns];
        var revealedSafeCells = Reveal(source, revealed, flagged, firstRow, firstColumn);
        var safeCellCount = (rows * columns) - totalMines;

        while (revealedSafeCells < safeCellCount)
        {
            var progress = false;
            var constraints = BuildConstraints(source, revealed, flagged);
            var flaggedMines = Count(flagged);
            var hiddenCells = CountHidden(revealed, flagged);
            var remainingMines = totalMines - flaggedMines;

            if (remainingMines == 0)
            {
                for (var row = 0; row < rows; row++)
                {
                    for (var column = 0; column < columns; column++)
                    {
                        if (!revealed[row, column] && !flagged[row, column])
                        {
                            revealedSafeCells += Reveal(source, revealed, flagged, row, column);
                            progress = true;
                        }
                    }
                }

                if (progress)
                {
                    continue;
                }
            }

            if (remainingMines == hiddenCells)
            {
                for (var row = 0; row < rows; row++)
                {
                    for (var column = 0; column < columns; column++)
                    {
                        if (!revealed[row, column] && !flagged[row, column])
                        {
                            flagged[row, column] = true;
                            progress = true;
                        }
                    }
                }

                if (progress)
                {
                    continue;
                }
            }

            foreach (var constraint in constraints)
            {
                if (constraint.RequiredMines == 0)
                {
                    foreach (var cell in constraint.Cells)
                    {
                        if (!revealed[cell.Row, cell.Column] && !flagged[cell.Row, cell.Column])
                        {
                            revealedSafeCells += Reveal(source, revealed, flagged, cell.Row, cell.Column);
                            progress = true;
                        }
                    }
                }
                else if (constraint.RequiredMines == constraint.Cells.Count)
                {
                    foreach (var cell in constraint.Cells)
                    {
                        if (!flagged[cell.Row, cell.Column])
                        {
                            flagged[cell.Row, cell.Column] = true;
                            progress = true;
                        }
                    }
                }
            }

            if (progress)
            {
                continue;
            }

            var deductions = FindComponentDeductions(constraints);
            foreach (var deduction in deductions)
            {
                if (revealed[deduction.Row, deduction.Column] || flagged[deduction.Row, deduction.Column])
                {
                    continue;
                }

                if (deduction.IsMine)
                {
                    flagged[deduction.Row, deduction.Column] = true;
                }
                else
                {
                    revealedSafeCells += Reveal(source, revealed, flagged, deduction.Row, deduction.Column);
                }

                progress = true;
            }

            if (!progress)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<Deduction> FindComponentDeductions(IReadOnlyList<Constraint> constraints)
    {
        if (constraints.Count == 0)
        {
            return [];
        }

        var variables = constraints
            .SelectMany(static constraint => constraint.Cells)
            .Distinct()
            .ToList();

        var variableIndex = new Dictionary<MinesweeperPosition, int>(variables.Count);
        for (var index = 0; index < variables.Count; index++)
        {
            variableIndex[variables[index]] = index;
        }

        var constraintIndexesByVariable = new List<int>[variables.Count];
        for (var index = 0; index < constraintIndexesByVariable.Length; index++)
        {
            constraintIndexesByVariable[index] = [];
        }

        var constraintVariables = new List<int>[constraints.Count];
        for (var constraintIndex = 0; constraintIndex < constraints.Count; constraintIndex++)
        {
            constraintVariables[constraintIndex] = [];
            foreach (var cell in constraints[constraintIndex].Cells)
            {
                var index = variableIndex[cell];
                constraintVariables[constraintIndex].Add(index);
                constraintIndexesByVariable[index].Add(constraintIndex);
            }
        }

        var deductions = new List<Deduction>();
        var visitedVariables = new bool[variables.Count];
        for (var start = 0; start < variables.Count; start++)
        {
            if (visitedVariables[start])
            {
                continue;
            }

            var componentVariables = new HashSet<int>();
            var componentConstraints = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visitedVariables[start] = true;

            while (queue.Count > 0)
            {
                var variable = queue.Dequeue();
                componentVariables.Add(variable);

                foreach (var constraintIndex in constraintIndexesByVariable[variable])
                {
                    if (componentConstraints.Add(constraintIndex))
                    {
                        foreach (var nextVariable in constraintVariables[constraintIndex])
                        {
                            if (!visitedVariables[nextVariable])
                            {
                                visitedVariables[nextVariable] = true;
                                queue.Enqueue(nextVariable);
                            }
                        }
                    }
                }
            }

            if (componentVariables.Count > MaxComponentVariables)
            {
                continue;
            }

            deductions.AddRange(AnalyzeComponent(
                componentVariables.ToList(),
                componentConstraints.ToList(),
                constraints,
                constraintVariables,
                variables));
        }

        return deductions;
    }

    private static IReadOnlyList<Deduction> AnalyzeComponent(
        List<int> componentVariables,
        List<int> componentConstraints,
        IReadOnlyList<Constraint> constraints,
        IReadOnlyList<List<int>> constraintVariables,
        IReadOnlyList<MinesweeperPosition> variables)
    {
        var localIndex = new Dictionary<int, int>(componentVariables.Count);
        for (var index = 0; index < componentVariables.Count; index++)
        {
            localIndex[componentVariables[index]] = index;
        }

        var localConstraints = new List<LocalConstraint>(componentConstraints.Count);
        foreach (var constraintIndex in componentConstraints)
        {
            var localVariables = constraintVariables[constraintIndex].Select(variable => localIndex[variable]).ToArray();
            localConstraints.Add(new LocalConstraint(localVariables, constraints[constraintIndex].RequiredMines));
        }

        var assignment = new bool[componentVariables.Count];
        var mineAppearances = new int[componentVariables.Count];
        var solutionCount = 0;

        Search(0);
        if (solutionCount == 0)
        {
            return [];
        }

        var deductions = new List<Deduction>();
        for (var index = 0; index < mineAppearances.Length; index++)
        {
            if (mineAppearances[index] == 0)
            {
                var position = variables[componentVariables[index]];
                deductions.Add(new Deduction(position.Row, position.Column, false));
            }
            else if (mineAppearances[index] == solutionCount)
            {
                var position = variables[componentVariables[index]];
                deductions.Add(new Deduction(position.Row, position.Column, true));
            }
        }

        return deductions;

        void Search(int index)
        {
            if (index == assignment.Length)
            {
                if (!AllConstraintsSatisfied(localConstraints, assignment))
                {
                    return;
                }

                solutionCount++;
                for (var i = 0; i < assignment.Length; i++)
                {
                    if (assignment[i])
                    {
                        mineAppearances[i]++;
                    }
                }

                return;
            }

            assignment[index] = false;
            if (CanStillSatisfy(localConstraints, assignment, index + 1))
            {
                Search(index + 1);
            }

            assignment[index] = true;
            if (CanStillSatisfy(localConstraints, assignment, index + 1))
            {
                Search(index + 1);
            }

            assignment[index] = false;
        }
    }

    private static bool CanStillSatisfy(IEnumerable<LocalConstraint> constraints, IReadOnlyList<bool> assignment, int assignedCount)
    {
        foreach (var constraint in constraints)
        {
            var mines = 0;
            var unknown = 0;
            foreach (var variable in constraint.Variables)
            {
                if (variable < assignedCount)
                {
                    if (assignment[variable])
                    {
                        mines++;
                    }
                }
                else
                {
                    unknown++;
                }
            }

            if (mines > constraint.RequiredMines || mines + unknown < constraint.RequiredMines)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllConstraintsSatisfied(IEnumerable<LocalConstraint> constraints, IReadOnlyList<bool> assignment)
    {
        foreach (var constraint in constraints)
        {
            var mines = 0;
            foreach (var variable in constraint.Variables)
            {
                if (assignment[variable])
                {
                    mines++;
                }
            }

            if (mines != constraint.RequiredMines)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<Constraint> BuildConstraints(MinesweeperCell[,] source, bool[,] revealed, bool[,] flagged)
    {
        var rows = source.GetLength(0);
        var columns = source.GetLength(1);
        var constraints = new List<Constraint>();

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!revealed[row, column])
                {
                    continue;
                }

                var required = source[row, column].AdjacentMines;
                var unknown = new List<MinesweeperPosition>();
                foreach (var neighbor in GetNeighbors(rows, columns, row, column))
                {
                    if (flagged[neighbor.Row, neighbor.Column])
                    {
                        required--;
                    }
                    else if (!revealed[neighbor.Row, neighbor.Column])
                    {
                        unknown.Add(neighbor);
                    }
                }

                if (unknown.Count == 0)
                {
                    continue;
                }

                if (required < 0 || required > unknown.Count)
                {
                    return [];
                }

                constraints.Add(new Constraint(unknown, required));
            }
        }

        return constraints;
    }

    private static int Reveal(MinesweeperCell[,] source, bool[,] revealed, bool[,] flagged, int startRow, int startColumn)
    {
        if (source[startRow, startColumn].IsMine || flagged[startRow, startColumn])
        {
            return 0;
        }

        var rows = source.GetLength(0);
        var columns = source.GetLength(1);
        var count = 0;
        var queue = new Queue<MinesweeperPosition>();
        queue.Enqueue(new MinesweeperPosition(startRow, startColumn));

        while (queue.Count > 0)
        {
            var position = queue.Dequeue();
            if (revealed[position.Row, position.Column]
                || flagged[position.Row, position.Column]
                || source[position.Row, position.Column].IsMine)
            {
                continue;
            }

            revealed[position.Row, position.Column] = true;
            count++;

            if (source[position.Row, position.Column].AdjacentMines != 0)
            {
                continue;
            }

            foreach (var neighbor in GetNeighbors(rows, columns, position.Row, position.Column))
            {
                if (!revealed[neighbor.Row, neighbor.Column] && !source[neighbor.Row, neighbor.Column].IsMine)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return count;
    }

    private static IEnumerable<MinesweeperPosition> GetNeighbors(int rows, int columns, int row, int column)
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
                if (nextRow >= 0 && nextRow < rows && nextColumn >= 0 && nextColumn < columns)
                {
                    yield return new MinesweeperPosition(nextRow, nextColumn);
                }
            }
        }
    }

    private static int Count(bool[,] cells)
    {
        var count = 0;
        foreach (var cell in cells)
        {
            if (cell)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountHidden(bool[,] revealed, bool[,] flagged)
    {
        var count = 0;
        for (var row = 0; row < revealed.GetLength(0); row++)
        {
            for (var column = 0; column < revealed.GetLength(1); column++)
            {
                if (!revealed[row, column] && !flagged[row, column])
                {
                    count++;
                }
            }
        }

        return count;
    }

    private sealed record Constraint(IReadOnlyList<MinesweeperPosition> Cells, int RequiredMines);
    private sealed record LocalConstraint(int[] Variables, int RequiredMines);
    private sealed record Deduction(int Row, int Column, bool IsMine);
}
