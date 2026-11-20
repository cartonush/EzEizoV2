using System.Data.OleDb;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EzEizo.Core;

public sealed class Database
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EzEizo");
    public static readonly string DataDirectory = Path.Combine(Root, "data");
    public static readonly string FilePath = Path.Combine(DataDirectory, "EzEizo.accdb");
    private readonly string connectionString = $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={FilePath};Persist Security Info=False;";
    public static bool ValidPersonalNumber(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9]{7}\z");
    private OleDbConnection Connect()
    {
        var connection = new OleDbConnection(connectionString);
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "signatures"));
        if (File.Exists(FilePath)) { using var check = Connect(); return; }
        var provider = Type.GetTypeFromProgID("DAO.DBEngine.120")
            ?? throw new InvalidOperationException("רכיב Microsoft Access Database Engine x64 חסר במחשב.");
        object? instance = null;
        object? database = null;
        try
        {
            instance = Activator.CreateInstance(provider);
            // dbVersion120 (128) creates a real ACCDB using the Access DAO engine.
            database = ((dynamic)instance!).CreateDatabase(FilePath, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128);
            ((dynamic)database).Close();
        }
        finally
        {
            if (database is not null && Marshal.IsComObject(database)) Marshal.FinalReleaseComObject(database);
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }

        using var connection = Connect();
        foreach (var sql in Schema)
        {
            using var command = new OleDbCommand(sql, connection);
            command.ExecuteNonQuery();
        }
    }

    // The database is provisioned as a blank schema, never with personnel data in the public repository.
    private static readonly string[] Schema =
    [
        "CREATE TABLE [Users] ([UserID] TEXT(36) PRIMARY KEY, [DisplayName] TEXT(100), [PersonalNumber] TEXT(7), [Role] TEXT(20), [Active] BIT, [CreatedAt] DATETIME, [DeletedAt] DATETIME, [UpdatedAt] DATETIME)",
        "CREATE UNIQUE INDEX [UX_Users_Personal] ON [Users] ([PersonalNumber])",
        "CREATE TABLE [Squadrons] ([SquadronID] TEXT(36) PRIMARY KEY, [Name] TEXT(50))",
        "CREATE TABLE [Aircraft] ([AircraftID] TEXT(36) PRIMARY KEY, [SquadronID] TEXT(36), [AircraftNumber] TEXT(12), [DefaultEWSuite] TEXT(100), [Active] BIT)",
        "CREATE TABLE [Locations] ([LocationID] TEXT(36) PRIMARY KEY, [SquadronID] TEXT(36), [ShelterName] TEXT(60), [WorkshopName] TEXT(60), [Active] BIT)",
        "CREATE TABLE [Systems] ([SystemID] TEXT(36) PRIMARY KEY, [SystemName] TEXT(100), [SystemCode] TEXT(20), [Active] BIT)",
        "CREATE TABLE [Forms] ([FormID] TEXT(36) PRIMARY KEY, [SerialNumber] TEXT(24), [FormKind] TEXT(20), [Status] TEXT(30), [SquadronID] TEXT(36), [LocationID] TEXT(36), [CreatedByUserID] TEXT(36), [OpenedAt] DATETIME, [SubmittedAt] DATETIME, [ClosedAt] DATETIME, [ReviewedAt] DATETIME, [UpdatedAt] DATETIME)",
        "CREATE UNIQUE INDEX [UX_Forms_Serial] ON [Forms] ([SerialNumber])",
        "CREATE TABLE [FaultDetails] ([FormID] TEXT(36) PRIMARY KEY, [AircraftID] TEXT(36), [SystemID] TEXT(36), [EWSuite] TEXT(100), [FaultDescription] MEMO, [RepairDescription] MEMO, [ArrivalTime] DATETIME, [CompletionTime] DATETIME, [Action11Outcome] TEXT(20))",
        "CREATE TABLE [KeyLoadDetails] ([FormID] TEXT(36) PRIMARY KEY, [WorkDate] DATETIME, [WorkTime] DATETIME, [Period] TEXT(20))",
        "CREATE TABLE [FormParticipants] ([ParticipantID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [UserID] TEXT(36), [ParticipantRole] TEXT(30), [Position] LONG, [AssignedAt] DATETIME)",
        "CREATE TABLE [KeyLoadAircraft] ([RowID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [AircraftID] TEXT(36), [BlueRequested] BIT, [BlueCompleted] BIT, [ShachRequested] BIT, [ShachCompleted] BIT, [ManagerNotes] MEMO, [TechnicianNotes] MEMO)",
        "CREATE TABLE [FormActions] ([ActionID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [ActionNumber] LONG, [Outcome] TEXT(20), [ActionTime] DATETIME)",
        "CREATE TABLE [FormUnits] ([UnitRowID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [Direction] TEXT(20), [UnitName] TEXT(100), [SerialNumber] TEXT(80))",
        "CREATE TABLE [FormTools] ([ToolRowID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [ToolName] TEXT(100), [ToolNumber] TEXT(80), [Notes] MEMO)",
        "CREATE TABLE [FormMaterials] ([MaterialRowID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [Category] TEXT(30), [Description] MEMO, [RemovedFrom] TEXT(100), [QuantityIn] LONG, [QuantityOut] LONG, [Quantity] LONG, [Mascha] TEXT(80), [RejectionReason] MEMO, [Notes] MEMO)",
        "CREATE TABLE [FormInspections] ([InspectionID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [Phase] TEXT(30), [InspectorPersonalNumber] TEXT(7), [ToolsOfficerName] TEXT(100), [LeadName] TEXT(100), [DepartureTime] DATETIME)",
        "CREATE TABLE [FormSignatures] ([SignatureID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [InspectionID] TEXT(36), [Purpose] TEXT(100), [SignerUserID] TEXT(36), [SignedAt] DATETIME, [FilePath] TEXT(255))",
        "CREATE TABLE [FormNotes] ([NoteID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [AuthorUserID] TEXT(36), [AuthorRole] TEXT(20), [Content] MEMO, [CreatedAt] DATETIME)",
        "CREATE TABLE [AircraftConstants] ([ConstantID] TEXT(36) PRIMARY KEY, [AircraftID] TEXT(36), [GroupName] TEXT(60), [ConstantName] TEXT(60), [Value] TEXT(80), [UpdatedByUserID] TEXT(36), [UpdatedAt] DATETIME)",
        "CREATE TABLE [FormEvents] ([EventID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [EventName] TEXT(50), [ActorUserID] TEXT(36), [EventAt] DATETIME, [Details] MEMO)",
        "CREATE TABLE [SyncQueue] ([ChangeID] TEXT(36) PRIMARY KEY, [EntityName] TEXT(50), [RecordID] TEXT(36), [Operation] TEXT(30), [ChangedAt] DATETIME, [SyncStatus] TEXT(30), [AttemptCount] LONG)",
        "CREATE TABLE [EditLocks] ([LockID] TEXT(36) PRIMARY KEY, [FormID] TEXT(36), [OwnerUserID] TEXT(36), [SessionID] TEXT(36), [AcquiredAt] DATETIME, [ExpiresAt] DATETIME)",
        "CREATE UNIQUE INDEX [UX_EditLocks_Form] ON [EditLocks] ([FormID])"
    ];

    private static string Id() => Guid.NewGuid().ToString();
    private static OleDbCommand Command(OleDbConnection c, OleDbTransaction? tx, string sql, params object?[] values)
    {
        var cmd = new OleDbCommand(sql, c, tx);
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var parameter = cmd.Parameters.Add("@p" + i, value switch
            {
                bool => OleDbType.Boolean,
                int => OleDbType.Integer,
                DateTime => OleDbType.DBTimeStamp,
                _ => OleDbType.VarWChar
            });
            parameter.Value = value ?? DBNull.Value;
        }
        return cmd;
    }

    public bool HasManager()
    {
        using var c = Connect();
        using var cmd = Command(c, null, "SELECT COUNT(*) FROM [Users] WHERE [Role]='manager' AND [Active]=True");
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public Account? SignIn(string number, AppRole role)
    {
        if (!ValidPersonalNumber(number)) return null;
        using var c = Connect();
        using var cmd = Command(c, null, "SELECT [UserID],[DisplayName],[PersonalNumber],[Role] FROM [Users] WHERE [PersonalNumber]=? AND [Role]=? AND [Active]=True", number, role == AppRole.Manager ? "manager" : "mechanic");
        using var r = cmd.ExecuteReader();
        return r is not null && r.Read() ? new Account(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)) : null;
    }

    public Account CreateAccount(string name, string number, string role)
    {
        if (!ValidPersonalNumber(number)) throw new ArgumentException("מספר אישי חייב להכיל בדיוק 7 ספרות.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("יש להזין שם.");
        if (role is not ("manager" or "mechanic")) throw new ArgumentException("תפקיד לא תקין.");
        if (role == "manager" && HasManager()) throw new InvalidOperationException("המנהל הראשוני כבר הוגדר.");
        var account = new Account(Id(), name.Trim(), number, role);
        using var c = Connect();
        using var cmd = Command(c, null, "INSERT INTO [Users] ([UserID],[DisplayName],[PersonalNumber],[Role],[Active],[CreatedAt],[UpdatedAt]) VALUES (?,?,?,?,?,?,?)",
            account.Id, account.Name, account.PersonalNumber, account.Role, true, DateTime.Now, DateTime.Now);
        cmd.ExecuteNonQuery();
        return account;
    }

    public List<Account> Technicians()
    {
        var result = new List<Account>();
        using var c = Connect();
        using var cmd = Command(c, null, "SELECT [UserID],[DisplayName],[PersonalNumber],[Role] FROM [Users] WHERE [Role]='mechanic' AND [Active]=True ORDER BY [DisplayName]");
        using var r = cmd.ExecuteReader();
        while (r is not null && r.Read()) result.Add(new Account(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return result;
    }

    public Ticket CreateFault(Account manager, string squadron, string aircraft, string description, IReadOnlyList<Account> assigned)
    {
        if (assigned.Count == 0) throw new ArgumentException("יש לבחור לפחות טכנאי אחד.");
        if (string.IsNullOrWhiteSpace(squadron) || string.IsNullOrWhiteSpace(aircraft) || string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("יש למלא טייסת, מטוס ותיאור תקלה.");
        var formId = Id();
        using var c = Connect();
        using var tx = c.BeginTransaction();
        try
        {
            string squadronId;
            using (var lookup = Command(c, tx, "SELECT [SquadronID] FROM [Squadrons] WHERE [Name]=?", squadron.Trim()))
                squadronId = Convert.ToString(lookup.ExecuteScalar()) ?? "";
            if (squadronId.Length == 0)
            {
                squadronId = Id();
                using var add = Command(c, tx, "INSERT INTO [Squadrons] ([SquadronID],[Name]) VALUES (?,?)", squadronId, squadron.Trim());
                add.ExecuteNonQuery();
            }
            string aircraftId;
            using (var lookup = Command(c, tx, "SELECT [AircraftID] FROM [Aircraft] WHERE [SquadronID]=? AND [AircraftNumber]=?", squadronId, aircraft.Trim()))
                aircraftId = Convert.ToString(lookup.ExecuteScalar()) ?? "";
            if (aircraftId.Length == 0)
            {
                aircraftId = Id();
                using var add = Command(c, tx, "INSERT INTO [Aircraft] ([AircraftID],[SquadronID],[AircraftNumber],[DefaultEWSuite],[Active]) VALUES (?,?,?,?,?)",
                    aircraftId, squadronId, aircraft.Trim(), "גרניט", true);
                add.ExecuteNonQuery();
            }
            using var last = Command(c, tx, "SELECT MAX([SerialNumber]) FROM [Forms] WHERE [FormKind]='fault'");
            var highest = Convert.ToString(last.ExecuteScalar());
            var serial = ((long.TryParse(highest, out var previous) ? previous : 0) + 1).ToString("D10");
            var now = DateTime.Now;
            using (var cmd = Command(c, tx, "INSERT INTO [Forms] ([FormID],[SerialNumber],[FormKind],[Status],[SquadronID],[CreatedByUserID],[OpenedAt],[UpdatedAt]) VALUES (?,?,?,?,?,?,?,?)",
                formId, serial, "fault", "open", squadronId, manager.Id, now, now)) cmd.ExecuteNonQuery();
            using (var cmd = Command(c, tx, "INSERT INTO [FaultDetails] ([FormID],[AircraftID],[SystemID],[EWSuite],[FaultDescription],[RepairDescription]) VALUES (?,?,?,?,?,?)",
                formId, aircraftId, null, "גרניט", description.Trim(), "")) cmd.ExecuteNonQuery();
            for (var i = 0; i < assigned.Count; i++)
            {
                using var cmd = Command(c, tx, "INSERT INTO [FormParticipants] ([ParticipantID],[FormID],[UserID],[ParticipantRole],[Position],[AssignedAt]) VALUES (?,?,?,?,?,?)",
                    Id(), formId, assigned[i].Id, i == 0 ? "lead" : "mechanic", i + 1, now);
                cmd.ExecuteNonQuery();
            }
            using (var ev = Command(c, tx, "INSERT INTO [FormEvents] ([EventID],[FormID],[EventName],[ActorUserID],[EventAt]) VALUES (?,?,?,?,?)",
                Id(), formId, "opened", manager.Id, now)) ev.ExecuteNonQuery();
            tx.Commit();
            return new Ticket(formId, serial, "fault", squadron.Trim(), aircraft.Trim(), description.Trim(), "", "open",
                string.Join(", ", assigned.Select(x => x.Name)), now.ToString("g"));
        }
        catch { tx.Rollback(); throw; }
    }

    public List<Ticket> Faults(Account? user = null)
    {
        var result = new List<Ticket>();
        using var c = Connect();
        var sql = "SELECT f.[FormID],f.[SerialNumber],f.[FormKind],s.[Name],a.[AircraftNumber],d.[FaultDescription],d.[RepairDescription],f.[Status],f.[OpenedAt] FROM (([Forms] AS f INNER JOIN [FaultDetails] AS d ON f.[FormID]=d.[FormID]) LEFT JOIN [Squadrons] AS s ON f.[SquadronID]=s.[SquadronID]) LEFT JOIN [Aircraft] AS a ON d.[AircraftID]=a.[AircraftID] WHERE f.[FormKind]='fault'";
        if (user is not null) sql += " AND EXISTS (SELECT * FROM [FormParticipants] AS p WHERE p.[FormID]=f.[FormID] AND p.[UserID]=?)";
        sql += " ORDER BY f.[OpenedAt] DESC";
        using var cmd = user is null ? Command(c, null, sql) : Command(c, null, sql, user.Id);
        using var r = cmd.ExecuteReader();
        while (r is not null && r.Read())
            result.Add(new Ticket(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5), r.IsDBNull(6) ? "" : r.GetString(6),
                r.GetString(7), "", Convert.ToDateTime(r.GetValue(8)).ToString("g")));
        return result;
    }

    public string[] ActionOutcomes(string formId)
    {
        var result = new string[12];
        using var c = Connect();
        using var cmd = Command(c, null, "SELECT [ActionNumber],[Outcome] FROM [FormActions] WHERE [FormID]=?", formId);
        using var r = cmd.ExecuteReader();
        while (r is not null && r.Read())
            if (r.GetInt32(0) is >= 1 and <= 12) result[r.GetInt32(0) - 1] = r.IsDBNull(1) ? "" : r.GetString(1);
        return result;
    }

    public void SaveFault(Ticket ticket, Account actor, string repair, string[] outcomes, bool close)
    {
        if (close && (string.IsNullOrWhiteSpace(repair) || outcomes.Length != 12 || outcomes.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("לסגירה יש למלא תיאור תיקון ולסמן את כל 12 הפעולות.");
        using var c = Connect();
        using var tx = c.BeginTransaction();
        try
        {
            using (var cmd = Command(c, tx, "UPDATE [FaultDetails] SET [RepairDescription]=? WHERE [FormID]=?", repair.Trim(), ticket.Id)) cmd.ExecuteNonQuery();
            for (var i = 0; i < outcomes.Length; i++)
            {
                using (var del = Command(c, tx, "DELETE FROM [FormActions] WHERE [FormID]=? AND [ActionNumber]=?", ticket.Id, i + 1)) del.ExecuteNonQuery();
                if (string.IsNullOrWhiteSpace(outcomes[i])) continue;
                using var add = Command(c, tx, "INSERT INTO [FormActions] ([ActionID],[FormID],[ActionNumber],[Outcome],[ActionTime]) VALUES (?,?,?,?,?)",
                    Id(), ticket.Id, i + 1, outcomes[i], (i is 1 or 11) ? DateTime.Now : null);
                add.ExecuteNonQuery();
            }
            var now = DateTime.Now;
            using (var cmd = Command(c, tx, "UPDATE [Forms] SET [Status]=?,[UpdatedAt]=?,[SubmittedAt]=? WHERE [FormID]=? AND [Status]='open'",
                close ? "pending_review" : "open", now, close ? now : null, ticket.Id))
                if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("הטופס כבר אינו פתוח לעריכה.");
            using (var ev = Command(c, tx, "INSERT INTO [FormEvents] ([EventID],[FormID],[EventName],[ActorUserID],[EventAt]) VALUES (?,?,?,?,?)",
                Id(), ticket.Id, close ? "submitted" : "saved", actor.Id, now)) ev.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public void Review(Ticket ticket, Account actor)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        try
        {
            var now = DateTime.Now;
            using (var cmd = Command(c, tx, "UPDATE [Forms] SET [Status]='closed',[ClosedAt]=?,[ReviewedAt]=?,[UpdatedAt]=? WHERE [FormID]=? AND [Status]='pending_review'",
                now, now, now, ticket.Id))
                if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("הטופס אינו ממתין לסגירה.");
            using (var ev = Command(c, tx, "INSERT INTO [FormEvents] ([EventID],[FormID],[EventName],[ActorUserID],[EventAt]) VALUES (?,?,?,?,?)",
                Id(), ticket.Id, "closed", actor.Id, now)) ev.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }
}
