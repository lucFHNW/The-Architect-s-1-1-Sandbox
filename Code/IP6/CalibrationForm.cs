using System.Drawing;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Brushes = Avalonia.Media.Brushes;
using Point = Avalonia.Point;

namespace IP6
{
    public class CalibrationWindow : Window
    {
        private DateTime _lastPoint = DateTime.Now;
        private int _p = 1;
        private Canvas _canvas;
        private double _width;
        private double _height;
        private double _radius;
        private bool _uiSet = false;

        public CalibrationWindow(Window parent)
        {
            IP6.IrCamera.Calibrated = false;
            Width = parent.Bounds.Width;
            Height = parent.Bounds.Height;
            Position = parent.Position;
            SystemDecorations = SystemDecorations.Full;
            Topmost = true;
            Background = Brushes.Black;
            _canvas = new Canvas
            {
                Background = Avalonia.Media.Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
            Content = _canvas;
            _canvas.AttachedToVisualTree += async (_, _) =>
            {
                await Task.Yield(); 
                _width = _canvas.Bounds.Width;
                _height = _canvas.Bounds.Height;
                if (_uiSet) return;
                _uiSet = true;
                TriggerStart();
                InvalidateVisual();
            };
        }

        void TriggerStart()
        {
            _radius = 10;
            Point position = new Point(_width / 2, _height / 2);
            var ellipse = new Ellipse
            {
                Width = _radius * 2,
                Height = _radius * 2,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(ellipse, position.X - _radius);
            Canvas.SetTop(ellipse, position.Y - _radius);
            _canvas.Children.Add(ellipse);
            Dispatcher.UIThread.Post(InvalidateVisual);
            
            IP6.IrCamera.CameraEvent += OnCameraEvent;
        }

        private void OnCameraEvent(object sender, IrCamera.IrCameraEventArgs e)
        {
            var now = DateTime.Now;
            if ((now - _lastPoint).TotalMilliseconds < 1000)
                return;
            PointF lc = new PointF((float)e.Points[0].X, (float)e.Points[0].Y);
            switch (_p)
            {
                case 1:
                    IP6.IrCamera.Center = UpdatePoint(lc);
                    break;
                case 2:
                    IP6.IrCamera.ObenLinks = UpdatePoint(lc);
                    break;
                case 3:
                    IP6.IrCamera.ObenRechts = UpdatePoint(lc);
                    break;
                case 4:
                    IP6.IrCamera.UntenLinks = UpdatePoint(lc);
                    break;
                case 5:
                    IP6.IrCamera.UntenRechts = UpdatePoint(lc);
                    IP6.IrCamera.DrawingAreaWidth = (float)this.Width;
                    IP6.IrCamera.DrawingAreaHeight = (float)this.Height;
                    IP6.IrCamera.SetCalibrated(true);
                    break;
                default:
                    break;
            }
            if (_p >= 5)
            {
                _p++;
                Dispatcher.UIThread.Post(() => Close());
                return;
            }
            _lastPoint = now;
            Point position = _p switch
            {
                1 => new Point(_radius, _radius),
                2 => new Point(_width - _radius, _radius),
                3 => new Point(_radius, _height - _radius),
                4 => new Point(_width - _radius, _height - _radius),
                _ => new Point(-1, -1)
            };
            var ellipse = new Ellipse
            {
                Width = _radius * 2,
                Height = _radius * 2,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(ellipse, position.X - _radius);
            Canvas.SetTop(ellipse, position.Y - _radius);
            _canvas.Children.Clear();
            _canvas.Children.Add(ellipse);
            _p++;
            Dispatcher.UIThread.Post(InvalidateVisual);
        }

        PointF UpdatePoint(PointF p)
        {
            return new PointF(p.X * IP6.IrCamera.Width, p.Y * IP6.IrCamera.Height);
        }

        
    }
}