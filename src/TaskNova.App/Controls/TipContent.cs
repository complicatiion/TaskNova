using Avalonia;
using Avalonia.Controls.Primitives;

namespace TaskNova.App.Controls;

/// <summary>Tooltip of a symbol-only button: the action in bold, a short description below.</summary>
public class TipContent : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TipContent, string?>(nameof(Title));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TipContent, string?>(nameof(Text));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
