# EzEizoV2

גרסת Windows ‏2.5.1 של EzEizo: שתי תוכנות מקומיות, מנהל וטכנאי, עם ממשק Windows מקורי (WinForms) ומסד Access משותף. אין WebView2, דפדפן, Firebase או דרישה לאינטרנט במהלך השימוש. קוד המקור נמצא ב־`src/EzEizo.Native*` ודרישות ההתקנה ב־[INSTALL-WINDOWS.md](INSTALL-WINDOWS.md).

הורד את `EzEizo-Windows-2.5.1.zip` מ־[Releases](https://github.com/cartonush/EzEizoV2/releases). חלץ את תיקיית `EzEizo` לשולחן העבודה, התקן Microsoft Access Database Engine/Runtime x64 אם הוא חסר, והפעל ישירות את `Metis.exe` או `Mechanic.exe` בשורש התיקייה. רכיבי התוכנה נמצאים ב־`program`; שני היישומים משתמשים בקובץ `Desktop/EzEizo/data/EzEizo.accdb`.

המסד המצורף הוא תבנית הדגמה עם חשבון המנהל הראשי רועי סויסה (`9862436`) וסיסמה ראשונית `111111`. בעת עדכון, שמור את תיקיית `data` הישנה במקום התבנית המצורפת; אחרת תאבד את המשתמשים והטפסים שלך. אין להשתמש בסיסמת ההדגמה בסביבת עבודה אמיתית.

האתר המקורי 1.6.3 גובה בענף `backup-v1.6.3-2026-11-20` של [EzEizo](https://github.com/cartonush/EzEizo), commit `d3c7efa8eac925aa206a4fda40f086fa27001a99`. מסד הדוגמה והיישום מיועדים לבחינת אב טיפוס, ללא נתוני אמת.
