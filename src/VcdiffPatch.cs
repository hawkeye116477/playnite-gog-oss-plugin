using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Playnite.SDK;

namespace GogOssLibraryNS
{
    public class VcdiffPatch
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public const string LibraryName = "NativeVcdiffPatch";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "start_patching", CharSet = CharSet.Ansi)]
        internal static extern int start_patching(
            string oldFileName, string diffFileName, string outNewFileName, UIntPtr patchCacheSize, ProgressCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ProgressCallback(ulong writtenBytes, ulong totalBytes);


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public static bool CanLoad()
        {
            bool available = true;
            var handle = LoadLibrary(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), LibraryName));

            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                logger.Error($"Can't load {LibraryName} library. Error code: {error}");
                available = false;
            }
            else
            {
                FreeLibrary(handle);
            }

            return available;
        }
    }
}