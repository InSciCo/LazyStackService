namespace LazyStack.Notifications.Repo;

public class LzSubscriptionEnvelope : DataEnvelope<LzSubscription>
{
    public static string DefaultPK = "LzSubscription:";
    public override void SealEnvelope()
    {
        TypeName = CurrentTypeName;
        PK = DefaultPK; // Partition key
        SK = $"{EntityInstance.Id}:";
        SK1 = $"{EntityInstance.CreateUtcTick:X16}:";
        base.SealEnvelope();
    }
    public override string CurrentTypeName { get; set; } = $"{DefaultPK}v1.0.0";
}
/// <summary>
/// Repo for CRUDL of LzSubscription records.
/// </summary>
public interface ILzSubscriptionRepo : IDYDBRepository<LzSubscriptionEnvelope, LzSubscription>
{
    Task<ObjectResult> List_DateTimeTicks_Async(ICallerInfo callerInfo, long dateTimeTicks, bool? useCache = null);
}
public class LzSubscriptionRepo : DYDBRepository<LzSubscriptionEnvelope, LzSubscription>, ILzSubscriptionRepo
{
    public LzSubscriptionRepo(IAmazonDynamoDB client) : base(client)
    {
        PK = LzSubscriptionEnvelope.DefaultPK;
        UpdateReturnsOkResult = false; // just return value
        TTL = 48 * 60 * 60; // 48 hours 
    }
    public async Task<ObjectResult> List_DateTimeTicks_Async(ICallerInfo callerInfo, long dateTimeTicks, bool? useCache = null)
    {
        return await ListAsync(QueryRange(PK, "SK1", $"{dateTimeTicks:X16}:", $"{long.MaxValue:X16}:", table: callerInfo.Table), useCache: useCache);

    }
}