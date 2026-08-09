using System;

namespace ParsecClipboardIsolator
{
    // Режим работы изоляции буфера обмена
    internal enum IsolationMode
    {
        // Блокировка применяется ко всем обнаруженным окнам Parsec одновременно
        Global,

        // Блокировка настраивается индивидуально по путям исполняемых файлов
        Targeted
    }

    // Кэшированные данные о процессе Parsec и открытом Win32-дескрипторе
    internal sealed record ParsecProcessInfo(
        int Pid, 
        IntPtr ProcessHandle, 
        IntPtr MainWindowHandle, 
        string ExecutablePath
    );
}
