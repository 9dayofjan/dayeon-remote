using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TestVirtualSpan {
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    const int SRCCOPY = 0x00CC0020;

    static void Main() {
        SetProcessDPIAware();

        Rectangle vBounds = SystemInformation.VirtualScreen;
        Console.WriteLine("VirtualScreen: " + vBounds);

        IntPtr hwnd = GetDesktopWindow();
        IntPtr hdcSrc = GetWindowDC(hwnd);

        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();

        using (Bitmap fullBmp = new Bitmap(vBounds.Width, vBounds.Height, PixelFormat.Format32bppArgb)) {
            using (Graphics gFull = Graphics.FromImage(fullBmp)) {
                IntPtr hdcDest = gFull.GetHdc();
                BitBlt(hdcDest, 0, 0, vBounds.Width, vBounds.Height, hdcSrc, vBounds.Left, vBounds.Top, SRCCOPY);
                gFull.ReleaseHdc(hdcDest);
            }
            ReleaseDC(hwnd, hdcSrc);

            fullBmp.Save("full_span.jpg", ImageFormat.Jpeg);
            Console.WriteLine("Saved full_span.jpg, size: " + new FileInfo("full_span.jpg").Length);

            for (int i = 0; i < screens.Length; i++) {
                var s = screens[i];
                int offsetX = s.Bounds.Left - vBounds.Left;
                int offsetY = s.Bounds.Top - vBounds.Top;
                Console.WriteLine(string.Format("Screen {0} ({1}): offset=({2}, {3}), size=({4}, {5})", i, s.DeviceName, offsetX, offsetY, s.Bounds.Width, s.Bounds.Height));

                Rectangle cropRect = new Rectangle(offsetX, offsetY, s.Bounds.Width, s.Bounds.Height);
                using (Bitmap subBmp = fullBmp.Clone(cropRect, fullBmp.PixelFormat)) {
                    subBmp.Save("sliced_mon_" + i + ".jpg", ImageFormat.Jpeg);
                    Console.WriteLine("Saved sliced_mon_" + i + ".jpg, size: " + new FileInfo("sliced_mon_" + i + ".jpg").Length);
                }
            }
        }
    }
}
