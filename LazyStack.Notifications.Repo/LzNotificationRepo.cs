namespace LazyStack.Notifications.Repo;

public class LzNotificationEnvelope : DataEnvelope<LzNotification>
{
    public static string DefaultPK = "LzNotification:";
    public override void SealEnvelope()
    {
        TypeName = CurrentTypeName;
        PayloadId = EntityInstance.Id ??= Guid.NewGuid().ToString();
        var guid = Guid.NewGuid().ToString();
        EntityInstance.Id = guid;
        PK = DefaultPK; // Partition key
        SK = $"{guid}:"; // we use a guid in case we ever want to merge notifications across physical partitions.
        SK1 = $"{EntityInstance.TopicId}:{EntityInstance.CreateUtcTick:X16}:";
        SK2 = $"{EntityInstance.CreateUtcTick:X16}:";
        SK3 = $"{-EntityInstance.CreateUtcTick:D19}:"; // Reverse order index for getting latest items
        base.SealEnvelope();
    }
    public override string CurrentTypeName { get; set; } = $"{DefaultPK}v1.0.0";

}
/// <summary>
/// Repo for CRL of LzNotification records.
/// LzNotification records are Write only records and have a TTL value so they are deleted by 
/// DynamoDB at some point on or after the TTL datetime. 
/// Notes: 
/// - This repo assumes that the LzNotification records are stored in 
/// a table identified by {table}-LzNotifications. We keep the notification records separate 
/// from the main table to avoid doubling up on streams from the main table. Remmeber that
/// The LzNotificationsFromStream lambda is reading batchs of changes on the main table and
/// writing LzNotification records. If the LzNotifications records were in the main table, we 
/// would get batches of these in the stream and just ignore them. Also, since we have the 
/// notifications in a separate table, other table specific configurations can be tailored 
/// to their use; for instance, there is no reason to backup notifications.
/// 
/// Limitations: 
/// - This repo assumes all the notification records are in the same physical partition so they 
/// have unique CreatedAt datetime tick values. If you implement sharding, you will have to revisit this
/// routine. Since the notifications records are transient, it is unlikely a sufficient volume of these 
/// records will ever exist to force the use of multiple physical partitions. Remember that the PK, in this 
/// case "LzNotification:" can't be used across physical partitions so any sharding strategy will imply 
/// substantial changes to the repo.
/// 
/// - This repo caches LzNotifications without regard to Topic. This means the routine may become inefficient 
/// with a large number of Topics. Supporting a large number of Topics would be better served by using 
/// a routine that leverages ElastiCache or DAX. Since this repo implements the ILzNotificationsRepo interface
/// it is easy to replace with an implementation that uses ElastiCache or DAX (or whatever caching solution
/// you prefer).
/// 
/// - Calls to read LzNotification records older than MaxLzNotificationAge will return an 410 (Gone) status. The client should
/// reread application data when this happens. For example, let's say you have an app that sleeps for an three 
/// hours, on resume it will try and read notification records to update it's local datasets. If the MaxLzNotificationsAge
/// is set to 30 minutes, this read will fail and the app will have to reload application data directly instead of 
/// updating from notifications. 
/// 
/// </summary>
public interface ILzNotificationRepo : IDYDBRepository<LzNotificationEnvelope, LzNotification>
{
    Task<ActionResult<LzNotificationsPage>> LzNotification_Page_List_SubscriptionId_DateTimeTicks_Async(ICallerInfo callerInfo, string subscriptionId, long dateTimeTicks, bool? useCache = null);

}
public class LzNotificationRepo : DYDBRepository<LzNotificationEnvelope, LzNotification>, ILzNotificationRepo
{
    public static string DefaultPK = "LzNotification:"; 
    public LzNotificationRepo(
        IAmazonDynamoDB client,
        ILzSubscriptionRepo subscriptionRepo
        ) : base(client)
    {
        PK = LzNotificationEnvelope.DefaultPK;
        this.subscriptionRepo = subscriptionRepo;
        UpdateReturnsOkResult = false; // just return value
        TTL = 48 * 60 * 60; // 48 hours 
    }


