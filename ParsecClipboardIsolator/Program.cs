using System;
using ParsecClipboardIsolator;

Console.Title = "Parsec Clipboard Isolator";
Console.CursorVisible = false;

using var isolator = new ParsecIsolator();
var dashboard = new DashboardView();

ParsecIsolator.RefreshResult initResult;
try
{
    initResult = isolator.Initialize();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[!] Критическая ошибка инициализации: {ex.Message}");
    Console.ResetColor();
    Console.ReadLine();
    return;
}

AppDomain.CurrentDomain.ProcessExit += (s, e) => isolator.Dispose();
Console.CancelKeyPress += (s, e) => isolator.Dispose();

dashboard.DrawInitialDashboard(isolator.TrackedProcessesCount);
dashboard.UpdateStatus(isolator.IsActive, isolator.TrackedProcessesCount);

if (initResult.SkippedDueToArch > 0)
{
    dashboard.ShowFeedback($"ВНИМАНИЕ: Пропущено окон: {initResult.SkippedDueToArch} (разная разрядность!).", ConsoleColor.Red);
}
else if (isolator.TrackedProcessesCount == 0)
{
    dashboard.ShowFeedback("Parsec не запущен. Запустите Parsec и нажмите [R].", ConsoleColor.Yellow);
}

while (true)
{
    var key = Console.ReadKey(intercept: true);
    
    switch (key.Key)
    {
        case ConsoleKey.Enter:
            isolator.SetBlockState(!isolator.IsActive);
            dashboard.UpdateStatus(isolator.IsActive, isolator.TrackedProcessesCount);
            dashboard.ClearFeedback();
            break;

        case ConsoleKey.R:
            var result = isolator.Refresh();
            
            string archWarning = result.SkippedDueToArch > 0 
                ? $" (Пропущено {result.SkippedDueToArch} из-за разрядности!)" 
                : "";
                
            string removedInfo = result.Removed > 0 
                ? $". Очищено: {result.Removed}" 
                : "";
                
            string feedback = $"[+] Обновлено. Новых: {result.NewlyAttached}{removedInfo}{archWarning}";
            ConsoleColor color = result.SkippedDueToArch > 0 ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            
            dashboard.UpdateTrackedCount(isolator.TrackedProcessesCount);
            dashboard.UpdateStatus(isolator.IsActive, isolator.TrackedProcessesCount);
            dashboard.ShowFeedback(feedback, color);
            break;

        case ConsoleKey.Escape:
            dashboard.ShowFeedback("Завершение работы программы...", ConsoleColor.Cyan);
            return;
    }
}
