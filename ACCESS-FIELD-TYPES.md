# מיפוי שדות Access לגרסת Windows הראשונית

המיפוי מבוסס על `DATABASE-TABLES.md` של EzEizo. הוא מיועד לקובץ `.accdb` חדש, ללא נתונים שצריך להמיר. יש לגבות את הקובץ לפני שינוי סוגי שדות. **אין להעלות קובץ נתונים או חתימות ל־GitHub הציבורי.**

## כללי סוגים

| סוג Access | שימוש |
| --- | --- |
| טקסט קצר (Short Text), גודל 36 | כל מזהה `...ID`, כולל מפתח ראשי ומפתח זר; האפליקציה תייצר GUID. גם `RecordID` ב־`SyncQueue`. יש לשמור אותו גודל בשני צדי הקשר. |
| טקסט קצר | קודים, מספרי מטוסים, מספר אישי, מספרי כלים, מספרים סידוריים וערכי בחירה, כולל מספרים עם אפסים מובילים. |
| טקסט ארוך (Long Text) | מלל חופשי ותיאורים שיכולים לגדול. |
| מספר (Number), גודל שדה: מספר שלם ארוך (Long Integer) | מונה, כמות ומיקום מספרי. |
| תאריך/שעה (Date/Time) | זמן מלא, תאריך בלבד או שעה בלבד; הגדרת התצוגה אינה משנה את סוג השדה. |
| כן/לא (Yes/No) | דגלים וסימוני V. |

**מפתחות:** מפתח ראשי בכל טבלה הוא שדה המזהה הראשון שלה. בטבלאות `FaultDetails` ו־`KeyLoadDetails` המפתח הראשי הוא גם `FormID`, עם קשר אחד לאחד אל `Forms`. לא משתמשים כאן ב־AutoNumber, כדי שהמזהים יישארו יציבים כשתתווסף בעתיד סנכרון. אם כבר יצרת AutoNumber וקשרים, אל תשנה אותם לפני שנבדוק את הקובץ עצמו.

## כל טבלה וכל שדה

| טבלה | טקסט קצר 36 (מזהים) | טקסט קצר רגיל | טקסט ארוך | מספר שלם ארוך | תאריך/שעה | כן/לא |
| --- | --- | --- | --- | --- | --- | --- |
| `Users` | `UserID` | `DisplayName`, `Email`, `Role` | — | — | `CreatedAt`, `DeletedAt`, `UpdatedAt` | `Active` |
| `Squadrons` | `SquadronID` | `Name` | — | — | — | — |
| `Aircraft` | `AircraftID`, `SquadronID` | `AircraftNumber`, `DefaultEWSuite` | — | — | — | `Active` |
| `Locations` | `LocationID`, `SquadronID` | `ShelterName`, `WorkshopName` | — | — | — | `Active` |
| `Systems` | `SystemID` | `SystemName`, `SystemCode` | — | — | — | `Active` |
| `Forms` | `FormID`, `SquadronID`, `LocationID`, `CreatedByUserID` | `SerialNumber`, `FormKind`, `Status` | — | — | `OpenedAt`, `SubmittedAt`, `ClosedAt`, `ReviewedAt`, `UpdatedAt` | — |
| `FaultDetails` | `FormID`, `AircraftID`, `SystemID` | `EWSuite`, `Action11Outcome` | `FaultDescription`, `RepairDescription` | — | `ArrivalTime`, `CompletionTime` | — |
| `KeyLoadDetails` | `FormID` | `Period` | — | — | `WorkDate`, `WorkTime` | — |
| `FormParticipants` | `ParticipantID`, `FormID`, `UserID` | `ParticipantRole` | — | `Position` | `AssignedAt` | — |
| `KeyLoadAircraft` | `RowID`, `FormID`, `AircraftID` | — | `ManagerNotes`, `TechnicianNotes` | — | — | `BlueRequested`, `BlueCompleted`, `ShachRequested`, `ShachCompleted` |
| `FormActions` | `ActionID`, `FormID` | `Outcome` | — | `ActionNumber` | `ActionTime` | — |
| `FormUnits` | `UnitRowID`, `FormID` | `Direction`, `UnitName`, `SerialNumber` | — | — | — | — |
| `FormTools` | `ToolRowID`, `FormID` | `ToolName`, `ToolNumber` | `Notes` | — | — | — |
| `FormMaterials` | `MaterialRowID`, `FormID` | `Category`, `RemovedFrom`, `Mascha` | `Description`, `RejectionReason`, `Notes` | `QuantityIn`, `QuantityOut`, `Quantity` | — | — |
| `FormInspections` | `InspectionID`, `FormID` | `InspectorPersonalNumber`, `ToolsOfficerName`, `LeadName`, `Phase` | — | — | `DepartureTime` | — |
| `FormSignatures` | `SignatureID`, `FormID`, `InspectionID`, `SignerUserID` | `Purpose`, `FilePath` | — | — | `SignedAt` | — |
| `FormNotes` | `NoteID`, `FormID`, `AuthorUserID` | `AuthorRole` | `Content` | — | `CreatedAt` | — |
| `AircraftConstants` | `ConstantID`, `AircraftID`, `UpdatedByUserID` | `GroupName`, `ConstantName`, `Value` | — | — | `UpdatedAt` | — |
| `FormEvents` | `EventID`, `FormID`, `ActorUserID` | `EventName` | `Details` | — | `EventAt` | — |
| `SyncQueue` | `ChangeID`, `RecordID` | `EntityName`, `Operation`, `SyncStatus` | — | `AttemptCount` | `ChangedAt` | — |

