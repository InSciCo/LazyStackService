
namespace LazyStack.Notifications.WebSocketService;

public interface IWebSocketConnectionManager
{
    string AddSocket(WebSocket socket);
    Task RemoveSocketAsync(string id);
    Task SendMessageAsync(string connectionId, string message);
}