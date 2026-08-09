using System;

namespace ParsecClipboardIsolator.Models;

// Кэшированные данные о процессе Parsec и открытом Win32-дескрипторе
internal sealed record ParsecProcessInfo(
    int Pid, 
    IntPtr ProcessHandle, 
    IntPtr MainWindowHandle, 
    string ExecutablePath
);
