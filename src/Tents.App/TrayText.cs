using Tents.Shell.Tray;

namespace Tents.App;

/// <summary>The tray icon's tooltip and menu. Pure, so the menu's wording and state are tested.</summary>
internal static class TrayText
{
    public const int NewTentCommand = 1;
    public const int PauseCommand = 2;
    public const int SettingsCommand = 3;
    public const int ExitCommand = 4;

    public static string Tooltip(bool paused) => paused ? "Tents (paused)" : "Tents";

    /// <summary>
    /// The menu. Pause is a checkbox named for what it does: ticked, the Windows desktop icons show and the tents are
    /// hidden. Settings is a placeholder until the M2 settings window.
    /// </summary>
    public static IReadOnlyList<TrayMenuItem> Menu(bool paused) =>
    [
        new(NewTentCommand, "&New tent", Enabled: !paused),
        new(PauseCommand, "&Show Windows desktop icons", Checked: paused),
        TrayMenuItem.Separator,
        new(SettingsCommand, "Settings (coming later)", Enabled: false),
        TrayMenuItem.Separator,
        new(ExitCommand, "E&xit Tents"),
    ];
}
