# LazyStackDynamoDBRepo

This lib provides an application level abstraction for Create, Read, Update, Delete and List (CRUDL) operations on a DynamoDB table. The DynamoDB term "table" is not similar to the SQL term "table". AWS recommends that an application uses a single DynamoDB table so a "table" is more related to a "database". 

DynamoDB is essentially a ISAM library on steroids where the data table and index tables have 
very few size restrictions and a simple query library is provided to do indexed and scan based access.

I won't cover DynamoDB basics here, refer to the AWS documentation for in depth DynamoDB documentation. Here are a few important DynamoDB terms and concepts for review:

- **Table** - AWS accounts have a single DynamoDB service. This service provides access to "tables". You typically configure the service with a single "table" to store data for an application. For example, in a SaaS multi-tenant application you could store the data for each tenant in a separate table. 
- **Primary Key** - each table contains records indexed by composite primary key comprised of a Partition Key (PK) and Sort Key (SK). Partitions are limited to 10GB in size.
- **Physical Partition** - DynamoDB stores data in partitions. A partition may contain records with different Partition 
keys. However, records with the same Primary Key are stored in the same physical partition. As the databse grows, DynamoDB creates
multiple physical partitions to optimize performance. Each physical partition is limited to 10G in size.
This is where the 10G per Partition Key limitation comes from. If you have more than 10G of data associated with a Partition
Key, you will need to implement a sharding scheme on the Partition Key. This library doesn't do this for you but it 
doesn't preclude a sharding implementation. 
- **Secondary Indices** - each table can have up to five Secondary Indicies with composite keys of the Partition Key (PK) and a single record field. 
- **Glocal Secondary Index** - GSI's are like materialized views. They are essentially separate tables where the table name is the the GSI name and each record is indexed by the PK of the original table and a field from the original table. The GSI also cotains one or more additional fields from the original table. GSIs are eventually correct.

## Entity Relation Diagrams to DynamoDB Schema
I usually start any application design with an ER diagram establishing the entities and entity relationships useful in the application. With SQL databases, it is simple to convert 3NF ER designs into a SQL schema and SQL query definitions. With NoSQL databases, like DynamoDB, it can be challenging to generate a schema for DynamoDB.

### A Simple DynamoDB Schema Strategy
This libary allows the implementation of a simple strategy for implementing robust CRUDL operations on schemas based directly on entities defined in an ER model. Let's use a very simple (and obviously incomplete) ER model to scaffold this discussion:

- OrderApp 
  - Customer records // 0 or more
    - Order records  // 0 or more
      - OrderItem records // 0 or more

Our DynamoDB table conventions are:
- Entity names are used as Partition Keys and stroed in a field named PK.
- The unique id of each record is stored as a string in a field named SK.

We store the "entity data", which is different for each entity of course, in a record field named Data. This entity data is stored as a serialized JSON document.

Following these conventions we are able to store an arbitary number of entities having different schema content in a single table with just these fields:
- PK - contains the entity name
- SK - contains the entity instance identifier. Drawn from the entity.Id attribute for example.
- Data - contains the entity instance data as a serialzied JSON

This is fairly rudamentary as we can only query records from the table on their Primary Keys (PK + SK). We generally want to query on additional entity attributes. For instance, let's say each OrderItem entity contained an attribute called OrderId and we want to use this attribute to get all the OrderItems for a specific Order.

We could read all the records of type OrderItem, Deserialize the Data field, check if the OrderItem.OrderId matches our Order.Id. Clearly, this is not a performant solution so we implement and use a Secondary Index.

#### Secondary Indices 
By our convention, each record is defined with five additional **string** fields to support Secondary Indicies.
- SK1..SK5 support indices Index-SK1 through Index-SK5. 

When we create/update a record we determine, by entity type, which, if any, of these secondary index fields are updated and copy the content from the entity data into the secondary index field. For example, to make SK1 an index that allows us to efficiently query for the OrderItem records on the OrderItem entity attribute OrderId, we copy that OrderId value into the SK1 field.

