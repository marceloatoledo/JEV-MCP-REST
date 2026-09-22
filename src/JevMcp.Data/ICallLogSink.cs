namespace JevMcp.Data;

/// <summary>Accepts a record without waiting for disk. Persistence is someone else's job.</summary>
public interface ICallLogSink
{
    void Enqueue(CallLog log);
}
