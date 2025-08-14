using System.Drawing;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace IP6;

using Intel.RealSense;
using System;
using System.Runtime.InteropServices;
using Point = Avalonia.Point;

public static class IrCamera
{
    public class IrCameraEventArgs(List<Point> points) : EventArgs
    {
        public List<Point> Points { get; } = points;
    }

    public static event EventHandler<IrCameraEventArgs> CameraEvent;
    private static Pipeline _pipe = new();
    public static PointF ObenLinks { get; set; }
    public static PointF ObenRechts { get; set; }

    public static PointF UntenRechts { get; set; }
    public static PointF UntenLinks { get; set; }
    public static PointF Center { get; set; }

    public static bool Calibrated { get; set; }

    public static bool CameraConnected { get; set; } = true;

    public static int Width { get; set; }
    public static int Height { get; set; }

    private static Config _cfg;
    
    private static bool _showDebugWindow = false;


    static IrCamera()
    {
        var ctx = new Context();
        var list = ctx.QueryDevices();
        if (list.Count == 0)
        {
            CameraConnected = false;
            Console.Out.WriteLine($"IR_CAMERA not connected");
            return;
        }

        _cfg = new Config();
        _cfg.EnableStream(Stream.Infrared, 2);
        _cfg.EnableStream(Stream.Color, Format.Bgr8);
        
        var selection = _pipe.Start(_cfg);
        var selectedDevice = selection.Device;
        var depthSensor = selectedDevice.Sensors[0];

        if (depthSensor.Options.Supports(Option.EmitterEnabled))
            depthSensor.Options[Option.EmitterEnabled].Value = 0f;
        if (depthSensor.Options.Supports(Option.LaserPower))
        {
            var laserPower = depthSensor.Options[Option.LaserPower];
            laserPower.Value = 0f;
        }

        var thread = new Thread(() => { UpdateIrImagesSingleCameraLoop(); })
        {
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        thread.Start();
    }

    public static VisualizerWindow Win;

    public static async void ToggleDebugWindow()
    {
        if (!_showDebugWindow)
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Win = new VisualizerWindow();
                Win.Closed += (s, e) => _showDebugWindow = false;
                Win.Show();
            });
        else
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { Win.Close(); });
        _showDebugWindow = !_showDebugWindow;
    }

    private static async Task UpdateIrImagesSingleCameraLoop()
    {
        while (true)
            try
            {
                if (_pipe.PollForFrames(out var frames))
                {
                    var ir1Frame =
                        frames.FirstOrDefault(f => f.Profile.Stream == Stream.Infrared && f.Profile.Index == 2);
                    var rgb = frames.ColorFrame;

                    if (ir1Frame != null)
                    {
                        using var ir1 = ir1Frame.As<VideoFrame>();
                        if (_showDebugWindow && Win != null)
                            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                Win.SetWithVideoFrame(ir1Frame.As<VideoFrame>(), 0);
                                Win.SetWithVideoFrame(rgb, 2);
                            });
                        var detectedPoints = AnalyseFrame(ir1);
                        if (detectedPoints.Count > 0)
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                CameraEvent?.Invoke(null, new IrCameraEventArgs(detectedPoints));
                            });
                    }
                    else
                    {
                        Console.WriteLine("IR frames not found in this frame set.");
                    }
                }
            }
            catch (Exception e)
            {
                Console.Out.WriteLine(e);
                _pipe.Stop();
                _pipe.Start(_cfg);
            }
    }

    private static List<Point> AnalyseFrame(VideoFrame frame, byte threshold = 250)
    {
        Width = frame.Width;
        Height = frame.Height;

        var buffer = new byte[Width * Height];
        Marshal.Copy(frame.Data, buffer, 0, buffer.Length);

        var inputImage = new Image<Gray, byte>(Width, Height);
        inputImage.Bytes = buffer;

        var binary = new Image<Gray, byte>(Width, Height);
        CvInvoke.Threshold(inputImage, binary, threshold, 255, ThresholdType.Binary);

        var contours = new VectorOfVectorOfPoint();
        CvInvoke.FindContours(
            binary,
            contours,
            null,
            RetrType.External,
            ChainApproxMethod.ChainApproxSimple
        );
        var brightPoints = new List<Point>();
        if (_showDebugWindow && Win != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { Win.SetWithImage(binary, 1); });
        var max = double.MinValue;
        for (var i = 0; i < contours.Size; i++) max = Math.Max(max, CvInvoke.ContourArea(contours[i]));
        for (var i = 0; i < contours.Size; i++)
        {
            var contour = contours[i];

            var area = CvInvoke.ContourArea(contour);
            if (area < max * 0.25) continue;

            var m = CvInvoke.Moments(contour);
            var cx = (float)(m.M10 / m.M00);
            var cy = (float)(m.M01 / m.M00);

            Point res;
            if (Calibrated)
            {
                var normalized = ApplyHomography(_homography, new PointF(cx, cy));
                res = new Point(normalized.X, normalized.Y);
            }
            else
            {
                res = new Point(cx / Width, cy / Height);
            }

            brightPoints.Add(res);
        }

        return brightPoints;
    }

    private static Mat _homography;
    public static float DrawingAreaWidth, DrawingAreaHeight;

    public static void SetCalibrated(bool calibrated)
    {
        if (!Calibrated)
            Calibrated = calibrated;
        else
            return;

        var srcPoints = new PointF[]
        {
            new((float)ObenLinks.X, (float)ObenLinks.Y),
            new((float)ObenRechts.X, (float)ObenRechts.Y),
            new((float)UntenRechts.X, (float)UntenRechts.Y),
            new((float)UntenLinks.X, (float)UntenLinks.Y),
            new((float)Center.X, (float)Center.Y)
        };

        var dstPoints = new PointF[]
        {
            new(0.0f, 0.0f),
            new(DrawingAreaWidth, 0.0f),
            new(DrawingAreaWidth, DrawingAreaHeight),
            new(0.0f, DrawingAreaHeight),
            new(DrawingAreaWidth / 2f, DrawingAreaHeight / 2f)
        };
        _homography = CvInvoke.FindHomography(srcPoints, dstPoints);

        TestHomography();
    }


    private static PointF ApplyHomography(Mat h, PointF point)
    {
        PointF[] input = { new((float)point.X, (float)point.Y) };
        var output = CvInvoke.PerspectiveTransform(input, _homography);
        return output[0];
    }

    private static void TestHomography()
    {
        var testPoints = new[]
        {
            new PointF(ObenLinks.X, ObenLinks.Y),
            new PointF(ObenRechts.X, ObenRechts.Y),
            new PointF(UntenRechts.X, UntenRechts.Y),
            new PointF(UntenLinks.X, UntenLinks.Y),
            new PointF(Center.X, Center.Y)
        };

        Console.WriteLine("Testing homography transformation:");

        foreach (var p in testPoints)
        {
            var mapped = ApplyHomography(_homography, p);
            Console.WriteLine($"Input: ({p.X:F1}, {p.Y:F1}) => Output: ({mapped.X:F3}, {mapped.Y:F3})");
        }
    }

    public struct SerializablePointF
    {
        public float X { get; set; }
        public float Y { get; set; }

        public SerializablePointF(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static implicit operator SerializablePointF(PointF p)
        {
            return new SerializablePointF(p.X, p.Y);
        }

        public static implicit operator PointF(SerializablePointF p)
        {
            return new PointF(p.X, p.Y);
        }
    }

    public static void StoreCalibration()
    {
        if (!Calibrated) return;
        var srcPoints = new PointF[]
        {
            new((float)ObenLinks.X, (float)ObenLinks.Y),
            new((float)ObenRechts.X, (float)ObenRechts.Y),
            new((float)UntenRechts.X, (float)UntenRechts.Y),
            new((float)UntenLinks.X, (float)UntenLinks.Y),
            new((float)Center.X, (float)Center.Y)
        };
        var serializablePoints = srcPoints.Select(p => (SerializablePointF)p).ToArray();
        var json = JsonSerializer.Serialize(serializablePoints);
        File.WriteAllText("points.json", json);
    }

    public static void LoadCalibration()
    {
        if (!File.Exists("points.json")) return;
        var json = File.ReadAllText("points.json");
        var serializablePoints = JsonSerializer.Deserialize<SerializablePointF[]>(json);
        var srcPoints = serializablePoints.Select(p => (PointF)p).ToArray();
        var dstPoints = new PointF[]
        {
            new(0.0f, 0.0f),
            new(DrawingAreaWidth, 0.0f),
            new(DrawingAreaWidth, DrawingAreaHeight),
            new(0.0f, DrawingAreaHeight),
            new(DrawingAreaWidth / 2f, DrawingAreaHeight / 2f)
        };
        _homography = CvInvoke.FindHomography(srcPoints, dstPoints);
        Calibrated = true;
    }
}