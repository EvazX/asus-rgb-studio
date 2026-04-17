using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

var options = Options.Parse(args);
using var hid = new HidController(options.HidApiPath);
using var sampler = new ScreenSampler();

if (options.Red)
{
    hid.ApplyColors(Enumerable.Repeat(new RgbColor(255, 0, 0), AppConstants.ZoneCount).ToArray());
    Console.WriteLine("Applied solid red.");
    return;
}

if (options.BenchmarkFrames > 0)
{
    BenchmarkRunner.Run(options, hid, sampler);
    return;
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    options.StopRequested = true;
};

RgbColor[]? lastColors = null;
var frameDelay = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, options.Fps));
var stopwatch = Stopwatch.StartNew();

Console.WriteLine("C# ambient sync active. Press Ctrl+C to stop.");

while (!options.StopRequested)
{
    var liveIntensity = RuntimeState.ReadIntensity(options.IntensityBoost);
    var sampled = sampler.CaptureZones(options);
    var boosted = sampled.Select(color => ColorMath.Boost(color, options.SaturationBoost, options.ValueBoost, liveIntensity)).ToArray();

    var shouldUpdate = lastColors is null;
    if (lastColors is not null)
    {
        for (var i = 0; i < boosted.Length; i++)
        {
            if (ColorMath.Distance(lastColors[i], boosted[i]) >= options.Threshold)
            {
                shouldUpdate = true;
                break;
            }
        }
    }

    if (shouldUpdate)
    {
        hid.ApplyColors(boosted);
        lastColors = boosted;
    }

    if (options.Once)
    {
        break;
    }

    var elapsed = stopwatch.Elapsed;
    if (elapsed < frameDelay)
    {
        Thread.Sleep(frameDelay - elapsed);
    }

    stopwatch.Restart();
}

internal sealed record class Options
{
    public double Fps { get; init; } = 28;
    public int SamplesX { get; init; } = 6;
    public int SamplesY { get; init; } = 4;
    public int Threshold { get; init; } = 3;
    public double VerticalBias { get; init; } = 0.22;
    public double SaturationBoost { get; init; } = 3.0;
    public double ValueBoost { get; init; } = 0.9;
    public double IntensityBoost { get; init; } = 1.0;
    public int NeutralThreshold { get; init; } = 30;
    public double ColorBias { get; init; } = 3.5;
    public bool Mirror { get; init; }
    public bool Once { get; init; }
    public bool Red { get; init; }
    public int BenchmarkFrames { get; init; }
    public bool StopRequested { get; set; }
    public string HidApiPath { get; init; } = @"C:\Program Files\OpenRGB\hidapi.dll";

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string ReadValue()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {arg}");
                }

                i++;
                return args[i];
            }

            static double ParseDouble(string value) => double.Parse(value, CultureInfo.InvariantCulture);

            options = arg switch
            {
                "--fps" => options with { Fps = ParseDouble(ReadValue()) },
                "--samples-x" => options with { SamplesX = int.Parse(ReadValue()) },
                "--samples-y" => options with { SamplesY = int.Parse(ReadValue()) },
                "--threshold" => options with { Threshold = int.Parse(ReadValue()) },
                "--vertical-bias" => options with { VerticalBias = ParseDouble(ReadValue()) },
                "--saturation-boost" => options with { SaturationBoost = ParseDouble(ReadValue()) },
                "--value-boost" => options with { ValueBoost = ParseDouble(ReadValue()) },
                "--intensity-boost" => options with { IntensityBoost = ParseDouble(ReadValue()) },
                "--neutral-threshold" => options with { NeutralThreshold = int.Parse(ReadValue()) },
                "--color-bias" => options with { ColorBias = ParseDouble(ReadValue()) },
                "--mirror" => options with { Mirror = true },
                "--hidapi" => options with { HidApiPath = ReadValue() },
                "--benchmark" => options with { BenchmarkFrames = int.Parse(ReadValue()) },
                "--once" => options with { Once = true },
                "--red" => options with { Red = true },
                _ => options
            };
        }

        return options;
    }
}

internal readonly record struct RgbColor(int R, int G, int B);

internal static class AppConstants
{
    public const string HidPath = @"\\?\HID#VID_0B05&PID_1866&Col05#7&289d55ad&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    public const string IntensityStateFile = @"D:\asus-ambient-led\rgb_intensity.txt";
    public const int ZoneCount = 4;
    public const int StaticMode = 0x00;
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;
    public const int SourceCopy = 0x00CC0020;
    public const int Halftone = 4;
}

