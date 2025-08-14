using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SharpHook;
using SkiaSharp;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;
using Point = Avalonia.Point;
using SaveFileDialog = Avalonia.Controls.SaveFileDialog;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;

namespace IP6;

public class PaintApp : Window
{
    private bool _isDrawing;
    private Point _lastPoint;
    private Image _drawingImage;
    private WriteableBitmap _writeableBitmap;
    private SKBitmap _skBitmap;
    private SKCanvas _skCanvas;
    private SKColor _currentSkColor = SKColors.Black;
    private double _drawStrokeThickness = 2;
    private double _eraserStrokeThickness = 10;
    private ObservableBool _isErasing = new(false);
    private Grid _toolbar;
    private bool _hasOptionWindowOpen;
    private Window _drawingOptionsWindow;
    private double _unzoomedWidth;
    private double _unzoomedHeight;
    private ScrollViewer _sv;
    private Settings _appSettings = new Settings();

    private enum MoveDirections { Up, Down, Left, Right };

    public PaintApp()
    {
        Title = "IP6";
        Width = 1250;
        Height = 750;
        
        var dockPanel = new DockPanel();

        _toolbar = new Grid
        {
            Background = Brushes.LightGray,
            Height = 50,
            VerticalAlignment = VerticalAlignment.Top
        };

        _toolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _toolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _toolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var leftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var drawB = CreatePngButton("Assets/drawing.png", () => ShowDrawSettings(), "Draw");
        drawB.BorderBrush = Brushes.DodgerBlue;
        var eraseB = CreatePngButton("Assets/eraser.png", () => ShowDrawSettings(isEraser:true), "Eraser");
        _isErasing.ValueChanged += _ =>
        {
            eraseB.BorderBrush = _isErasing.Value ? Brushes.DodgerBlue : Brushes.Transparent;
            drawB.BorderBrush = _isErasing.Value ? Brushes.Transparent : Brushes.DodgerBlue;
        };
        leftPanel.Children.Add(drawB);
        leftPanel.Children.Add(eraseB);

        Grid.SetColumn(leftPanel, 0);
        _toolbar.Children.Add(leftPanel);

        var centerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        centerPanel.Children.Add(CreatePngButton("Assets/upload.png", LoadPdf, "Load PDF"));
        centerPanel.Children.Add(CreatePngButton("Assets/zoom-in.png", ZoomIn, "Zoom In"));
        centerPanel.Children.Add(CreatePngButton("Assets/zoom-out.png", ZoomOut, "Zoom Out"));
        centerPanel.Children.Add(CreatePngButton("Assets/rotate-right.png", () => RotCanvas(angle:90),
            "Rotate content by 90° right"));
        centerPanel.Children.Add(CreatePngButton("Assets/rotate-left.png", () => RotCanvas(angle:-90),
            "Rotate content by 90° left "));
        centerPanel.Children.Add(CreatePngButton("Assets/arrow_left.png", () => Move(MoveDirections.Left),
            "Move the the content to the left"));
        centerPanel.Children.Add(CreatePngButton("Assets/arrow_right.png", () => Move(MoveDirections.Right),
            "Move the the content to the right"));
        centerPanel.Children.Add(CreatePngButton("Assets/arrow_up.png", () => Move(MoveDirections.Up),
            "Move the the content to the top"));
        centerPanel.Children.Add(CreatePngButton("Assets/arrow_down.png", () => Move(MoveDirections.Down),
            "Move the the content to the bottom"));
        centerPanel.Children.Add(CreateButton("reset zoom", ResetZoom));


        Grid.SetColumn(centerPanel, 1);
        _toolbar.Children.Add(centerPanel);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        rightPanel.Children.Add(CreatePngButton("Assets/diskette.png", SaveCanvas, "Save Canvas"));
        rightPanel.Children.Add(CreatePngButton("Assets/trash.png", ClearCanvas, "Clear Canvas"));
        var menu = new Menu();
        menu.Opened += (_, _) => { _hasOptionWindowOpen = true; };
        menu.Closed += (_, _) => { _hasOptionWindowOpen = false; };
        var toolsMenuItem = new MenuItem { Header = "Tools" };

        var calibrateMenuItem = new MenuItem { Header = "Calibrate" };
        calibrateMenuItem.Click += (_, _) => OpenCalibrationForm();
        toolsMenuItem.Items.Add(calibrateMenuItem);

        var toggleDebugWindowMenuItem = new MenuItem { Header = "Show Camera Images" };
        toggleDebugWindowMenuItem.Click += (_, _) => IrCamera.ToggleDebugWindow();
        toolsMenuItem.Items.Add(toggleDebugWindowMenuItem);

        var storeCalibrationMenuItem = new MenuItem { Header = "Store Calibration" };
        storeCalibrationMenuItem.Click += (_, _) => Store();
        toolsMenuItem.Items.Add(storeCalibrationMenuItem);

        var loadCalibrationMenuItem = new MenuItem { Header = "Load Calibration" };
        loadCalibrationMenuItem.Click += (_, _) => Load();
        toolsMenuItem.Items.Add(loadCalibrationMenuItem);

        var settingsMenuItem = new MenuItem { Header = "Settings" };
        settingsMenuItem.Click += (_, _) => Settings();
        toolsMenuItem.Items.Add(settingsMenuItem);

        menu.Items.Add(toolsMenuItem);

        rightPanel.Children.Add(menu);
        Grid.SetColumn(rightPanel, 2);
        _toolbar.Children.Add(rightPanel);

        DockPanel.SetDock(_toolbar, Dock.Top);
        dockPanel.Children.Add(_toolbar);

        _drawingImage = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch.UniformToFill,
            ClipToBounds = true
        };