Secondary Indices are "sparse". If you don't store a value in a secondary index field, like SK1, then no entry is created in the index.

## DYDBRepository class 
The DYDBRepository and DataEnvelope classes make using DynamoDB tables implemented following the above conventions simple. 
- **DataEnvelope** - subclass to handle the details of assigning PK, SK, SK1, SK2, SK3, SK4 and SK5 from the entity data
- **DYDBRepository** - provides a set of simple CRUDL operations to access your table entities, some of these include:
  - CreateAsync(T data, string table, bool useCache)
  - ReadAsync(string pK, string sK, string table, bool useCache)
  - UpdateAsync(T data, string table)
  - DeleteAsync(string pK, string sK, string table)
  - ListAsync(QueryRequest queryRequest, bool useCache)

Review the IDYDBRepository for more details. 

## Advanced Implementations
You can implement some advanced features in your DataEnvelope subclass.

### Entity Versions
 One expected implementation that is supported by convention is the ability to transform versions of entity data on load. Each record is defined with a field called TypeName in which your DataEnvelope implementation may store the name and version of the entity data stored in the Data field. In our example, the Order entity might start with a TypeName = "Order.v1.0.0". Each record stored with this initial version of the DataEnvelope implmentation would have that TypeName value. 

As the application evolves we might change the schema of the Order entity. We modify the DataEnvelope to store the entity data with a new TypeName value = "Order.v1.0.1". We also add logic in the DataEnvelope to "transform" records read, which contain data in the older "Order.v1.0.0" schema, to the new format. We can even make the read process smart enough to automatically store the newly converted entity back to the database. 

### Optimistic Locking 
A common database transaction methodology is optimistic locking. The DynamoDBRepo class UpdateAsync() method implements optimistic locking. To support optimistic locking two fields are added to the record, CreatedAt and UpdatedAt. These field contain the UTC Ticks for the datetime the record was updated. 

The logic for optimistic locking is:
- read the existing record.
- compare the existing record UpdatedAt with the write candiate record's UpdatedAt. If they do not match then the update is abandoned and the client notified so it can take corrective action.

Optimistic locking has pros and cons. The most significant pro is that the expensive and computationally expensive database side transaction locking is avoided and only the client making the call may be impacted by the update operation. The most significant con is that the client has to handle failed updates.

### Caching 
The DYDBRepository also implements a very simple caching implementation. Use this capability with caution! It is not a sophisticated cache. Please read the source code and follow the caching logic before deciding to use this feature. 

### TTL 
Some records are transitory and need to be pruned from the database after a period of time.
We add a TTL Attribute to a record and set a Unix Epoch Time (signed 64-bit value) after which 
the record should be deleted. This feature requres that the TTL table feature is turned on.

We do not ADD a TTL attribute for records that should not participate in such deletions. 

### SetTopics() - supporting LzNotifications
The virtual method ```string SetTopics()``` is called during Create and Update. If it returns an empty string 
then nothing extra is done. If it returns a non-empty string, a Topics field is added to the record. 

The LazyStackNotificationsSharedCode project provides support for a WebSocket based notifications process
where Client apps can subscribe to topics and then receive notifications when records having those topics 
are written to the database. See that project for more information. 


### ListAsync 

ListAsync(callerInfo) -- list all items for PK
ListAsync(callerInfo, indexName, indexValue) -- list all items with indexName = SK, SK1 .. SK5
ListLessThanAsync(callerInfo, indexName, indexValue)
ListLessThanOrEqualAsync(callerInfo, indexName, indexValue) 
ListGreaterThanAsync(callerInfo, indexName, indexValue)
ListGreaterThanOrEqualAsync(callerInfo, indexName, indexValue)  
ListBetweenAsync(callerInfo, indexName, indexValue, indexValue2) - list items between indexValue and indexValue2
ListBeginsWithAsync(callerInfo, indexName, indexValue) - list items where indexValue is a prefix of the SK, SK1 .. SK5



