# LazyStackService

## LazyStackNotifications 
The objective of the Notifications service is to provide a simple way to send notifications to clients 
when data changes in a DynamoDB table. This accomplished by clients subscribing to "topics".

We stream changes made to a DynamoDB table (MyTable). These stream events are handled 
by the LazyStack.Notifications.FromStreams lambda function.

FromStreams sends messages to registered subscribers using a WebSocket. 
FromStreams writes messages to a DynamoDB table MyTable-LzNotifications.
Items in MyTable-LzNotifications are deleted after a TTL period.

LzSubscriptions are stored in MyTable under the PK "LzSubscriptions".


## Client Usage
Clients subscribe to notifications and receive notifications. 
Clients can request all notifications since a given time.

