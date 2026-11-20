namespace EzEizo.Core;

public enum AppRole { Manager, Mechanic }
public sealed record Account(string Id, string Name, string PersonalNumber, string Role);
public sealed record Ticket(string Id, string Serial, string Kind, string Squadron, string Aircraft,
    string Description, string Repair, string Status, string AssignedTo, string OpenedAt);
