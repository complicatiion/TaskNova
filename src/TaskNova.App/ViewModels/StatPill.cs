using CommunityToolkit.Mvvm.ComponentModel;
using TaskNova.App.Localization;

namespace TaskNova.App.ViewModels;

/// <summary>
/// One key figure of a page toolbar shown as a pill: the value (accent, bold) and its label ("270 Processes").
/// The objects are created once per page and only the value changes, so the pills are never rebuilt.
/// </summary>
public sealed partial class StatPill : ObservableObject
{
    private readonly string _labelKey;

    public StatPill(string labelKey)
    {
        _labelKey = labelKey;
        Loc.LanguageChanged += () => OnPropertyChanged(nameof(Label));
    }

    [ObservableProperty]
    private string _value = "–";

    public string Label => Loc.T(_labelKey);

    public void Set(int value) => Value = value.ToString("N0", Core.Formatting.Fmt.Culture);
}
