using System.Globalization;

namespace ArzType.App;

/// <summary>UI text in English and Arabic (chosen from the Windows display language).</summary>
internal static class S
{
    public static readonly bool Ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
    private static string T(string en, string ar) => Ar ? ar : en;

    public static string Tagline => T("Typed in the wrong keyboard language? It's fixed the moment you press Space.",
        "كتبت والكيبورد على اللغة الغلط؟ بينصلح لحالو أول ما تكبس مسافة.");
    public static string AutoFix => T("Auto-fix", "التصحيح التلقائي");
    public static string On => T("ON", "شغّال");
    public static string Off => T("OFF", "متوقف");
    public static string Active => T("Active — watching for wrong-layout words", "شغّال — بيراقب الكلمات المكتوبة على اللغة الغلط");
    public static string Paused => T("Paused — turn on to start fixing", "متوقف — شغّلو ليبلّش يصحّح");

    public static string HowTitle => T("How it works", "كيف بيشتغل");
    public static string How1 => T("Just type. A word typed on the wrong layout is replaced and the keyboard switches for you.",
        "اكتب عادي. الكلمة اللي بتنكتب على اللغة الغلط بتتبدّل والكيبورد بيتحوّل لحالو.");
    public static string How2 => T("Tap Shift twice (or Pause) to fix the last word yourself — or to undo a fix.",
        "اكبس Shift مرتين (أو Pause) لتصلّح آخر كلمة بنفسك — أو لترجّع التصحيح.");
    public static string How3 => T("It learns: undo a fix once and that word is left alone from then on.",
        "بيتعلّم: إذا رجّعت تصحيح مرة، ما بيرجع يغيّر هالكلمة أبدًا.");

    public static string KeyboardsTitle => T("Your keyboards", "لوحات المفاتيح عندك");
    public static string Smart => T("Smart detection", "كشف ذكي");
    public static string ManualOnly => T("Manual fix (hotkey)", "تصحيح يدوي (اختصار)");
    public static string Ime => T("IME — not touched", "IME — ما بيتلمس");
    public static string Loading => T("loading…", "عم يحمّل…");
    public static string AddKeyboardHint => T("Add keyboards in Windows Settings › Time & language › Language & region.",
        "زيد لوحات مفاتيح من إعدادات ويندوز › الوقت واللغة › اللغة والمنطقة.");

    public static string OptionsTitle => T("Options", "الخيارات");
    public static string StartWithWindows => T("Start with Windows", "يشتغل مع ويندوز");
    public static string FixOnEnter => T("Also fix when pressing Enter", "صحّح كمان لما تكبس Enter");
    public static string DoubleShift => T("Shift + Shift fixes the last word", "Shift مرتين بيصلّح آخر كلمة");
    public static string PauseKey => T("Pause/Break key fixes the last word", "زر Pause بيصلّح آخر كلمة");
    public static string Learn => T("Learn from my fixes", "تعلّم من تصحيحاتي");
    public static string AllApps => T("Work in apps that hide their text fields (advanced)", "اشتغل بالتطبيقات اللي ما بتعرّف حقول النص (متقدم)");
    public static string Sensitivity => T("Sensitivity", "الحساسية");
    public static string Careful => T("Careful", "حذر");
    public static string Balanced => T("Balanced", "متوازن");
    public static string Eager => T("Eager", "جريء");

    public static string Stats(int session, long total, long keys) =>
        T($"Fixed this session: {session}   ·   All time: {total:N0}   ·   Keystrokes saved: {keys:N0}",
          $"تصحيحات هالجلسة: {session}   ·   المجموع: {total:N0}   ·   كبسات وفّرتها: {keys:N0}");
    public static string ExcludedApps => T("Excluded apps…", "تطبيقات مستثناة…");
    public static string Forget(int n) => T($"Forget learned words ({n})", $"انسَ الكلمات المتعلَّمة ({n})");
    public static string PacksFolder => T("My word lists…", "قوائم كلماتي…");
    public static string Privacy => T("100% offline. No internet, no cloud, nothing you type is saved or sent. Password fields, terminals, code editors and admin windows are never touched.",
        "أوفلاين ١٠٠٪. لا إنترنت ولا سيرفر، وما في شي من اللي بتكتبو بينحفظ أو بينبعت. حقول كلمات السر والطرفيات ومحررات الكود ونوافذ المسؤول ما بتنلمس أبدًا.");

    public static string TrayShow => T("Open ArzType", "افتح ArzType");
    public static string TrayToggle => T("Auto-fix", "التصحيح التلقائي");
    public static string TrayExclude(string app) => T($"Never fix in {app}", $"لا تصحّح أبدًا في {app}");
    public static string TrayQuit => T("Quit", "خروج");
    public static string RunningInTray => T("ArzType keeps working from the tray.", "ArzType مكمّل شغلو من شريط المهام.");
    public static string ExcludedTitle => T("Apps where ArzType never changes anything (one per line, process name):",
        "التطبيقات اللي ArzType ما بيغيّر فيها شي (واحد بكل سطر، اسم العملية):");
    public static string Save => T("Save", "حفظ");
    public static string Cancel => T("Cancel", "إلغاء");
    public static string ConfirmForget => T("Forget every word ArzType learned from your fixes?", "بدك ArzType ينسى كل الكلمات اللي تعلّمها من تصحيحاتك؟");
    public static string HookFailed(string m) => T($"ArzType could not start its keyboard hook: {m}", $"ما قدر ArzType يشغّل مراقبة الكيبورد: {m}");
    public static string Welcome => T("Welcome! Turn on Auto-fix, then type a word on the wrong keyboard and press Space.",
        "أهلا! شغّل التصحيح التلقائي، وجرّب تكتب كلمة والكيبورد على اللغة الغلط واكبس مسافة.");
}
