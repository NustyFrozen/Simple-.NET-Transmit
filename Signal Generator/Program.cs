using ClickableTransparentOverlay;
using Signal_Generator;
using System.Runtime.InteropServices;
[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool AllocConsole();
AllocConsole();
Transmission.beginStream();

if(File.Exists("cal.csv"))
{
    string[] data = File.ReadAllText("cal.csv").Split('\n');
    UI.hasCalibration = true;
    for(int i = 0;i<data.Length;i++)
    {
        var tempData = data[i].Split(',');
        if (tempData.Length == 0) break;
        UI.caliData.Add(Convert.ToDouble(tempData[0]), Convert.ToDouble(tempData[1]));
    }
}
await new UI().Start();
