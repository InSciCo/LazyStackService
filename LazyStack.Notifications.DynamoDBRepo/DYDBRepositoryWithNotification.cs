namespace LazyStack.Notifications.DynamoDBRepo;

public abstract class DYDBRepositoryWithNotification<TEnv, T> : DYDBRepository<TEnv,T> 
    where TEnv : class, IDataEnvelope<T>, new()
    where T : class, new()
{
    public DYDBRepositoryWithNotification(
        IAmazonDynamoDB client,
        ILzNotificationRepo lzNotificationRepo ) : base(client)
    {
        this.client = client;
        this.lzNotificationRepo = lzNotificationRepo;
        UseNotifications = true;
    }

    protected ILzNotificationRepo lzNotificationRepo { get; init; }

    public override async Task WriteDeleteNotificationAsync(ICallerInfo callerInfo, string dataType, string sk, string topics, long updatedUtcTick)
         => await lzNotificationRepo.WriteDeleteNotificationAsync(callerInfo, dataType, sk, topics, updatedUtcTick);

    public override async Task WriteNotificationAsync(ICallerInfo callerInfo, string dataType, string data, string topics, long updatedUtcTick, string action)
        => await lzNotificationRepo.WriteNotificationAsync(callerInfo, dataType, data, topics, updatedUtcTick, action);
}
