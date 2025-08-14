using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Emgu.CV;
using Emgu.CV.Structure;
using Intel.RealSense;


namespace IP6;

public class VisualizerWindow : Window
{
    private Image _display0;
    private Image _display1;
    private Image _display2;

    public VisualizerWindow()
    {
        var panel = new StackPanel { };

        _display0 = new Image { Width = 320, Height = 240, Margin = new Thickness(5) };
        _display1 = new Image { Width = 320, Height = 240, Margin = new Thickness(5) };
        _display2 = new Image { Width = 320, Height = 240, Margin = new Thickness(5) };

        panel.Children.Add(_display0);
        panel.Children.Add(_display1);
        panel.Children.Add(_display2);

        Content = panel;
    }

    public void SetDisplayBitmap(Bitmap bitmap, int id)
    {
        switch (id)
        {
            case 0:
                _display0.Source = bitmap;
                break;
            case 1:
                _display1.Source = bitmap;
                break;
            case 2:
                _display2.Source = bitmap;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), "ID must be 0, 1, or 2.");
        }
    }

    public void SetWithImage(Image<Gray, byte> image, int id)
    {
        SetDisplayBitmap(new Bitmap(
            PixelFormats.Gray8,
            AlphaFormat.Opaque,
            image.MIplImage.ImageData,
            new PixelSize(image.Width, image.Height),
            new Vector(96,96),
            image.MIplImage.WidthStep),id);
    }
    
    

    public void SetWithVideoFrame(VideoFrame frame, int id)
    {
        
        int width = frame.Width;
        int height = frame.Height;
        Bitmap bitmap = null; 
        try
        {
            bitmap = new Bitmap(
                frame.Stride == frame.Width?PixelFormats.Gray8:PixelFormats.Bgr24,             
                AlphaFormat.Opaque,             
                frame.Data,                     
                new PixelSize(width, height),   
                new Vector(96, 96),             
                frame.Stride                    
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Failed to create Bitmap for frame ID {id}.");
            Console.Error.WriteLine($"Exception Type: {ex.GetType().Name}");
            Console.Error.WriteLine($"Message: {ex.Message}");
            Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
            return;
        }
        SetDisplayBitmap(bitmap, id);
    }
    
}
