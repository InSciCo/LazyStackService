# LazyStack.Notifications.SharedCode

This project contains the shared yaml specifications for the notification service. These are used in Service Side solutions.

These specifications do not change from solution to solution. They are included in this project to make it easier to generate the client code and AWS CloudFormation templates for the notification service.

In addition to these shared specifications, your service and client side solutions will need to include other libraries. See the Related Libraries section below.

## NotificationsSvc.yaml
This is an OpenAPI specification for the notification service. It is used to generate the client code for the notification service.
Insert into your solutions LazyStack.yaml file as follows:
```
OpenApiSpecs:
- ..\LazyStackService\LazyStack.Notifications.SharedCode\NotificationsSvc.yaml
```
Notes:
Including this OpenAPI spec will generate the client code for the notification service into the client SDK. These include:
```
SubscribeAsync(LzSubscription body)
LzNotificationsPageListSessionIdDateTimeTicksAsync(string subscriptionId, long dateTimeTicks)
GetLzSubscriptionByIdAsync(string sessionId)
```

Although the DTO objects are defined in this OpenAPI spec, the C# classes are not generated into the usual Schema library. Instead, the are predefined in the LazyStack.Notifications.Schema project. This addresses namespaces issues.

## NotificationsSvcSAM.yaml
This is an AWS SAM specification for the notification service. It is used to generate the AWS CloudFormation template notification resources for the stack.
Insert into your solutions LazyStack.yaml file as follows:
```
AwsTemplates:
- ..\LazyStackService\LazyStack.Notifications.SharedCode\NotificationsSvcSAM.yaml
```

## Related Libraries

### Both Client and Service Side - LazyStackShared solution
LazyStack.Notifications.Schema

### AWS Service Side - LazyStackService solution
LazyStack.Notifications.FromStreams
LazyStack.Notifications.Notifications.Repo 
LazyStack.Notifications.Notifications.WebSocket


### Local WebApi Service  - LazyStackService solution
LazyStack.Notifications.WebSocketService

### Client Side - LazyStackClient solution
LazyStack.Notifications.ViewModels 
LazyStack.Notifications.SharedSDK 


