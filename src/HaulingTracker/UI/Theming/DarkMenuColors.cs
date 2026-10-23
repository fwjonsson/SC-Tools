namespace HaulingTracker.UI.Theming;

/// <summary>Colours of the Options menu in dark mode.</summary>
sealed class DarkMenuColors : ProfessionalColorTable
{
    static Color Back => Theme.Window;
    static Color Hot => Color.FromArgb(78, 78, 86);
    public override Color ToolStripDropDownBackground => Back;
    public override Color ImageMarginGradientBegin => Back;
    public override Color ImageMarginGradientMiddle => Back;
    public override Color ImageMarginGradientEnd => Back;
    public override Color MenuBorder => Theme.Lines;
    public override Color MenuItemBorder => Theme.Lines;
    public override Color MenuItemSelected => Hot;
    public override Color MenuItemSelectedGradientBegin => Hot;
    public override Color MenuItemSelectedGradientEnd => Hot;
    public override Color MenuItemPressedGradientBegin => Hot;
    public override Color MenuItemPressedGradientMiddle => Hot;
    public override Color MenuItemPressedGradientEnd => Hot;
    public override Color MenuStripGradientBegin => Back;
    public override Color MenuStripGradientEnd => Back;
    public override Color SeparatorDark => Theme.Lines;
    public override Color SeparatorLight => Theme.Lines;
    public override Color CheckBackground => Hot;
    public override Color CheckSelectedBackground => Hot;
    public override Color CheckPressedBackground => Hot;
    public override Color ButtonSelectedHighlight => Hot;
    public override Color ButtonSelectedGradientBegin => Hot;
    public override Color ButtonSelectedGradientMiddle => Hot;
    public override Color ButtonSelectedGradientEnd => Hot;
    public override Color ButtonPressedGradientBegin => Hot;
    public override Color ButtonPressedGradientMiddle => Hot;
    public override Color ButtonPressedGradientEnd => Hot;
    public override Color ButtonSelectedBorder => Theme.Lines;
    public override Color ButtonPressedBorder => Theme.Lines;
}
