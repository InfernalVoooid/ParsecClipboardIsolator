using System;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ParsecClipboardIsolator
{
    // Низкоуровневые Win32 P/Invoke функции для патчинга памяти и управления окнами
    internal static partial class Native
    {
        // Права доступа к процессу для записи и смены защиты страниц памяти
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        
        // Флаг временной защиты памяти для записи машинного кода в страницу кода
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        
        // Команда разворачивания свернутого окна при фокусе ("прозвон")
        public const int SW_RESTORE = 9;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, in byte lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr LoadLibrary(string lpFileName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool FreeLibrary(IntPtr hModule);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr GetProcAddress(IntPtr hModule, string procName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, Span<char> lpExeName, ref uint lpdwSize);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
