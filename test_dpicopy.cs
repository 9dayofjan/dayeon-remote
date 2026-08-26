using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TestDpiCopy {
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static void Main() {
        SetProcessDPIAware();

        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();
        for (int i = 0; i < screens.Length; i++) {
            var s = screens[i];
            try {
                using (Bitmap bmp = new Bitmap(s.Bounds.Width, s.Bounds.Height, PixelFormat.Format32bppArgb)) {
                    using (Graphics g = Graphics.FromImage(bmp)) {
                        g.CopyFromScreen(s.Bounds.Location, Point.Empty, s.Bounds.Size, CopyPixelOperation.SourceCopy);
                    }
                    bmp.Save("dpicopy_mon_" + i + ".jpg", ImageFormat.Jpeg);
                    Console.WriteLine(string.Format("Success screen {0}: {1} Bounds={2} size={3}", i, s.DeviceName, s.Bounds, new FileInfo("dpicopy_mon_" + i + ".jpg").Length));
                }
            } catch (Exception ex) {
                Console.WriteLine("Error screen " + i + ": " + ex.Message);
            }
        }
    }
}
