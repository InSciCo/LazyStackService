namespace LazyStack.ControllerBase;

public interface ICallerInfo
{
    public string? LzUserId { get; set; }
    public string? UserName { get; set; }    
    public string? Tenancy { get; set; }
    public string? Table { get; set; }
    public string? SessionId { get; set; }
    public List<string> Permissions { get; set; }
}
