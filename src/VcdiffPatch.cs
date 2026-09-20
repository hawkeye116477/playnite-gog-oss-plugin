using Playnite.Common;
using Playnite.SDK;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GogOssLibraryNS
{
    public class VcdiffPatch
    {
        [DllImport("NativeVcdiffPatch.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "start_patching", CharSet = CharSet.Ansi)]
        internal static extern int start_patching(string oldFileName, string diffFileName, string outNewFileName, UIntPtr patchCacheSize, ProgressCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ProgressCallback(ulong writtenBytes, ulong totalBytes);
    }
}
