namespace GomokuApp.Sudoku;

public sealed class SudokuCell
{
    private readonly bool[] notes = new bool[10];

    public int GivenValue { get; internal set; }
    public int PlayerValue { get; internal set; }
    public bool IsGiven => GivenValue != 0;
    public int DisplayValue => IsGiven ? GivenValue : PlayerValue;
    public bool HasWrongValue { get; internal set; }
    public IReadOnlyList<bool> Notes => notes;

    internal void ClearNotes()
    {
        Array.Clear(notes);
    }

    internal void ToggleNote(int value)
    {
        if (value is < 1 or > 9)
        {
            return;
        }

        notes[value] = !notes[value];
    }

    internal void ClearNoteValue(int value)
    {
        if (value is < 1 or > 9)
        {
            return;
        }

        notes[value] = false;
    }
}
