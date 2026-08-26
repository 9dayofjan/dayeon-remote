using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TestInputDesktop {
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    const uint GENERIC_ALL = 0x10000000;
    const int SRCCOPY = 0x00CC0020;

    static void Main() {
        SetProcessDPIAware();

        IntPtr hDesk = OpenInputDesktop(0, false, GENERIC_ALL);
        if (hDesk != IntPtr.Zero) {
            SetThreadDesktop(hDesk);
            Console.WriteLine("Attached to OpenInputDesktop!");
        } else {
            Console.WriteLine("OpenInputDesktop failed: " + Marshal.GetLastWin32Error());
        }

        IntPtr hdcSrc = GetDC(IntPtr.Zero);
        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();

        for (int i = 0; i < screens.Length; i++) {
            var s = screens[i];
            using (Bitmap bmp = new Bitmap(s.Bounds.Width, s.Bounds.Height, PixelFormat.Format32bppArgb)) {
                using (Graphics gDest = Graphics.FromImage(bmp)) {
                    IntPtr hdcDest = gDest.GetHdc();
                    BitBlt(hdcDest, 0, 0, s.Bounds.Width, s.Bounds.Height, hdcSrc, s.Bounds.Left, s.Bounds.Top, SRCCOPY);
                    gDest.ReleaseHdc(hdcDest);
                }

                int nonBlack = 0;
                for (int y = 0; y < bmp.Height; y += 50) {
                    for (int x = 0; x < bmp.Width; x += 50) {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R > 10 || c.G > 10 || c.B > 10) nonBlack++;
                    }
                }

                bmp.Save("inputdesk_mon_" + i + ".jpg", ImageFormat.Jpeg);
                Console.WriteLine(string.Format("Screen {0} ({1}): nonBlack={2} Bounds={3}", i, s.DeviceName, nonBlack, s.Bounds));
            }
        }

        ReleaseDC(IntPtr.Zero, hdcSrc);
        if (hDesk != IntPtr.Zero) CloseDesktop(hDesk);
    }
}
