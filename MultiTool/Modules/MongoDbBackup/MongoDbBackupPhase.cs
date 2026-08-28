namespace MultiTool.Modules.MongoDbBackup;

public enum MongoDbBackupPhase
{
    Idle,
    Validating,
    Running,
    Cancelling,
    Succeeded,
    Cancelled,
    Faulted
}
