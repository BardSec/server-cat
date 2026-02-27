namespace ServerCat.Core.Enums;

public enum ServerEnvironment
{
    Production,
    Staging,
    Development,
    DR,
    Lab
}

public enum Criticality
{
    Critical,
    High,
    Medium,
    Low
}

public enum OsType
{
    Windows,
    Linux,
    Unknown
}

public enum AuthMethod
{
    WinRmKerberos,
    WinRmNtlm,
    WinRmBasic,
    WmiDcom,
    SnmpV2,
    SnmpV3
}

public enum PollStatus
{
    Running,
    Success,
    Partial,
    Failed
}

public enum PollTrigger
{
    Scheduler,
    Manual,
    Api
}

public enum LogSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public enum UserRole
{
    Viewer,
    Engineer,
    Admin
}

public enum ServerStatus
{
    Green,
    Yellow,
    Red,
    Gray
}
