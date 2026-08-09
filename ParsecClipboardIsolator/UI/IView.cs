using System;
using ParsecClipboardIsolator.Services;

namespace ParsecClipboardIsolator.UI;

// Общий контракт для консольных режимов отображения (Global / Targeted)
internal interface IView
{
    void DrawFull(ParsecIsolator isolator);
    void UpdateDynamic(ParsecIsolator isolator);
    void HandleKey(ConsoleKeyInfo key, ParsecIsolator isolator);
    void ShowFeedback(string message, ConsoleColor color);
    void ClearFeedback();
}
