using System;
using System.Runtime.InteropServices;

namespace AsusAmbientLed
{
    internal static class Program
    {
        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const uint CoinitMultithreaded = 0x0;

        private static int Main()
        {
            var initialized = false;
            try
            {
                var hr = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
                initialized = hr >= 0;

                var sdkType = Type.GetTypeFromProgID("aura.sdk");
                if (sdkType == null)
                {
                    Console.WriteLine("ProgID aura.sdk introuvable.");
                    return 1;
                }

                dynamic sdk = Activator.CreateInstance(sdkType);
                sdk.SwitchMode();

                dynamic devices = sdk.Enumerate(0);
                Console.WriteLine("Devices count: " + devices.Count);

                for (int i = 0; i < devices.Count; i++)
                {
                    dynamic device = devices.Item(i);
                    Console.WriteLine(string.Format("{0} type=0x{1:X8} size={2}x{3}", device.Name, device.Type, device.Width, device.Height));
                }

                sdk.ReleaseControl(0);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
            finally
            {
                if (initialized)
                {
                    CoUninitialize();
                }
            }
        }
    }
}
