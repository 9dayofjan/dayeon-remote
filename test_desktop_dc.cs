using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TestDesktopDC {
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
        IntPtr hwnd = GetDesktopWindow();
        IntPtr hdcSrc = GetWindowDC(hwnd);

        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();
        for (int i = 0; i < screens.Length; i++) {
            var s = screens[i];
            Console.WriteLine(string.Format("Screen {0}: {1} Bounds={2}", i, s.DeviceName, s.Bounds));
            using (Bitmap bmp = new Bitmap(s.Bounds.Width, s.Bounds.Height, PixelFormat.Format32bppArgb)) {
                using (Graphics g = Graphics.FromImage(bmp)) {
                    IntPtr hdcDest = g.GetHdc();
                    // In Desktop Window DC, coordinates are VirtualScreen (s.Bounds.Left, s.Bounds.Top)
                    BitBlt(hdcDest, 0, 0, s.Bounds.Width, s.Bounds.Height, hdcSrc, s.Bounds.Left, s.Bounds.Top, SRCCOPY);
                    g.ReleaseHdc(hdcDest);
                }
                bmp.Save("desktop_dc_" + i + ".jpg", ImageFormat.Jpeg);
            }
        }
        ReleaseDC(hwnd, hdcSrc);
    }
}