internal static class RuntimeState
{
    public static double ReadIntensity(double fallback)
    {
        try
        {
            if (File.Exists(AppConstants.IntensityStateFile))
            {
                var text = File.ReadAllText(AppConstants.IntensityStateFile).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return Math.Clamp(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture), 0.0, 1.0);
                }
            }
        }
        catch
        {
        }

        return Math.Clamp(fallback, 0.0, 1.0);
    }
}

internal static class BenchmarkRunner
{
    public static void Run(Options options, HidController hid, ScreenSampler sampler)
    {
        var captureTotal = 0.0;
        var boostTotal = 0.0;
        var sendTotal = 0.0;
        var frames = Math.Max(1, options.BenchmarkFrames);
        var timer = Stopwatch.StartNew();

        for (var i = 0; i < frames; i++)
        {
            var liveIntensity = RuntimeState.ReadIntensity(options.IntensityBoost);
            timer.Restart();
            var sampled = sampler.CaptureZones(options);
            timer.Stop();
            captureTotal += timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            var boosted = sampled.Select(color => ColorMath.Boost(color, options.SaturationBoost, options.ValueBoost, liveIntensity)).ToArray();
            timer.Stop();
            boostTotal += timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            hid.ApplyColors(boosted);
            timer.Stop();
            sendTotal += timer.Elapsed.TotalMilliseconds;
        }

        Console.WriteLine($"Frames: {frames}");
        Console.WriteLine($"Capture avg: {captureTotal / frames:F2} ms");
        Console.WriteLine($"Boost avg: {boostTotal / frames:F2} ms");
        Console.WriteLine($"Send avg: {sendTotal / frames:F2} ms");
        Console.WriteLine($"Software pipeline avg: {(captureTotal + boostTotal + sendTotal) / frames:F2} ms");
        Console.WriteLine("Note: this measures software time only, not the physical LED response time.");
    }
}

internal static class ColorMath
{
    public static int Distance(RgbColor left, RgbColor right) =>
        Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);

    public static RgbColor Boost(RgbColor color, double saturationBoost, double valueBoost, double intensityBoost)
    {
        RgbToHsv(color, out var hue, out var saturation, out var value);
        saturation = Math.Clamp(saturation * saturationBoost, 0.0, 1.0);
        value = Math.Clamp(value * valueBoost, 0.0, 1.0);
        var rgb = HsvToRgb(hue, saturation, value);
        return new RgbColor(
            ClampByte(rgb.R * intensityBoost),
            ClampByte(rgb.G * intensityBoost),
            ClampByte(rgb.B * intensityBoost));
    }

    private static void RgbToHsv(RgbColor color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        hue = 0.0;
        if (delta > 0.0)
        {
            if (max == r)
            {
                hue = 60.0 * (((g - b) / delta) % 6.0);
            }
            else if (max == g)
            {
                hue = 60.0 * (((b - r) / delta) + 2.0);
            }
            else
            {
                hue = 60.0 * (((r - g) / delta) + 4.0);
            }
        }

        if (hue < 0.0)
        {
            hue += 360.0;
        }

        saturation = max == 0.0 ? 0.0 : delta / max;
        value = max;
    }

    private static RgbColor HsvToRgb(double hue, double saturation, double value)
    {
        if (saturation <= 0.0)
        {
            var gray = ClampByte(value * 255.0);
            return new RgbColor(gray, gray, gray);
        }

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60.0 % 2) - 1));
        var m = value - chroma;

        (double r, double g, double b) = hue switch
        {
            >= 0 and < 60 => ((double, double, double))(chroma, x, 0),
            >= 60 and < 120 => ((double, double, double))(x, chroma, 0),
            >= 120 and < 180 => ((double, double, double))(0, chroma, x),
            >= 180 and < 240 => ((double, double, double))(0, x, chroma),
            >= 240 and < 300 => ((double, double, double))(x, 0, chroma),
            _ => ((double, double, double))(chroma, 0, x)
        };

        return new RgbColor(
            ClampByte((r + m) * 255.0),
            ClampByte((g + m) * 255.0),
            ClampByte((b + m) * 255.0));
    }

    private static int ClampByte(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);
}

internal sealed class ScreenSampler : IDisposable
{
    private readonly IntPtr _screenDc;
    private Bitmap? _bitmap;
    private Graphics? _graphics;
    private int _captureWidth;
    private int _captureHeight;

