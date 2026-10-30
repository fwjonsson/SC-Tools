namespace SCTools.View;

/// <summary>A column of a table of the web view: its header and how its cells are aligned (left, center, right).</summary>
public sealed record ViewColumn(string Text, string Align = "center");

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
    string? _back, _fore;   // the cells' colours as #rrggbb, made once per table

    public ViewTable(ViewPalette palette, params ViewColumn[] columns)
    {
        _p = palette;
        _columns = columns;
        Headers = columns.Select(c => new ViewHeader(c.Text, c.Align)).ToList();
    }

    public List<ViewHeader> Headers { get; }
    public List<ViewRow> Rows { get; } = new();

    /// <summary>
    /// Adds a row: every cell the colour of the tables (Calc; nothing is typed on the page, so no column looks different, unlike the program where the columns
    /// you type in are not tinted); the text has the theme's colour.
    /// </summary>
    public ViewRow Add(params string[] cells)
    {
        _back ??= ViewPalette.Hex(_p.Calc);
        _fore ??= ViewPalette.Hex(_p.Text);
        var back = new string[_columns.Length];
        var fore = new string[_columns.Length];
        Array.Fill(back, _back);
        Array.Fill(fore, _fore);
        var row = new ViewRow { Cells = cells, Back = back, Fore = fore };
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
