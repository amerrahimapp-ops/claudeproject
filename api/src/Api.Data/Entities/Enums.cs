namespace Api.Data.Entities;

public enum RequestStatus
{
    Draft,
    Submitted,
    AiEvaluation,
    AiReviewed,
    CapacityReview,
    InfraApproval,
    Done,
    Rejected,
    Deferred,
}

public enum RequestEnvironment
{
    Prod,
    DR,
    UAT,
    SIT,
    Dev,
}

public enum ProjectType
{
    New,
    Enhancement,
    Maintenance,
    BAU,
}

public enum RequestPriority
{
    Low,
    Medium,
    High,
}

public enum ResourceType
{
    Storage,
    Cpu,
    Ram,
}

public enum Platform
{
    Unix,
    Wintel,
}

public enum WorkflowStageStatus
{
    Pending,
    InProgress,
    Approved,
    Rejected,
    Deferred,
}

public enum UserRole
{
    Requestor,
    CapacityManager,
    InfraHead,
    Admin,
}

public enum ThemePreference
{
    Light,
    Dark,
}
