using Avalonia;
using Avalonia.Controls;

namespace TaskNova.App.Views;

public partial class DetailsView : UserControl
{
    public DetailsView()
    {
        InitializeComponent();

        // The wide table scrolls horizontally: the header follows the rows.
        List.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            if (e.Source is ScrollViewer rows && !ReferenceEquals(rows, HeadScroll))
            {
                HeadScroll.Offset = new Vector(rows.Offset.X, 0);
            }
        });
    }
}
