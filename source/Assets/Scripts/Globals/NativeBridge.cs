using System;
using System.Runtime.InteropServices;

internal static class NativeBridge
{
    internal enum DownloadState
    {
        Idle = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4
    }

    [DllImport("UnityOrbisBridge")]
    internal static extern int ConfigureHttp(string caBundlePath);

    [DllImport("UnityOrbisBridge")]
    internal static extern int InitializePackageServices();

    [DllImport("UnityOrbisBridge")]
    internal static extern int StartDirectDownload(string url, string finalPath);

    [DllImport("UnityOrbisBridge")]
    private static extern int GetDirectDownloadState();

    [DllImport("UnityOrbisBridge")]
    internal static extern int GetDirectDownloadErrorCode();

    [DllImport("UnityOrbisBridge")]
    internal static extern int GetDirectDownloadHttpStatus();

    [DllImport("UnityOrbisBridge")]
    internal static extern ulong GetDirectDownloadTotalBytes();

    [DllImport("UnityOrbisBridge")]
    internal static extern ulong GetDirectDownloadCurrentBytes();

    [DllImport("UnityOrbisBridge")]
    internal static extern ulong GetDirectDownloadSpeedBytesPerSecond();

    [DllImport("UnityOrbisBridge")]
    internal static extern void CancelDirectDownload();

    [DllImport("UnityOrbisBridge")]
    internal static extern int ResetDirectDownload();

    [DllImport("UnityOrbisBridge")]
    internal static extern ulong GetDiskFreeBytes(string mountPoint);

    [DllImport("UnityOrbisBridge")]
    internal static extern int InstallLocalPackageResult(
        string filePath,
        string name,
        string iconUri,
        [MarshalAs(UnmanagedType.I1)] bool deleteAfter);

    [DllImport("UnityOrbisBridge")]
    internal static extern int InstallWebPackageResult(
        string url,
        string name,
        string titleId,
        string iconUri);

    internal static DownloadState CurrentDownloadState
    {
        get { return (DownloadState)GetDirectDownloadState(); }
    }

    internal static bool IsTerminal(DownloadState state)
    {
        return state == DownloadState.Succeeded ||
               state == DownloadState.Failed ||
               state == DownloadState.Cancelled;
    }
}