        InitializeDrawingSurface((int)Width, (int)Height - (int)_toolbar.Height);


        Resized += (_, _) =>
        {
            _unzoomedWidth = Width;
            _unzoomedHeight = Height;
            var temp = _skBitmap.Copy();
            InitializeDrawingSurface((int)Width, (int)Height - (int)_toolbar.Height);
            temp.ScalePixels(_skBitmap, SKSamplingOptions.Default);
            UpdateAvaloniaBitmap();
        };

        _unzoomedWidth = Width;
        _unzoomedHeight = Height;

        _drawingImage.PointerPressed += OnPointerPressed;
        _drawingImage.PointerMoved += OnPointerMoved;
        _drawingImage.PointerReleased += OnPointerReleased;

        IrCamera.CameraEvent += OnIRCameraEvent;

        _sv = new ScrollViewer
        {
            Content = _drawingImage,
            Background = Brushes.White,
            ClipToBounds = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        dockPanel.Children.Add(_sv);
        Content = dockPanel;
        LayoutUpdated += (_, _) =>
        {
            var newWidth = (int)_drawingImage.Bounds.Width;
            var newHeight = (int)_drawingImage.Bounds.Height;
            if (_skBitmap.Width != newWidth || _skBitmap.Height != newHeight)
            {
                InitializeDrawingSurface(newWidth, newHeight);
                ClearCanvas();
            }
        };

        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (_, _) => { CleanPenList(); };
        timer.AutoReset = true;
        timer.Start();

        timer = new System.Timers.Timer(15);
        timer.Elapsed += (_, _) => { UpdateAvaloniaBitmapTest(); };
        timer.AutoReset = true;
        timer.Start();
    }

    private void InitializeDrawingSurface(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _skCanvas?.Dispose();
        _skBitmap?.Dispose();
        _writeableBitmap?.Dispose();
        _writeableBitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        _skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skCanvas = new SKCanvas(_skBitmap);
        _skCanvas.Clear(SKColors.White);
        using (var paint = new SKPaint())
        {
            for (var y = 0; y < height; y += _appSettings.GridWidth) _skCanvas.DrawLine(0f, y, width, y, paint);
            for (var x = 0; x < width; x += _appSettings.GridWidth) _skCanvas.DrawLine(x, 0, x, height, paint);
        }

        _drawingImage.Width = width;
        _drawingImage.Height = height;
        _drawingImage.Source = _writeableBitmap;
        UpdateAvaloniaBitmap();
    }