    protected readonly SortedDictionary<long, LzNotification> notificationsCache = new();
    protected long earliestCacheItemTick;
    protected long latestCacheItemTick;
    public long MaxLzNotificationsCacheSize { get; set; } = 20 * 1024 * 1024; // todo, make a configuration value
    private long currentLzNotificationsCacheSize;
    protected const long MaxResultSize = 5 * 1024 * 1024; // 5MB to stay under API Gateway Response size limit of 6MB
    public long MaxLzNotificationsAgeInSeconds { get; set; } = 1800; // 30 minutes

    private readonly ILzSubscriptionRepo subscriptionRepo;

    /// <summary>
    /// Read LzNotifications for topics associated with a connectionId. 
    /// We read the subscription record and then aggregate all the subscribed topics into a list ordered by CreatedAt. 
    /// </summary>
    /// <param name="callerInfo"></param>
    /// <param name="connectionId"></param>
    /// <param name="dateTimeTicks"></param>
    /// <param name="useCache"></param>
    /// <returns></returns>
    public async Task<ActionResult<LzNotificationsPage>> LzNotification_Page_List_SubscriptionId_DateTimeTicks_Async(ICallerInfo callerInfo, string subscriptionId, long dateTimeTicks, bool? useCache = null)
    {
        var notifications = new List<LzNotification>();
        var now = DateTime.UtcNow.Ticks;
        if (now - dateTimeTicks > MaxLzNotificationsAgeInSeconds * 10000000)
            return new StatusCodeResult(410); // Gone

        // Get subscription record for connectionId
        var subscriptionTask = await subscriptionRepo.ReadAsync(callerInfo, subscriptionId);
        var subscription = subscriptionTask.Value;
        if (subscription is null || subscription.UserId != callerInfo.LzUserId || subscription.TopicIds.Count == 0)
            return new LzNotificationsPage() { LzNotifications = notifications, More = false }; // return empty list if no subscription or incorrect userId

        var statusCode = 0;
        // Filter by subscription topic(s) 
        var topicIds = subscription.TopicIds;
        do
        {
            var objResult = await ReadLatestLzNotifications(callerInfo, dateTimeTicks);
            if (objResult is null) return new ObjectResult(null) { StatusCode = 500 };
            statusCode = (int)objResult.StatusCode!;
            if (statusCode < 200 || statusCode > 299)
                return new ObjectResult(null) { StatusCode = statusCode };

        } while (statusCode == 206); // partial result

        return new ObjectResult(new LzNotificationsPage() { LzNotifications = notifications, More = false }); ;
    }

