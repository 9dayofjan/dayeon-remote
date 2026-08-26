using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

class TestCopy {
    static void Main() {
        var sortedScreens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();
        for (int i = 0; i < sortedScreens.Length; i++) {
            var s = sortedScreens[i];
            using (Bitmap bmp = new Bitmap(s.Bounds.Width, s.Bounds.Height, PixelFormat.Format32bppArgb)) {
                using (Graphics g = Graphics.FromImage(bmp)) {
                    g.CopyFromScreen(s.Bounds.Location, Point.Empty, s.Bounds.Size);
                }
                bmp.Save("test_mon_" + i + ".jpg", ImageFormat.Jpeg);
                Console.WriteLine("Saved test_mon_" + i + ".jpg from " + s.DeviceName + " " + s.Bounds);
            }
        }
    }
}
