# EzEizoV2

גרסת Windows מקומית לטכנאי ולמנהל. פיתוח נפרד מ־[EzEizo](https://github.com/cartonush/EzEizo).

הגיבוי המדויק של גרסת האתר 1.6.3: `EzEizo` branch `backup-v1.6.3-2026-11-20`, commit `d3c7efa8eac925aa206a4fda40f086fa27001a99`.

## גרסת Windows ראשונית

קוד המקור לשני יישומי WinForms x64 נמצא ב־`src/`. תהליך הבנייה ב־`.github/workflows/windows-build.yml` יוצר חבילה מלאה להורדה עם `EzEizo-Metis.exe`, `EzEizo-Mechanic.exe`, הממשק המקומי ו־`data/EzEizo.accdb` התחלתי עם חשבון מנהל לדוגמה. הוראות והיקף ההדגמה מפורטים ב־`INSTALL-WINDOWS.md`.

מסכי האתר 1.6.3 הועתקו ל־`wwwroot` והותאמו לנתונים מקומיים ב־Access; טפסים וטיוטות נשמרים ב־ACCDB לפי משתמש. זו גרסת אב טיפוס לבדיקה; הממשק משתמש ב־WebView2 להצגת HTML מקומי. בדיקות הבנייה כוללות בדיקת מסכים בדפדפן ובדיקת מסד לתהליך כניסה, תקלה, מפתחות וקבועים. אין להעלות מסד עם נתוני אמת או חתימות למאגר ציבורי.