    private Button CreateButton(string text, Action callback)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(5),
            Padding = new Thickness(5)
        };
        b.Click += (_, _) => callback.Invoke();
        return b;
    }

    private Button CreatePngButton(string imagePath, Action onClick, string tooltip)
    {
        var image = new Image
        {
            Source = new Bitmap(imagePath),
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform
        };

        var button = new Button
        {
            Content = image,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(2),
            Padding = new Thickness(6),
            Width = 40,
            Height = 40
        };

        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => onClick?.Invoke();
        return button;
    }

    private void ClearCanvas()
    {
        InitializeDrawingSurface(_skBitmap.Width, _skBitmap.Height);
        UpdateAvaloniaBitmap();
    }

    private async void SaveCanvas()
    {
        _hasOptionWindowOpen = true;
        var saveFileDialog = new SaveFileDialog
        {
            Filters = { new FileDialogFilter { Name = "PNG", Extensions = { "png" } } },
            DefaultExtension = "png"
        };

        var filePath = await saveFileDialog.ShowAsync(this);
        if (!string.IsNullOrEmpty(filePath))
            using (var image = SKImage.FromBitmap(_skBitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            await using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }

        _hasOptionWindowOpen = false;
    }

    private void LoadPdf()
    {
        Task.Run(async () =>
        {
            _hasOptionWindowOpen = true;
            var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open PDF",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
            });

            if (file.Count > 0)
            {
                var pdfPath = file[0].Path.AbsolutePath;
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { LoadPdfPageAsBackground(pdfPath); });
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine("Error loading PDF: " + ex.Message);
                }
            }

            _hasOptionWindowOpen = false;
        });
    }

    private void LoadPdfPageAsBackground(string pdfPath)
    {
        using var pdfStream = File.OpenRead(pdfPath);
        var originalPdfBitmap = PDFtoImage.Conversion.ToImage(pdfStream);
        var rot = Rotate(originalPdfBitmap, 90);
        rot.ScalePixels(_skBitmap, SKSamplingOptions.Default);
        UpdateAvaloniaBitmap();
    }

    private void RotCanvas(double angle)
    {
        var rot = Rotate(_skBitmap, angle);
        InitializeDrawingSurface(rot.Width, rot.Height);
        _skCanvas.DrawBitmap(rot, 0, 0);
        UpdateAvaloniaBitmap();
    }

    private static SKBitmap Rotate(SKBitmap bitmap, double angle)
    {
        var radians = Math.PI * angle / 180;
        var sine = (float)Math.Abs(Math.Sin(radians));
        var cosine = (float)Math.Abs(Math.Cos(radians));
        var originalWidth = bitmap.Width;
        var originalHeight = bitmap.Height;
        var rotatedWidth = (int)(cosine * originalWidth + sine * originalHeight);
        var rotatedHeight = (int)(cosine * originalHeight + sine * originalWidth);

        var rotatedBitmap = new SKBitmap(rotatedWidth, rotatedHeight);

        using var surface = new SKCanvas(rotatedBitmap);
        surface.Clear();
        surface.Translate(rotatedWidth / 2, rotatedHeight / 2);
        surface.RotateDegrees((float)angle);
        surface.Translate(-originalWidth / 2, -originalHeight / 2);
        surface.DrawBitmap(bitmap, new SKPoint());

        return rotatedBitmap;
    }

    private double _zoom = 1.0;

    private void Zoom(double zoom)
    {
        _zoom += zoom;
        if (_zoom >= 0.95)
        {
            var temp = _skBitmap.Copy();
            InitializeDrawingSurface((int)(_unzoomedWidth * _zoom), (int)(_unzoomedHeight * _zoom));
            temp.ScalePixels(_skBitmap, SKSamplingOptions.Default);
            UpdateAvaloniaBitmap();
        }
        else
        {
            _zoom = 1.0;
            var temp = _skBitmap.Copy();
            InitializeDrawingSurface((int)(_unzoomedWidth * _zoom), (int)(_unzoomedHeight * _zoom));
            temp.ScalePixels(_skBitmap, SKSamplingOptions.Default);
            UpdateAvaloniaBitmap();
        }
    }

    private void ResetZoom()
    {
        while (_zoom > 1.0) Zoom(-_appSettings.ZoomLevelIncrement);
    }

    private void ZoomOut()
    {
        Zoom(-_appSettings.ZoomLevelIncrement);
    }

    private void ZoomIn()
    {
        Zoom(_appSettings.ZoomLevelIncrement);
    }

    private void Move(MoveDirections direction)
    {
        Action call = null;
        switch (direction)
        {
            case MoveDirections.Up:
                call = _sv.LineUp;
                break;
            case MoveDirections.Down:
                call = _sv.LineDown;
                break;
            case MoveDirections.Left:
                call = _sv.LineLeft;
                break;
            case MoveDirections.Right:
                call = _sv.LineRight;
                break;
        }
        for (var i = 0; i <= _appSettings.MoveIncrement; i++) call?.Invoke();
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(_drawingImage);
        _isDrawing = true;
        _lastPoint = pos;
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_isDrawing) return;
        var pos = e.GetPosition(_drawingImage);
        DrawLine(_lastPoint, pos);
        _lastPoint = pos;
        UpdateAvaloniaBitmap();
    }

    private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        _isDrawing = false;
        UpdateAvaloniaBitmap();
    }


    private static DateTime _lastButtonHit = DateTime.Now;

    private void OnIRCameraEvent(object sender, IrCamera.IrCameraEventArgs e)
    {
        if (_isCalibrating || !IrCamera.Calibrated) return;

        foreach (var p in e.Points)
        {
            var adjustedP = new Point(p.X, p.Y - _toolbar.Bounds.Height);

            if (adjustedP.Y < 0 || _hasOptionWindowOpen)
            {
                if ((DateTime.Now - _lastButtonHit).TotalMilliseconds < 500) return;
                _lastButtonHit = DateTime.Now;
                Dispatcher.UIThread.InvokeAsync(() => ButtonClickCheck(p));
                return;
            }

            var offset = _sv.Offset;
            adjustedP = new Point(adjustedP.X + offset.X, adjustedP.Y + offset.Y);
            var foundNoPen = true;
            foreach (var pen in _pens)
            {
                var p1 = pen.LastPoint;
                var p2 = adjustedP;
                var distance = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
                if (distance >= _drawingImage.Bounds.Width * 0.1) continue;
                HandelPenInput(adjustedP, pen);
                foundNoPen = false;
                break;
            }

            if (foundNoPen) _pens.Add(new Pen(adjustedP, DateTime.Now));
        }

        UpdateAvaloniaBitmap();
    }

    private EventSimulator _simulator = new();

    private void ButtonClickCheck(Point target)
    {
        var screenPoint = this.PointToScreen(target);
        _simulator.SimulateMousePress((short)screenPoint.X, (short)screenPoint.Y, SharpHook.Data.MouseButton.Button1);
        _simulator.SimulateMouseRelease((short)screenPoint.X, (short)screenPoint.Y, SharpHook.Data.MouseButton.Button1);
    }

    private void CleanPenList()
    {
        var np = new List<Pen>();
        foreach (var p in _pens)
        {
            if ((DateTime.Now - p.LastInputTime).TotalMilliseconds > 750) continue;
            np.Add(p);
        }

        _pens.Clear();
        _pens = np;
    }

    private List<Pen> _pens = new();

    private class Pen(Point lastPoint, DateTime lastInputTime)
    {
        public Point LastPoint = lastPoint;
        public DateTime LastInputTime = lastInputTime;
    }

    private void HandelPenInput(Point p, Pen pen)
    {
        var tempTime = pen.LastInputTime;
        pen.LastInputTime = DateTime.Now;

        if (pen.LastPoint == default || (pen.LastInputTime - tempTime).TotalMilliseconds > 200)
        {
            pen.LastPoint = p;
            return;
        }

        DrawLine(pen.LastPoint, p);
        pen.LastPoint = p;
    }

    private void DrawLine(Point from, Point to)
    {
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;

        if (_isErasing.Value)
        {
            paint.Color = SKColors.White;
            paint.BlendMode = SKBlendMode.Src;
            paint.StrokeWidth = (float)_eraserStrokeThickness;
        }
        else
        {
            paint.Color = _currentSkColor;
            paint.StrokeWidth = (float)_drawStrokeThickness;
        }

        _skCanvas.DrawLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y, paint);
        _skCanvas.DrawCircle((float)to.X, (float)to.Y, paint.StrokeWidth / 2, paint);
    }

    private void UpdateAvaloniaBitmap()
    {
    }

    private void UpdateAvaloniaBitmapTest()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            using (var l = _writeableBitmap.Lock())
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)_skBitmap.GetPixels(),
                        (void*)l.Address,
                        l.RowBytes * l.Size.Height,
                        _skBitmap.RowBytes * _skBitmap.Height);
                }
            }

            _drawingImage.InvalidateVisual();
        }, DispatcherPriority.Render);
    }


    private bool _isCalibrating;

    private void OpenCalibrationForm()
    {
        _isCalibrating = true;
        var calibWindow = new CalibrationWindow(this);
        calibWindow.Closed += (_, _) => _isCalibrating = false;
        calibWindow.ShowDialog(this);
    }

    private void Settings()
    {
        _hasOptionWindowOpen = true;
        var settingsWindow = new Window
        {
            Title = "Settings",
            Width = 300,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false
        };
        settingsWindow.Closed += (_, _) => _hasOptionWindowOpen = false;
        var gridWidthSlider = new Slider
        {
            Minimum = 1,
            Maximum = _skBitmap.Width,
            Value = _appSettings.GridWidth,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(20)
        };
        gridWidthSlider.ValueChanged += (sender, args) =>
        {
            var old = _appSettings.GridWidth;
            _appSettings.GridWidth = (int)args.NewValue;
            InitializeDrawingSurface(_skBitmap.Width, _skBitmap.Height);
            UpdateAvaloniaBitmap();
            _appSettings.GridWidth = old;
        };

        var zoomLevelSlider = new Slider
        {
            Minimum = 0.01,
            Maximum = 0.25,
            Value = _appSettings.ZoomLevelIncrement,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(20)
        };

        var moveLevelSlider = new Slider
        {
            Minimum = 1,
            Maximum = 25,
            Value = _appSettings.MoveIncrement,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(20)
        };

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(10)
        };

        okButton.Click += (_, _) =>
        {
            _appSettings.GridWidth = (int)gridWidthSlider.Value;
            _appSettings.ZoomLevelIncrement = (float)zoomLevelSlider.Value;
            _appSettings.MoveIncrement = (int)moveLevelSlider.Value;
            settingsWindow.Close();
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Grid Dimention",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(gridWidthSlider);
        stack.Children.Add(new TextBlock
        {
            Text = "Zoom Strength",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(zoomLevelSlider);
        stack.Children.Add(new TextBlock
        {
            Text = "Move stride Size",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(moveLevelSlider);
        stack.Children.Add(okButton);

        settingsWindow.Content = stack;
        settingsWindow.Show();
    }

    private enum DrawingColor
    {
        Black,
        Red,
        Green,
        Blue,
        Yellow,
        Orange,
        Purple
    }
    private DrawingColor _lastChoosen = DrawingColor.Black;
    private void ShowDrawSettings(bool isEraser = false)
    {
        if (isEraser != _isErasing.Value)
        {
            _isErasing.Value = isEraser;
            return;
        }

        if (_hasOptionWindowOpen) return;
        _hasOptionWindowOpen = true;
        _drawingOptionsWindow = new Window
        {
            Title = isEraser ? "Eraser Settings" : "Draw Settings",
            Width = 300,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        _drawingOptionsWindow.Closed += (_, _) => { _hasOptionWindowOpen = false; };
        ComboBox? colorComboBox = null;
        
        

        if (!isEraser)
            colorComboBox = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(DrawingColor)),
                SelectedItem = _lastChoosen,
                Margin = new Thickness(10)
            };

        var slider = new Slider
        {
            Minimum = 1,
            Maximum = 25,
            Value = isEraser ? _eraserStrokeThickness : _drawStrokeThickness,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(20)
        };
        
        var previewBrush = _lastChoosen switch
        {
            DrawingColor.Red => Brushes.Red,
            DrawingColor.Green => Brushes.Green,
            DrawingColor.Blue => Brushes.Blue,
            DrawingColor.Yellow => Brushes.Yellow,
            DrawingColor.Orange => Brushes.Orange,
            DrawingColor.Purple => Brushes.Purple,
            _ => Brushes.Black
        };

        Shape previewShape = isEraser
            ? new Ellipse
            {
                Width = _eraserStrokeThickness,
                Height = _eraserStrokeThickness,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Margin = new Thickness(10)
            }
            : new Line
            {
                StartPoint = new Point(10, 10),
                EndPoint = new Point(250, 10),
                Stroke = previewBrush,
                StrokeThickness = _drawStrokeThickness,
                Margin = new Thickness(20)
            };


        void UpdatePreview()
        {
            if (isEraser)
            {
                if (previewShape is Ellipse ellipse)
                {
                    ellipse.Width = slider.Value;
                    ellipse.Height = slider.Value;
                }
            }
            else
            {
                var selectedColor = (DrawingColor)(colorComboBox!.SelectedItem?? DrawingColor.Black);
                _lastChoosen = selectedColor;
                _currentSkColor = selectedColor switch
                {
                    DrawingColor.Red => SKColors.Red,
                    DrawingColor.Green => SKColors.Green,
                    DrawingColor.Blue => SKColors.Blue,
                    DrawingColor.Yellow => SKColors.Yellow,
                    DrawingColor.Orange => SKColors.Orange,
                    DrawingColor.Purple => SKColors.Purple,
                    _ => SKColors.Black
                };
                ((Line)previewShape).Stroke = selectedColor switch
                {
                    DrawingColor.Red => Brushes.Red,
                    DrawingColor.Green => Brushes.Green,
                    DrawingColor.Blue => Brushes.Blue,
                    DrawingColor.Yellow => Brushes.Yellow,
                    DrawingColor.Orange => Brushes.Orange,
                    DrawingColor.Purple => Brushes.Purple,
                    _ => Brushes.Black
                };
                ((Line)previewShape).StrokeThickness = slider.Value;
            }
        }

        if (colorComboBox != null)
            colorComboBox.SelectionChanged += (_, _) => UpdatePreview();

        slider.PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
                UpdatePreview();
        };

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(10)
        };

        okButton.Click += (_, _) =>
        {
            if (isEraser)
                _eraserStrokeThickness = slider.Value;
            else
                _drawStrokeThickness = slider.Value;
            _isErasing.Value = isEraser;
            _drawingOptionsWindow.Close();
        };

        var stack = new StackPanel();
        if (colorComboBox != null)
            stack.Children.Add(colorComboBox);
        stack.Children.Add(slider);
        stack.Children.Add(previewShape);
        stack.Children.Add(okButton);
        _drawingOptionsWindow.Content = stack;

        _drawingOptionsWindow.ShowDialog(this);
    }

    private void Store()
    {
        IP6.IrCamera.StoreCalibration();
        var settings = JsonSerializer.Serialize(_appSettings);
        File.WriteAllText("settings.json", settings);
    }

    private void Load()
    {
        IP6.IrCamera.LoadCalibration();
        if (!File.Exists("settings.json")) return;
        string json = File.ReadAllText("settings.json");
        var settings = JsonSerializer.Deserialize<Settings>(json);
        _appSettings = settings;
        ClearCanvas();
    }
}