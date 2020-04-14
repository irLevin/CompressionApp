using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CompressionApp
{
    public class Utils
    {
        public static readonly Regex driveStart = new Regex(@"^[a-zA-Z]{1}:\\.*$");
        public static bool IsFullPath(string path) => driveStart.IsMatch(path);

        public static void SetThreadAffinityForCurrentThread(IntPtr mask)
        {
            if (mask != IntPtr.Zero && (mask.ToInt64() & Process.GetCurrentProcess().ProcessorAffinity.ToInt64()) > 0 )
            {
                Thread.BeginThreadAffinity();
                IntPtr osThread = Native32.GetCurrentThread();
                IntPtr lastaffinity = Native32.SetThreadAffinityMask(osThread, mask);
                if (lastaffinity == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }
    }
}
