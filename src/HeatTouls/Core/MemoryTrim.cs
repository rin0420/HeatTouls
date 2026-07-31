using System.Runtime;
using System.Runtime.InteropServices;

namespace HeatTouls.Core;

/// <summary>
/// Hands memory back to the OS when the dashboard is put away.
///
/// This is a tray-resident app: it spends most of its life with no window on screen, and the
/// heatmap rows it built are worth hundreds of megabytes of Win2D surfaces. Collecting and then
/// trimming the working set turns that from "resident all day" into "paged back in when shown".
/// </summary>
internal static class MemoryTrim
{
    /// <summary>
    /// Passing -1 for both bounds tells Windows to trim the working set as far as it can. The
    /// pages are not lost — they fault back in on next use, which only happens when the window
    /// is shown again.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public static void Release()
    {
        try
        {
            // Compact the large object heap too: the Win2D bitmaps and the per-app daily
            // dictionaries land there, and without compaction the freed space stays reserved.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch (Exception)
        {
            // Purely an optimisation — if the runtime refuses, the app just keeps its pages.
        }
    }
}
