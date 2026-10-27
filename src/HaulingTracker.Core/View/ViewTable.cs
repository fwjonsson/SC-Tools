namespace HaulingTracker.View;

/// <summary>A column of a table of the web view: its header, how its cells are aligned (left, center, right), and whether it is tinted (calculated, not typed).</summary>
public sealed record ViewColumn(string Text, string Align = "center", bool Calc = true);

/// <summary>The header of a column as the page gets it.</summary>
public sealed record ViewHeader(string Text, string Align);

/// <summary>A row as the page gets it: the text of every cell and its colours ("#rrggbb"), and whether it is bold (a Total row).</summary>
public sealed class ViewRow
{
    public string[] Cells { get; init; } = Array.Empty<string>();
    public string[] Back { get; init; } = Array.Empty<string>();
    public string[] Fore { get; init; } = Array.Empty<string>();
    public bool Bold { get; set; }

    /// <summary>Gives this cell its own colour (and text colour).</summary>
    public ViewRow Color(int column, int back, int? fore = null)
    {
        Back[column] = ViewPalette.Hex(back);
        if (fore is int f) Fore[column] = ViewPalette.Hex(f);
        return this;
    }

    /// <summary>Gives the whole row a colour.</summary>
    public ViewRow RowColor(int back, int? fore = null)
    {
        for (var i = 0; i < Back.Length; i++) Color(i, back, fore);
        return this;
    }
}

/// <summary>A table of the live web view: the same columns, texts and colours as in the program.</summary>
public sealed class ViewTable
{
    readonly ViewPalette _p;
    readonly ViewColumn[] _columns;

    public ViewTable(ViewPalette palette, params ViewColumn[] columns)
    {
        _p = palette;
        _columns = columns;
        Headers = columns.Select(c => new ViewHeader(c.Text, c.Align)).ToList();
    }

    public List<ViewHeader> Headers { get; }
    public List<ViewRow> Rows { get; } = new();

    /// <summary>Adds a row: tinted columns get the calculated colour, the others the table colour; the text has the theme's colour.</summary>
    public ViewRow Add(params string[] cells)
    {
        var row = new ViewRow
        {
            Cells = cells,
            Back = _columns.Select(c => ViewPalette.Hex(c.Calc ? _p.Calc : _p.Cell)).ToArray(),
            Fore = _columns.Select(_ => ViewPalette.Hex(_p.Text)).ToArray(),
        };
        Rows.Add(row);
        return row;
    }

    /// <summary>Numbers the first `count` rows 1, 2, 3... in the "#" column (the rest, like a Total row, stay empty).</summary>
    public ViewTable Number(int column = 0, int? count = null)
    {
        var n = count ?? Rows.Count;
        for (var i = 0; i < Rows.Count; i++) Rows[i].Cells[column] = i < n ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        return this;
    }
}
