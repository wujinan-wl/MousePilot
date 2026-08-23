using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MousePilot.ViewModels;

namespace MousePilot.Views;

public partial class CursorEditorWindow : Window
{
    public CursorEditorWindow(CursorEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose(); // 釋放 VM 快取的原圖 Bitmap
        viewModel.CloseRequested += () =>
        {
            DialogResult = true;
            Close();
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CursorEditorViewModel.HotspotX)
                or nameof(CursorEditorViewModel.HotspotY)
                or nameof(CursorEditorViewModel.SelectedSize)
                or nameof(CursorEditorViewModel.PreviewImage))
            {
                DrawHotspotMarker();
            }
        };
    }

    private CursorEditorViewModel Vm => (CursorEditorViewModel)DataContext;

    private void OnSourceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CursorSourceItem item })
        {
            Vm.SelectedSource = item;
        }
    }

    private void OnEditImageClicked(object sender, MouseButtonEventArgs e)
    {
        if (!Vm.CanEditProcessing)
        {
            return; // .cur/.ani 原樣使用，不可改 hotspot
        }

        var pos = e.GetPosition(EditImage);
        var (x, y) = HotspotMath.DisplayToPixel(pos.X, pos.Y, EditImage.ActualWidth, Vm.SelectedSize);
        Vm.SetHotspot(x, y);
    }

    /// <summary>十字準心標記 + 尺寸 ≤ 48 時畫座標格線（規格 §9）。</summary>
    private void DrawHotspotMarker()
    {
        HotspotOverlay.Children.Clear();
        var display = 256.0;
        var size = Vm.SelectedSize;
        if (Vm.PreviewImage is null || size <= 0)
        {
            return;
        }

        if (size <= 48)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
            for (var i = 1; i < size; i++)
            {
                var offset = i * display / size;
                HotspotOverlay.Children.Add(new Line { X1 = offset, Y1 = 0, X2 = offset, Y2 = display, Stroke = gridBrush, StrokeThickness = 1 });
                HotspotOverlay.Children.Add(new Line { X1 = 0, Y1 = offset, X2 = display, Y2 = offset, Stroke = gridBrush, StrokeThickness = 1 });
            }
        }

        var cx = HotspotMath.PixelToDisplayCenter(Vm.HotspotX, display, size);
        var cy = HotspotMath.PixelToDisplayCenter(Vm.HotspotY, display, size);
        var cross = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        HotspotOverlay.Children.Add(new Line { X1 = cx - 10, Y1 = cy, X2 = cx + 10, Y2 = cy, Stroke = cross, StrokeThickness = 2 });
        HotspotOverlay.Children.Add(new Line { X1 = cx, Y1 = cy - 10, X2 = cx, Y2 = cy + 10, Stroke = cross, StrokeThickness = 2 });
    }
}
