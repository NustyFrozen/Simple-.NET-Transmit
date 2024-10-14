using ClickableTransparentOverlay;
using Signal_Generator;
using System.Runtime.InteropServices;
[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool AllocConsole();
AllocConsole();
Transmission.beginStream();
await new UI().Start();
