namespace HaulingTracker.UI.Tables;

/// <summary>Rows and cells of a table by the names of their columns (see Columns.cs).</summary>
static class GridRows
{
    /// <summary>
    /// Adds a row with the cells it names filled in; the other cells stay empty. The values are in place before the row is added (like Rows.Add with values),
    /// so adding a row raises no CellValueChanged.
    /// </summary>
    public static DataGridViewRow AddRow(this DataGridView grid, params (string Column, object? Value)[] cells)
    {
        var values = new object[grid.Columns.Count];
        foreach (var (column, value) in cells)
        {
            var col = grid.Columns[column] ?? throw new ArgumentException("The table has no column " + column + ".", nameof(cells));
            values[col.Index] = value!;
        }
        return grid.Rows[grid.Rows.Add(values)];
    }

    /// <summary>The name of the column with this index, or "" for the row headers (-1).</summary>
    public static string ColumnName(this DataGridView grid, int index) =>
        index >= 0 && index < grid.Columns.Count ? grid.Columns[index].Name : "";
}
