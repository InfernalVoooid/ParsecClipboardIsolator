namespace ParsecClipboardIsolator.Models;

// Результат сканирования процессов Parsec при обновлении списка
internal sealed record RefreshResult(
    int NewlyAttached, 
    int SkippedDueToArch, 
    int Removed,
    int Failed
);
