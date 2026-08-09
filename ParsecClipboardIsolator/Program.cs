using System;
using ParsecClipboardIsolator;

Console.Title = "Parsec Clipboard Isolator";
Console.CursorVisible = false;

using var isolator = new ParsecIsolator();
var globalView = new GlobalView();
var targetedView = new TargetedView();
IView activeView = globalView;

string? defaultProfile = ProfileManager.GetDefaultProfile();
if (defaultProfile != null)
{
    var paths = ProfileManager.LoadProfile(defaultProfile);
    isolator.LoadTargetedBlockedPaths(paths);
    
    // Автоматически переходим в Таргетный режим при наличии дефолтного профиля
    isolator.SetMode(IsolationMode.Targeted);
    activeView = targetedView;
}

ParsecIsolator.RefreshResult initResult;
try
{
    initResult = isolator.Initialize();
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[!] Критическая ошибка инициализации: {ex.Message}");
    Console.ResetColor();
    Console.ReadLine();
    return;
}

// Гарантируем откат байт OpenClipboard и закрытие дескрипторов при любом способе выхода из программы
AppDomain.CurrentDomain.ProcessExit += (s, e) => isolator.Dispose();
Console.CancelKeyPress += (s, e) => isolator.Dispose();

activeView.DrawFull(isolator);

if (initResult.SkippedDueToArch > 0)
{
    activeView.ShowFeedback($"ВНИМАНИЕ: Пропущено окон: {initResult.SkippedDueToArch} (разная разрядность!).", ConsoleColor.Red);
}
else if (isolator.TrackedProcessesCount == 0)
{
    activeView.ShowFeedback("Parsec не запущен. Запустите Parsec и нажмите [R].", ConsoleColor.Yellow);
}

while (true)
{
    var key = Console.ReadKey(intercept: true);
    
    if (key.Key == ConsoleKey.Escape)
    {
        activeView.ShowFeedback("Завершение работы программы...", ConsoleColor.Cyan);
        return;
    }
    
    if (key.Key == ConsoleKey.R)
    {
        var result = isolator.Refresh();
        
        string archWarning = result.SkippedDueToArch > 0 
            ? $" (Пропущено {result.SkippedDueToArch} из-за разрядности!)" 
            : "";
            
        string removedInfo = result.Removed > 0 
            ? $". Очищено: {result.Removed}" 
            : "";
            
        string feedback = $"[+] Обновлено. Новых: {result.NewlyAttached}{removedInfo}{archWarning}";
        ConsoleColor color = result.SkippedDueToArch > 0 ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
        
        activeView.UpdateDynamic(isolator);
        activeView.ShowFeedback(feedback, color);
        continue;
    }

    if (key.Key == ConsoleKey.LeftArrow && activeView is TargetedView)
    {
        isolator.SetMode(IsolationMode.Global);
        activeView = globalView;
        activeView.DrawFull(isolator);
        continue;
    }
    
    if (key.Key == ConsoleKey.RightArrow && activeView is GlobalView)
    {
        isolator.SetMode(IsolationMode.Targeted);
        activeView = targetedView;
        activeView.DrawFull(isolator);
        continue;
    }

    activeView.HandleKey(key, isolator);
}
