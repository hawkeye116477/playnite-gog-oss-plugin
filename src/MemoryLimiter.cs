using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GogOssLibraryNS
{
    public sealed class MemoryLimiter : IDisposable
    {
        private readonly long _maxBytes;
        private long _currentUsage;
        private int _disposed; // 0 = false, 1 = true

        public long CurrentUsage => Interlocked.Read(ref _currentUsage);
        public long MaxBytes => _maxBytes;

        public MemoryLimiter(long maxBytes)
        {
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "MaxBytes must be positive.");
            }

            _maxBytes = maxBytes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public bool TryReserve(long bytes)
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(MemoryLimiter));
            }

            if (bytes <= 0)
            {
                return true;
            }

            while (true)
            {
                long current = Interlocked.Read(ref _currentUsage);
                long newTotal = current + bytes;

                MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

                long systemAvailBytes = 0;

                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    systemAvailBytes = (long)memStatus.ullAvailPhys;
                }

                if (newTotal > systemAvailBytes || newTotal > _maxBytes)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _currentUsage, newTotal, current) == current)
                {
                    return true;
                }
            }
        }

        public void Release(long bytesToRelease)
        {
            if (bytesToRelease <= 0)
            {
                return;
            }

            while (true)
            {
                long current = Interlocked.Read(ref _currentUsage);
                long newTotal = current - bytesToRelease;

                if (newTotal < 0) newTotal = 0;

                if (Interlocked.CompareExchange(ref _currentUsage, newTotal, current) == current)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Exchange(ref _currentUsage, 0);
            }
            GC.SuppressFinalize(this);
        }
    }

}