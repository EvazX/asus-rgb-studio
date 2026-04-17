using System.Diagnostics;
using System.Runtime.InteropServices;

var options = Options.Parse(args);
using var hid = new HidController(options.HidApiPath);
using var meter = new AudioPeakMeter();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    options.StopRequested = true;
};

Console.WriteLine("Audio reactive mode active. Press Ctrl+C to stop.");

var levels = new double[AppConstants.ZoneCount];
var peakHold = 0.0;
var watch = Stopwatch.StartNew();
var frameDelay = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, options.Fps));

while (!options.StopRequested)
{
    var peak = meter.GetPeak();
    var boostedPeak = Math.Clamp(Math.Pow(peak, options.Gamma) * options.Sensitivity, 0.0, 1.0);

    for (var i = 0; i < AppConstants.ZoneCount; i++)
    {
        var threshold = (i + 1) / (double)AppConstants.ZoneCount;
        var target = Math.Clamp((boostedPeak - (threshold - 0.25)) / 0.45, 0.0, 1.0);
        if (target > levels[i])
        {
            levels[i] = levels[i] + (target - levels[i]) * options.Attack;
        }
        else
        {
            levels[i] *= options.Decay;
        }
    }

    peakHold = Math.Max(boostedPeak, peakHold * 0.93);
    hid.ApplyColors(RenderFrame(levels, peakHold, options));

    var elapsed = watch.Elapsed;
    if (elapsed < frameDelay)
    {
        Thread.Sleep(frameDelay - elapsed);
    }
    watch.Restart();
}

hid.ApplyColors(Enumerable.Repeat(new RgbColor(255, 255, 255), AppConstants.ZoneCount).ToArray());

static RgbColor[] RenderFrame(double[] levels, double peakHold, Options options)
{
    var gradient = options.Style switch
    {
        "ice" => new[]
        {
            new RgbColor(36, 132, 255),
            new RgbColor(72, 190, 255),
            new RgbColor(150, 228, 255),
            new RgbColor(235, 245, 255)
        },
        "neon" => new[]
        {
            new RgbColor(0, 220, 255),
            new RgbColor(80, 120, 255),
            new RgbColor(180, 80, 255),
            new RgbColor(255, 255, 255)
        },
        _ => new[]
        {
            new RgbColor(255, 70, 16),
            new RgbColor(255, 135, 24),
            new RgbColor(255, 195, 54),
            new RgbColor(255, 245, 185)
        }
    };

    var colors = new RgbColor[AppConstants.ZoneCount];
    for (var i = 0; i < AppConstants.ZoneCount; i++)
    {
        var energy = Math.Clamp(levels[i], 0.0, 1.0);
        var color = gradient[i];
        var floor = 0.02;
        var factor = floor + (energy * 0.98);
        colors[i] = new RgbColor(
            ClampByte(color.R * factor * options.IntensityBoost),
            ClampByte(color.G * factor * options.IntensityBoost),
            ClampByte(color.B * factor * options.IntensityBoost));
    }

    var heldZone = Math.Clamp((int)Math.Ceiling(peakHold * AppConstants.ZoneCount) - 1, 0, AppConstants.ZoneCount - 1);
    colors[heldZone] = Blend(colors[heldZone], new RgbColor(255, 255, 255), 0.45);
    return colors;
}

static int ClampByte(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);

static RgbColor Blend(RgbColor left, RgbColor right, double ratio)
{
    var t = Math.Clamp(ratio, 0.0, 1.0);
    return new RgbColor(
        ClampByte(left.R + (right.R - left.R) * t),
        ClampByte(left.G + (right.G - left.G) * t),
        ClampByte(left.B + (right.B - left.B) * t));
}

internal sealed record class Options
{
    public double Fps { get; init; } = 30;
    public double Sensitivity { get; init; } = 2.2;
    public double Attack { get; init; } = 0.72;
    public double Decay { get; init; } = 0.86;
    public double Gamma { get; init; } = 0.72;
    public double IntensityBoost { get; init; } = 1.0;
    public string Style { get; init; } = "fire";
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

