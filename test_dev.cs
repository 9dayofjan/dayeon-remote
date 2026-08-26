using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TestPerDevice {
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    const int SRCCOPY = 0x00CC0020;

    static void Main() {
        var sortedScreens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();
        for (int i = 0; i < sortedScreens.Length; i++) {
            var s = sortedScreens[i];
            Console.WriteLine("Capturing screen " + i + ": " + s.DeviceName + " " + s.Bounds);
            IntPtr hdcSrc = CreateDC(s.DeviceName, null, null, IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) {
                Console.WriteLine("Failed to create DC for " + s.DeviceName);
                continue;
            }

            using (Bitmap bmp = new Bitmap(s.Bounds.Width, s.Bounds.Height, PixelFormat.Format32bppArgb)) {
                using (Graphics gDest = Graphics.FromImage(bmp)) {
                    IntPtr hdcDest = gDest.GetHdc();
                    // Each screen's DC has origin (0, 0)!
                    BitBlt(hdcDest, 0, 0, s.Bounds.Width, s.Bounds.Height, hdcSrc, 0, 0, SRCCOPY);
                    gDest.ReleaseHdc(hdcDest);
                }
                DeleteDC(hdcSrc);
                bmp.Save("test_dev_" + i + ".jpg", ImageFormat.Jpeg);
                Console.WriteLine("Saved test_dev_" + i + ".jpg, size: " + new FileInfo("test_dev_" + i + ".jpg").Length);
            }
        }
    }
}
