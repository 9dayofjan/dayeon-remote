using System;
using System.Windows.Forms;

class CheckScreens {
    static void Main() {
        Console.WriteLine("Total screens: " + Screen.AllScreens.Length);
        for (int i = 0; i < Screen.AllScreens.Length; i++) {
            var s = Screen.AllScreens[i];
            Console.WriteLine(string.Format("Screen {0}: Name={1}, Bounds={2}, Primary={3}", i, s.DeviceName, s.Bounds, s.Primary));
        }
    }
}