            static double ParseDouble(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

            options = arg switch
            {
                "--fps" => options with { Fps = ParseDouble(ReadValue()) },
                "--sensitivity" => options with { Sensitivity = ParseDouble(ReadValue()) },
                "--attack" => options with { Attack = ParseDouble(ReadValue()) },
                "--decay" => options with { Decay = ParseDouble(ReadValue()) },
                "--gamma" => options with { Gamma = ParseDouble(ReadValue()) },
                "--intensity-boost" => options with { IntensityBoost = ParseDouble(ReadValue()) },
                "--style" => options with { Style = ReadValue().ToLowerInvariant() },
                "--hidapi" => options with { HidApiPath = ReadValue() },
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
    public const int ZoneCount = 4;
    public const int StaticMode = 0x00;
}

internal sealed class HidController : IDisposable
{
    private readonly HidApi _hidApi;
    private readonly IntPtr _handle;

    public HidController(string hidApiPath)
    {
        _hidApi = new HidApi(hidApiPath);
        _hidApi.Init();
        _handle = _hidApi.Open(AppConstants.HidPath);
    }

    public void ApplyColors(RgbColor[] colors)
    {
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            var packet = new List<byte> { 0x5D, 0xB3, (byte)(i + 1), AppConstants.StaticMode, (byte)color.R, (byte)color.G, (byte)color.B, 0x00 };
            while (packet.Count < 17)
            {
                packet.Add(0x00);
            }
            _hidApi.Write(_handle, packet.ToArray());
        }

        _hidApi.Write(_handle, BuildShortPacket(0x5D, 0xB5));
        _hidApi.Write(_handle, BuildShortPacket(0x5D, 0xB4));
    }

    public void Dispose()
    {
        _hidApi.Close(_handle);
    }

    private static byte[] BuildShortPacket(byte a, byte b)
    {
        var packet = new byte[17];
        packet[0] = a;
        packet[1] = b;
        return packet;
    }
}

internal sealed class HidApi
{
    private readonly NativeHid _hid;

    public HidApi(string path)
    {
        _hid = new NativeHid(path);
    }

    public void Init()
    {
        var result = _hid.hid_init();
        if (result != 0)
        {
            throw new InvalidOperationException($"hid_init failed: {result}");
        }
    }

    public IntPtr Open(string path)
    {
        var handle = _hid.hid_open_path(path);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to open ASUS HID device.");
        }
        return handle;
    }

    public void Write(IntPtr handle, byte[] payload)
    {
        var result = _hid.hid_write(handle, payload, (nuint)payload.Length);
        if (result < 0)
        {
            throw new InvalidOperationException("hid_write failed.");
        }
    }

    public void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            _hid.hid_close(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class NativeHid
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int HidInit();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr HidOpenPath([MarshalAs(UnmanagedType.LPStr)] string path);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int HidWrite(IntPtr device, byte[] data, nuint length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void HidClose(IntPtr device);

        public readonly HidInit hid_init;
        public readonly HidOpenPath hid_open_path;
        public readonly HidWrite hid_write;
        public readonly HidClose hid_close;

        public NativeHid(string dllPath)
        {
            var module = NativeLibrary.Load(dllPath);
            hid_init = Marshal.GetDelegateForFunctionPointer<HidInit>(NativeLibrary.GetExport(module, "hid_init"));
            hid_open_path = Marshal.GetDelegateForFunctionPointer<HidOpenPath>(NativeLibrary.GetExport(module, "hid_open_path"));
            hid_write = Marshal.GetDelegateForFunctionPointer<HidWrite>(NativeLibrary.GetExport(module, "hid_write"));
            hid_close = Marshal.GetDelegateForFunctionPointer<HidClose>(NativeLibrary.GetExport(module, "hid_close"));
        }
    }
}

internal sealed class AudioPeakMeter : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly IMMDevice _device;
    private readonly IAudioMeterInformation _meter;

    public AudioPeakMeter()
    {
        var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!;
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _device));

        var iid = typeof(IAudioMeterInformation).GUID;
        Marshal.ThrowExceptionForHR(_device.Activate(ref iid, 23, IntPtr.Zero, out var meterObject));
        _meter = (IAudioMeterInformation)meterObject;
    }

    public float GetPeak()
    {
        Marshal.ThrowExceptionForHR(_meter.GetPeakValue(out var peak));
        return peak;
    }

    public void Dispose()
    {
        if (_meter is not null) Marshal.ReleaseComObject(_meter);
        if (_device is not null) Marshal.ReleaseComObject(_device);
        if (_enumerator is not null) Marshal.ReleaseComObject(_enumerator);
    }
}

internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int NotImpl1();
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    int GetPeakValue(out float pfPeak);
    int GetMeteringChannelCount(out int pnChannelCount);
    int GetChannelsPeakValues(int u32ChannelCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] afPeakValues);
    int QueryHardwareSupport(out int pdwHardwareSupportMask);
}
