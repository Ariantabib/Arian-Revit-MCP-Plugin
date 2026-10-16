namespace Arian.RevitMcp.Core;

/// <summary>Tiny UI localisation (English / Persian). Agent-facing tools stay English.</summary>
public static class Loc
{
    static readonly Dictionary<string, string> Fa = new()
    {
        ["MCP Server"] = "سرور MCP",
        ["Start\nServer"] = "شروع\nسرور",
        ["Stop Server\n:{0}"] = "توقف سرور\n:{0}",
        ["Connect\nAgent"] = "اتصال\nایجنت",
        ["Settings"] = "تنظیمات",
        ["Automation"] = "خودکارسازی",
        ["Warnings:\nDefault"] = "وارنینگ:\nپیش‌فرض",
        ["Warnings:\nIgnore"] = "وارنینگ:\nنادیده",
        ["Warnings:\nIgnore+Fix"] = "وارنینگ:\nنادیده+رفع",
        ["Auto Dialogs:\nOn"] = "پنجره‌ها:\nخودکار",
        ["Auto Dialogs:\nOff"] = "پنجره‌ها:\nدستی",
        ["Dismiss\nDialogs"] = "بستن\nپنجره‌ها",
        ["Run Dynamo\nGraph"] = "اجرای\nDynamo",
        ["Model\nWarnings"] = "وارنینگ‌های\nمدل",
        ["Monitor"] = "نظارت",
        ["Agent\nPanel"] = "پنل\nایجنت",
        ["Undo\nAgent"] = "بازگردانی\nایجنت",
        ["Cancel\nTask"] = "لغو\nعملیات",
        ["Help"] = "راهنما",
        ["Open\nLogs"] = "باز کردن\nلاگ",
        ["About"] = "درباره",
        ["Arian Agent"] = "ایجنت آرین",
        ["Server running"] = "سرور فعال است",
        ["Server stopped"] = "سرور متوقف است",
        ["tools"] = "ابزار",
        ["calls"] = "فراخوانی",
        ["failed"] = "ناموفق",
        ["Idle"] = "آماده",
        ["Running"] = "در حال اجرا",
        ["Activity"] = "فعالیت",
        ["Images"] = "تصاویر",
        ["Log"] = "لاگ",
        ["Undo last change"] = "بازگردانی آخرین تغییر",
        ["Cancel running task"] = "لغو عملیات جاری",
        ["Clear"] = "پاک کردن",
        ["Time"] = "زمان",
        ["Tool"] = "ابزار",
        ["Status"] = "وضعیت",
        ["Start server"] = "شروع سرور",
        ["Stop server"] = "توقف سرور",
        ["Save"] = "ذخیره",
        ["Cancel"] = "انصراف",
        ["Language"] = "زبان",
        ["Profile"] = "پروفایل ابزارها",
        ["Tool categories (tick the ones the agent may see)"] = "دسته‌های ابزار (آنهایی که ایجنت می‌بیند)",
        ["Read-only categories (agent cannot change the model with these)"] = "دسته‌های فقط‌خواندنی (ایجنت با آنها مدل را تغییر نمی‌دهد)",
        ["Port (restart the server to apply)"] = "پورت (برای اعمال سرور را دوباره شروع کنید)",
        ["Start the MCP server automatically when Revit starts"] = "شروع خودکار سرور هنگام اجرای Revit",
        ["Allow the agent to run C# code inside Revit (execute_csharp)"] = "اجازهٔ اجرای کد C# توسط ایجنت (execute_csharp)",
        ["Automatically answer Revit task dialogs (least destructive choice)"] = "پاسخ خودکار به پنجره‌های Revit (کم‌خطرترین گزینه)",
        ["Warnings / errors policy"] = "سیاست وارنینگ و خطا",
        ["Tool timeout (seconds)"] = "مهلت اجرای ابزار (ثانیه)",
        ["Optional access token (leave empty for none; loopback only anyway)"] = "توکن دسترسی (اختیاری)",
        ["Restart Revit to refresh the ribbon language."] = "برای تغییر زبان نوار ابزار، Revit را دوباره باز کنید.",
    };

    public static bool IsFa => string.Equals(PluginSettings.Current.Language, "fa", StringComparison.OrdinalIgnoreCase);

    public static string T(string key, params object[] args)
    {
        string s = key;
        if (IsFa && Fa.TryGetValue(key, out var f)) s = f;
        return args.Length > 0 ? string.Format(s, args) : s;
    }
}
