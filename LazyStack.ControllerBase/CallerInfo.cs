namespace LazyStack.ControllerBase;

public class CallerInfo : ICallerInfo
{
    public string? LzUserId { get; set; }
    public string? UserName { get; set; }
    public string? Table { get; set; }
    public string? SessionId { get; set; }
    public List<string> Permissions { get; set; } = new();
}
