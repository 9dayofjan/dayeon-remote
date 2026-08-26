using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TestDirectDevice {
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    const int SRCCOPY = 0x00CC0020;

    static void Main() {
        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();
        for (int i = 0; i < screens.Length; i++) {
            var s = screens[i];
            Console.WriteLine(string.Format("Testing screen {0}: {1} Bounds={2}", i, s.DeviceName, s.Bounds));
            
            // Direct Monitor Device Context
            IntPtr hdcSrc = CreateDC(s.DeviceName, null, null, IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) {
                hdcSrc = CreateDC(null, s.DeviceName, null, IntPtr.Zero);
            }
            if (hdcSrc == IntPtr.Zero) {
                Console.WriteLine("CreateDC failed for " + s.DeviceName);
                continue;
            }

            using (Bitmap bmp = new Bitmap(s.Bounds.Width, s.Bounds.Height, PixelFormat.Format32bppArgb)) {
                using (Graphics gDest = Graphics.FromImage(bmp)) {
                    IntPtr hdcDest = gDest.GetHdc();
                    // Source is (0, 0) inside the monitor's own DC!
                    BitBlt(hdcDest, 0, 0, s.Bounds.Width, s.Bounds.Height, hdcSrc, 0, 0, SRCCOPY);
                    gDest.ReleaseHdc(hdcDest);
                }
                DeleteDC(hdcSrc);

                // Count non-black pixels
                int nonBlack = 0;
                for (int y = 0; y < bmp.Height; y += 50) {
                    for (int x = 0; x < bmp.Width; x += 50) {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R > 5 || c.G > 5 || c.B > 5) nonBlack++;
                    }
                }

                bmp.Save("direct_mon_" + i + ".jpg", ImageFormat.Jpeg);
                Console.WriteLine(string.Format("Saved direct_mon_{0}.jpg, size={1}, non-black samples={2}", i, new FileInfo("direct_mon_" + i + ".jpg").Length, nonBlack));
            }
        }
    }
}