### דיוקים חשובים

- `AircraftNumber` (`034`), `SerialNumber` (`K-000000001` או מספר תקלה בן עשר ספרות), `InspectorPersonalNumber` (עד 7 ספרות), `ToolNumber` ו־`Mascha` נשארים **טקסט קצר**; אחרת אפסים מובילים עלולים להיעלם.
- גם `AircraftConstants.Value` הוא **טקסט קצר**. הקבועים כוללים כעת מספרים שלמים, אך שמירת הערך כטקסט מאפשרת תיעוד מדויק של סימן, אפסים מובילים וערכים עתידיים. אם יהיה צורך לחשב איתם, האפליקציה תאמת ותמיר לערך מספרי בעת החישוב.
- `ArrivalTime`, `CompletionTime`, `WorkTime`, `ActionTime`, `DepartureTime`: סוג **תאריך/שעה**, פורמט תצוגה **שעה קצרה**. האפליקציה תשמור תאריך ושעה מלאים, גם אם יוצגת רק השעה, כדי שלא לאבד את היום במקרה של עבודה מעבר לחצות.
- `WorkDate`: סוג **תאריך/שעה**, פורמט **תאריך קצר**. את `Period` אפשר לשמור כטקסט לדוגמה `11-12/26`, אך היסטוריית ההטענה לכל סוג מפתח נגזרת מהתאריך ומהשורות בטבלה, ולא מסתמכת על המחרוזת לבדה.
- כל שדות הטקסט שמכילים בחירות (`Status`, `Outcome`, `Role`, `Phase` ועוד) נשארים טקסט קצר; האפליקציה תגביל את הערכים המותרים.
- `DeletedAt`, `SubmittedAt`, `ClosedAt`, `ReviewedAt`, `InspectionID` בחתימות ושדות חומר שאינם חלים על סוג השורה: יש לאפשר ערך ריק (Null). גם הערות אינן חובה.
- חתימות נשמרות כקבצים לצד בסיס הנתונים; `FilePath` הוא נתיב יחסי בלבד. גיבוי נתונים חייב לכלול את התיקייה הזאת.

## תוספת לגרסה המקומית: נעילת עריכה

כדי להראות מי עורך טופס ולמנוע שתי עריכות במקביל, הוסף טבלה `EditLocks`:

| שדה | סוג |
| --- | --- |
| `LockID` | טקסט קצר 36; מפתח ראשי |
| `FormID` | טקסט קצר 36; אינדקס ייחודי, מפתח זר אל `Forms.FormID` |
| `OwnerUserID` | טקסט קצר 36; מפתח זר אל `Users.UserID` |
| `SessionID` | טקסט קצר 36 |
| `AcquiredAt` | תאריך/שעה |
| `ExpiresAt` | תאריך/שעה |

האפליקציה תבדוק נעילה לפני עריכה, תחדש אותה בזמן עבודה ותשחרר אותה ביציאה. נעילה שהתיישנה תוכל להשתחרר אחרי בדיקת תוקף. הנעילה אינה מתירה דריסה של שמירה שכבר בוצעה.

## לפני שינוי ב־Access

1. סגור את Access ושמור עותק של קובץ ה־`.accdb` במקום אחר.
2. פתח כל טבלה ב־**תצוגת עיצוב**, ושנה את `Data Type` לפי הטבלה למעלה. בשדות מזהה קבע `Field Size = 36` והגדר מפתח ראשי.
3. הגדר קשרים בין שדות המזהה התואמים בחלון **Database Tools → Relationships**, עם אכיפת תקינות קשרים. אל תאפשר מחיקה גורפת אוטומטית לטפסים ולהיסטוריה.
4. אל תכניס לטבלת `Users` סיסמאות כטקסט. בחירת שיטת ההתחברות המקומית תיכלל במימוש Windows ולא ברשימת העמודות הזו.

אם השמות בקובץ שבנית שונים מהמסמך הזה, צריך למפות לפי הקובץ האמיתי לפני שמריצים המרה.
