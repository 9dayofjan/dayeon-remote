using System;
using System.Diagnostics;
using System.IO;
using System.Text;

class ScrcpyWrapper
{
    static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string targetExe = Path.Combine(dir, "scrcpy_core.exe");
        if (!File.Exists(targetExe))
        {
            return 1;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("--max-fps=30 --render-driver=direct3d11 --video-bit-rate=4M ");

        foreach (string a in args)
        {
            if (a.Contains(" "))
                sb.Append("\"" + a + "\" ");
            else
                sb.Append(a + " ");
        }

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = targetExe,
            Arguments = sb.ToString().Trim(),
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch
        {
            return 1;
        }
        }
}
