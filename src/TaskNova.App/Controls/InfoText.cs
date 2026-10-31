using Avalonia;
using Avalonia.Controls.Primitives;

namespace TaskNova.App.Controls;

/// <summary>Introduction / hint text of a card: small round info symbol in the accent color left of a muted, wrapping text.</summary>
public class InfoText : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<InfoText, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