    /// <summary>
    /// This method reads the latest  Notifciation records with CreatedAt
    /// >= earliestTick. It maintains a notificationsCache of records having 
    /// up to MaxCacheSize of content. It only reads notification records 
    /// newer than those in the cache. 
    /// Note: Lambdas are inheriently thread safe so we can use SortedDictionary for our cache. 
    /// Note: This routine is not thread safe - which isn't an issue when running in a Lambda. Keep this in mind when running in a local web server for debugging.
    /// </summary>
    /// <param name="callerInfo"></param>
    /// <param name="fromTick"></param>
    /// <returns></returns>
    protected async Task<ObjectResult> ReadLatestLzNotifications(ICallerInfo callerInfo, long earliestTick, int limit = 0)
    {
        // The notificationsCache may contain some of the required records. We do queries to retrieve requested records not in the
        // notificationsCache and add records to the notificationsCache within the constraints of MaxCacheRecords. The cache always 
        // contains the latest retrieved records, up to MaxCacheRecords, on exit from this method.
        var nowTick = DateTime.UtcNow.Ticks;

        // First, if there are records in the notificationsCache, we load any records available from nowTick to the either the
        // earliestTick argument or the latestTick value in notificationsCache.
        // Note: Using SK3 which is the descending order index on CreatedAt. 
        // Note: This step can grow the cache beyound MaxLzNotificationsCacheSize. We prune the cache before we exit this method.
        int statusCode = 0;
        do
        {
            var startOfRange = -nowTick;
            var endOfRange = notificationsCache.Count > 0 ? notificationsCache.First().Value.CreateUtcTick : -earliestTick;

            var (objResult, size) = await ListAndSizeAsync(QueryRange(PK, "SK3", $"{startOfRange:D19}:", $"{endOfRange:D19}:", table: callerInfo.Table + "-LzNotifications"), useCache: false);
            statusCode = (int)objResult!.StatusCode!;

            if (statusCode < 200 && statusCode > 299)
            {
                // Clear the cache if anything screwy has happened.
                // TODO: Experiment with less draconian recovery options.
                Console.WriteLine($"ReadLatestLzNotifications failed in phase 1. Clearing Cache");
                notificationsCache.Clear();
                currentLzNotificationsCacheSize = 0;
                    return new ObjectResult(null) { StatusCode = statusCode };
            }

            // Add retreived items to cache
            var list = objResult.Value as List<LzNotification>;
            if(list != null)
                foreach(var item in list)
                {
                    notificationsCache.Add(item.CreateUtcTick, item);
                    currentLzNotificationsCacheSize += item.Payload.Length + 150; // rough estimate is sufficient
                }

        } while (statusCode == 206);

        // Next, if the earliestTick is before the tick of the earliest tick in notificationsCache, we load that set of records.
        // Note: This step can also grow the cache beyound the MaxNotifciationsCacheSize. We prune the cache before we leave method.
        // Note: Using SK2 which is the ascending order index on CreatedAt
        if (notificationsCache.Count > 0 && earliestTick < notificationsCache.Keys.First() + 1)
            do
            {
                var startOfRange = earliestTick;
                var endOfRange = notificationsCache.Keys.First() + 1;

                var (objResult, size) = await ListAndSizeAsync(QueryRange(PK, "SK2", $"{startOfRange:X16}:", $"{endOfRange:X16}:", table: callerInfo.Table + "-LzNotifications"), useCache: false);
                statusCode = (int)objResult!.StatusCode!;

                if (statusCode < 200 && statusCode > 299)
                {
                    // Clear the cache if anything screwy has happened.
                    // TODO: Experiment with less draconian recovery options.
                    Console.WriteLine($"ReadLatestLzNotifications failed in phase 1. Clearing Cache");
                    notificationsCache.Clear();
                    currentLzNotificationsCacheSize = 0;
                    return new ObjectResult(null) { StatusCode = statusCode };
                }

                // Add retreived items to cache
                var list = objResult.Value as List<LzNotification>;
                if (list != null)
                    foreach (var item in list)
                    {
                        notificationsCache.Add(item.CreateUtcTick, item);
                        currentLzNotificationsCacheSize += item.Payload.Length + 150; // rough estimate is sufficient
                    }
            } while (statusCode == 206);

        List<LzNotification> returnList = new();
        long returnListSize = 0;
        foreach (var kvp in notificationsCache)
            if (kvp.Key >= earliestTick && (returnListSize+= kvp.Value.Payload.Length) < MaxResultSize)
                returnList.Add(kvp.Value);
            else
            {
                statusCode = 206; // partial return
                break;
            }

        // Prune Cache if it has grown too large
        if (currentLzNotificationsCacheSize > MaxLzNotificationsCacheSize)
        {
            var keys = notificationsCache.Keys.ToList();
            foreach (var key in keys)
            {
                var notification = notificationsCache[key];
                currentLzNotificationsCacheSize -= notification.Payload.Length;
                notificationsCache.Remove(key);
                if (currentLzNotificationsCacheSize < MaxLzNotificationsCacheSize)
                    break;
            }
        }

        return new ObjectResult(returnList) { StatusCode = statusCode };
    }
}