    public ScreenSampler()
    {
        _screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (_screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to access desktop DC.");
        }
    }

    public RgbColor[] CaptureZones(Options options)
    {
        var left = NativeMethods.GetSystemMetrics(AppConstants.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(AppConstants.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(AppConstants.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(AppConstants.SmCyVirtualScreen);

        var captureWidth = Math.Max(AppConstants.ZoneCount * Math.Max(1, options.SamplesX), AppConstants.ZoneCount);
        var captureHeight = Math.Max(2, options.SamplesY * 3);
        EnsureBuffers(captureWidth, captureHeight);

        var sampledHeight = Math.Max(1, (int)(height * Math.Clamp(options.VerticalBias, 0.1, 1.0)));
        var startY = top + height - sampledHeight;
        var zones = new RgbColor[AppConstants.ZoneCount];
        var captureZoneWidth = captureWidth / AppConstants.ZoneCount;

        var captureDc = _graphics!.GetHdc();
        try
        {
            NativeMethods.SetStretchBltMode(captureDc, AppConstants.Halftone);
            var ok = NativeMethods.StretchBlt(
                captureDc,
                0,
                0,
                captureWidth,
                captureHeight,
                _screenDc,
                left,
                startY,
                width,
                sampledHeight,
                AppConstants.SourceCopy);

            if (!ok)
            {
                throw new InvalidOperationException("StretchBlt failed.");
            }
        }
        finally
        {
            _graphics.ReleaseHdc(captureDc);
        }

        var rect = new Rectangle(0, 0, captureWidth, captureHeight);
        var bitmapData = _bitmap!.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var basePtr = (byte*)bitmapData.Scan0.ToPointer();
                for (var zoneIndex = 0; zoneIndex < AppConstants.ZoneCount; zoneIndex++)
                {
                    var x0 = zoneIndex * captureZoneWidth;
                    var x1 = zoneIndex == AppConstants.ZoneCount - 1 ? captureWidth : x0 + captureZoneWidth;

                    double rTotal = 0;
                    double gTotal = 0;
                    double bTotal = 0;
                    double weightTotal = 0;
                    long fallbackR = 0;
                    long fallbackG = 0;
                    long fallbackB = 0;
                    long fallbackCount = 0;

                    for (var y = 0; y < captureHeight; y++)
                    {
                        var row = basePtr + y * bitmapData.Stride;
                        for (var x = x0; x < x1; x++)
                        {
                            var pixel = row + x * 4;
                            var blue = pixel[0];
                            var green = pixel[1];
                            var red = pixel[2];

                            fallbackR += red;
                            fallbackG += green;
                            fallbackB += blue;
                            fallbackCount++;

                            var span = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
                            if (span < options.NeutralThreshold)
                            {
                                continue;
                            }

                            var weight = 1.0 + (span / 255.0) * Math.Max(0.0, options.ColorBias);
                            if (options.Mirror)
                            {
                                var localX = (x - x0) / (double)Math.Max(1, x1 - x0 - 1);
                                var edgeBias = zoneIndex <= 1 ? (1.0 - localX) : localX;
                                var mirrorBoost = 1.0 + edgeBias * 1.4;
                                var verticalBoost = 1.0 + ((captureHeight - 1 - y) / (double)Math.Max(1, captureHeight - 1)) * 0.45;
                                weight *= mirrorBoost * verticalBoost;
                            }
                            rTotal += red * weight;
                            gTotal += green * weight;
                            bTotal += blue * weight;
                            weightTotal += weight;
                        }
                    }

                    zones[zoneIndex] = weightTotal > 0
                        ? new RgbColor((int)(rTotal / weightTotal), (int)(gTotal / weightTotal), (int)(bTotal / weightTotal))
                        : fallbackCount > 0
                            ? new RgbColor((int)(fallbackR / fallbackCount), (int)(fallbackG / fallbackCount), (int)(fallbackB / fallbackCount))
                            : new RgbColor(0, 0, 0);
                }
            }
        }
        finally
        {
            _bitmap.UnlockBits(bitmapData);
        }

        return options.Mirror ? ApplyMirrorBlending(zones) : zones;
    }

    private static RgbColor[] ApplyMirrorBlending(RgbColor[] zones)
    {
        return
        [
            Blend(zones[0], zones[1], 0.18),
            Blend(zones[1], zones[0], 0.28),
            Blend(zones[2], zones[3], 0.28),
            Blend(zones[3], zones[2], 0.18),
        ];
    }

    private static RgbColor Blend(RgbColor left, RgbColor right, double ratio)
    {
        var r = Math.Clamp(ratio, 0.0, 1.0);
        return new RgbColor(
            (int)Math.Round(left.R + (right.R - left.R) * r),
            (int)Math.Round(left.G + (right.G - left.G) * r),
            (int)Math.Round(left.B + (right.B - left.B) * r));
    }

    private void EnsureBuffers(int width, int height)
    {
        if (_bitmap is not null && width == _captureWidth && height == _captureHeight)
        {
            return;
        }

        _graphics?.Dispose();
        _bitmap?.Dispose();

        _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);
        _captureWidth = width;
        _captureHeight = height;
    }

