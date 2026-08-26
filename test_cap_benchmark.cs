using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class TestCap {
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
    const int SRCCOPY = 0x00CC0020;

    static ImageCodecInfo GetEncoder(ImageFormat format) {
        foreach (var c in ImageCodecInfo.GetImageDecoders()) {
            if (c.FormatID == format.Guid) return c;
        }
        return null;
    }

    static void Main() {
        IntPtr hdcSrc = GetDC(IntPtr.Zero);
        int w = 1920, h = 1088;
        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        Graphics g = Graphics.FromImage(bmp);
        
        var encoder = GetEncoder(ImageFormat.Jpeg);
        var encParams = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
        MemoryStream ms = new MemoryStream(256 * 1024);

        Stopwatch sw = new Stopwatch();
        for (int i = 0; i < 20; i++) {
            sw.Restart();
            IntPtr hdcDest = g.GetHdc();
            BitBlt(hdcDest, 0, 0, w, h, hdcSrc, 0, 0, SRCCOPY);
            g.ReleaseHdc(hdcDest);
            long bitBltMs = sw.ElapsedMilliseconds;

            ms.SetLength(0);
            bmp.Save(ms, encoder, encParams);
            long totalMs = sw.ElapsedMilliseconds;

            Console.WriteLine("Frame " + i + ": BitBlt = " + bitBltMs + "ms, Total = " + totalMs + "ms (size: " + ms.Length + " bytes)");
        }
        ReleaseDC(IntPtr.Zero, hdcSrc);
    }
}
