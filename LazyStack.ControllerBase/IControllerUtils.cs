namespace LazyStack.ControllerBase;

public interface IControllerUtils
{
    public Task<ICallerInfo> GetCallerInfoAsync(HttpRequest request, [CallerMemberName] string endpointName = "");
}