    public void Dispose()
    {
        _graphics?.Dispose();
        _bitmap?.Dispose();
        if (_screenDc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
        }
    }
}

internal sealed class HidController : IDisposable
{
    private readonly IntPtr _libraryHandle;
    private readonly HidInitDelegate _hidInit;
    private readonly HidOpenPathDelegate _hidOpenPath;
    private readonly HidWriteDelegate _hidWrite;
    private readonly HidCloseDelegate _hidClose;
    private readonly HidErrorDelegate _hidError;
    private readonly IntPtr _device;

    public HidController(string dllPath)
    {
        _libraryHandle = NativeMethods.LoadLibrary(dllPath);
        if (_libraryHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unable to load hidapi from {dllPath}");
        }

        _hidInit = LoadFunction<HidInitDelegate>("hid_init");
        _hidOpenPath = LoadFunction<HidOpenPathDelegate>("hid_open_path");
        _hidWrite = LoadFunction<HidWriteDelegate>("hid_write");
        _hidClose = LoadFunction<HidCloseDelegate>("hid_close");
        _hidError = LoadFunction<HidErrorDelegate>("hid_error");

        if (_hidInit() != 0)
        {
            throw new InvalidOperationException("hid_init failed.");
        }

        _device = _hidOpenPath(AppConstants.HidPath);
        if (_device == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to open ASUS HID device.");
        }
    }

    public void ApplyColors(IReadOnlyList<RgbColor> colors)
    {
        for (var index = 0; index < colors.Count; index++)
        {
            var color = colors[index];
            Span<byte> packet = stackalloc byte[17];
            packet[0] = 0x5D;
            packet[1] = 0xB3;
            packet[2] = (byte)(index + 1);
            packet[3] = AppConstants.StaticMode;
            packet[4] = (byte)color.R;
            packet[5] = (byte)color.G;
            packet[6] = (byte)color.B;
            WritePacket(packet);
        }

        Span<byte> applyA = stackalloc byte[17];
        applyA[0] = 0x5D;
        applyA[1] = 0xB5;
        WritePacket(applyA);

        Span<byte> applyB = stackalloc byte[17];
        applyB[0] = 0x5D;
        applyB[1] = 0xB4;
        WritePacket(applyB);
    }

    private void WritePacket(ReadOnlySpan<byte> packet)
    {
        unsafe
        {
            fixed (byte* ptr = packet)
            {
                var written = _hidWrite(_device, ptr, (nuint)packet.Length);
                if (written < 0)
                {
                    var errorPtr = _hidError(_device);
                    var message = errorPtr != IntPtr.Zero ? Marshal.PtrToStringUni(errorPtr) : "hid_write failed";
                    throw new InvalidOperationException(message);
                }
            }
        }
    }

    private T LoadFunction<T>(string name) where T : Delegate
    {
        var proc = NativeMethods.GetProcAddress(_libraryHandle, name);
        if (proc == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Missing export: {name}");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    public void Dispose()
    {
        if (_device != IntPtr.Zero)
        {
            _hidClose(_device);
        }

        if (_libraryHandle != IntPtr.Zero)
        {
            NativeMethods.FreeLibrary(_libraryHandle);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HidInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr HidOpenPathDelegate([MarshalAs(UnmanagedType.LPStr)] string path);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int HidWriteDelegate(IntPtr device, byte* data, nuint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void HidCloseDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr HidErrorDelegate(IntPtr device);
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool StretchBlt(
        IntPtr hdcDest,
        int xDest,
        int yDest,
        int widthDest,
        int heightDest,
        IntPtr hdcSrc,
        int xSrc,
        int ySrc,
        int widthSrc,
        int heightSrc,
        int rop);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);
}
