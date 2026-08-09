namespace ParsecClipboardIsolator.Models;

// Режим работы изоляции буфера обмена
internal enum IsolationMode
{
    // Блокировка применяется ко всем обнаруженным окнам Parsec одновременно
    Global,

    // Блокировка настраивается индивидуально по путям исполняемых файлов
    Targeted
}
