namespace SharpAgent.WinForms;

internal static class Theme
{
    // Spacing
    public const int GutterLarge = 20;
    public const int Gutter = 16;
    public const int GutterSmall = 12;
    public const int SpacingSmall = 8;
    public const int SpacingXs = 4;

    // Sizing
    public static readonly Size IconButtonSize = new(36, 36);
    public static readonly Size ButtonSize = new(90, 36);
    public const int CornerRadiusLarge = 16;
    public const int CornerRadius = 12;
    public const int CornerRadiusSmall = 8;
    public const int HeaderHeight = 56;
    public const int InputPanelHeight = 120;

    // Colors - Background
    public static readonly Color Background = SystemColors.Window;
    public static readonly Color BackgroundSecondary = SystemColors.Control;
    public static readonly Color BackgroundTertiary = SystemColors.ControlLight;
    public static readonly Color HeaderStart = SystemColors.Control;
    public static readonly Color HeaderEnd = SystemColors.Control;
    public static readonly Color BorderSubtle = SystemColors.ControlDark;

    // Colors - Interactive
    public static readonly Color AccentPrimary = SystemColors.Highlight;
    public static readonly Color AccentPrimaryHover = ControlPaint.Light(SystemColors.Highlight);
    public static readonly Color AccentPrimaryPressed = ControlPaint.Dark(SystemColors.Highlight);
    public static readonly Color ButtonDefault = SystemColors.ControlLight;
    public static readonly Color ButtonHover = SystemColors.ControlLightLight;
    public static readonly Color ButtonPressed = SystemColors.ControlDark;
    public static readonly Color FocusRing = SystemColors.Highlight;
    public static readonly Color Error = Color.FromArgb(220, 60, 60);
    public static readonly Color ErrorHover = Color.FromArgb(240, 80, 80);

    // Colors - Text
    public static readonly Color TextPrimary = SystemColors.ControlText;
    public static readonly Color TextSecondary = SystemColors.GrayText;
    public static readonly Color TextMuted = SystemColors.GrayText;
    public static readonly Color TextDisabled = SystemColors.GrayText;

    // Typography
    public static readonly Font FontLarge = new("Segoe UI", 14, FontStyle.Bold);
    public static readonly Font FontMedium = new("Segoe UI", 11);
    public static readonly Font FontRegular = new("Segoe UI", 10);
    public static readonly Font FontSmall = new("Segoe UI", 9);
    public static readonly Font FontMono = new("Cascadia Code", 9.5f);
}
