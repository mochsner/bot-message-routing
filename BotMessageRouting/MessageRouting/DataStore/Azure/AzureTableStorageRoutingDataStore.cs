﻿using Microsoft.Bot.Schema;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Underscore.Bot.Models;
using Underscore.Bot.Utils;

namespace Underscore.Bot.MessageRouting.DataStore.Azure
{
    /// <summary>
    /// Routing data manager that stores the data in Azure Table Storage.
    /// 
    /// See IRoutingDataManager and AbstractRoutingDataManager for general documentation of
    /// properties and methods.
    /// </summary>

    [Serializable]
    public class AzureTableStorageRoutingDataStore : IRoutingDataStore
    {
        protected const string TableNameBotInstances = "BotInstances";
        protected const string TableNameUsers= "Users";
        protected const string TableNameAggregationChannels = "AggregationChannels";
        protected const string TableNameConnectionRequests = "ConnectionRequests";
        protected const string TableNameConnections = "Connections";
        protected const string PartitionKey = "PartitionKey";

        protected CloudTable _botInstancesTable;
        protected CloudTable _usersTable;
        protected CloudTable _aggregationChannelsTable;
        protected CloudTable _connectionRequestsTable;
        protected CloudTable _connectionsTable;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="connectionString">The connection string associated with an Azure Table Storage.</param>
        /// <param name="globalTimeProvider">The global time provider for providing the current
        /// time for various events such as when a connection is requested.</param>
        public AzureTableStorageRoutingDataStore(string connectionString, GlobalTimeProvider globalTimeProvider = null)
            : base()
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("The connection string cannot be null or empty");
            }

            _botInstancesTable = AzureStorageHelper.GetTable(connectionString, TableNameBotInstances);
            _usersTable = AzureStorageHelper.GetTable(connectionString, TableNameUsers);
            _aggregationChannelsTable = AzureStorageHelper.GetTable(connectionString, TableNameAggregationChannels);
            _connectionRequestsTable = AzureStorageHelper.GetTable(connectionString, TableNameConnectionRequests);
            _connectionsTable = AzureStorageHelper.GetTable(connectionString, TableNameConnections);

            MakeSureTablesExistAsync();
        }
        #region Get region
        public IList<ConversationReference> GetUsers()
        {
            var entities = GetAllEntitiesFromTable("botHandOff", _usersTable).Result;

            return GetAllConversationReferencesFromEntities(entities);
        }

        public IList<ConversationReference> GetBotInstances()
        {
            var entities = GetAllEntitiesFromTable("botHandOff", _botInstancesTable).Result;

            return GetAllConversationReferencesFromEntities(entities);
        }

        public IList<ConversationReference> GetAggregationChannels()
        {
            var entities = GetAllEntitiesFromTable("botHandOff", _aggregationChannelsTable).Result;

            return GetAllConversationReferencesFromEntities(entities);
        }

        public IList<ConnectionRequest> GetConnectionRequests()
        {
            IList<HandOffEntity> entities = GetAllEntitiesFromTable("botHandOff", _connectionRequestsTable).Result;

            List<ConnectionRequest> connectionRequests = new List<ConnectionRequest>();
            foreach (HandOffEntity entity in entities)
            {
                ConnectionRequest connectionRequest =
                    JsonConvert.DeserializeObject<ConnectionRequest>(entity.Body);
                connectionRequests.Add(connectionRequest);
            }
            return connectionRequests;
        }

        public IList<Connection> GetConnections()
        {
            IList<HandOffEntity> entities = GetAllEntitiesFromTable("botHandOff", _connectionsTable).Result;

            List<Connection> connections = new List<Connection>();
            foreach (HandOffEntity entity in entities)
            {
                Connection connection =
                    JsonConvert.DeserializeObject<Connection>(entity.Body);
                connections.Add(connection);
            }
            return connections;
        }
        #endregion

        #region Add region
        public bool AddConversationReference(ConversationReference conversationReferenceToAdd)
        {
            CloudTable table;
            if (conversationReferenceToAdd.Bot != null)
                table = _botInstancesTable;
            else table = _usersTable;

            return AzureStorageHelper.InsertAsync<HandOffEntity>(
                table,
                new HandOffEntity()
                {
                    Body = JsonConvert.SerializeObject(conversationReferenceToAdd),
                    PartitionKey = "handOffBot",
                    RowKey = conversationReferenceToAdd.Conversation.Id
                }).Result;
        }

        public bool AddAggregationChannel(ConversationReference aggregationChannelToAdd)
        {
            return AzureStorageHelper.InsertAsync<HandOffEntity>(
                _aggregationChannelsTable,
                new HandOffEntity()
                {
                    Body = JsonConvert.SerializeObject(aggregationChannelToAdd),
                    PartitionKey = "handOffBot",
                    RowKey = aggregationChannelToAdd.Conversation.Id
                }).Result;
        }

        public bool AddConnectionRequest(ConnectionRequest connectionRequestToAdd)
        {
            return AzureStorageHelper.InsertAsync<HandOffEntity>(
                _connectionRequestsTable,
                new HandOffEntity()
                {
                    Body = JsonConvert.SerializeObject(connectionRequestToAdd),
                    PartitionKey = "handOffBot",
                    RowKey = connectionRequestToAdd.Requestor.Conversation.Id
                }).Result;
        }
        public bool AddConnection(Connection connectionToAdd)
        {
            string rowKey = connectionToAdd.ConversationReference1.Conversation.Id +
                connectionToAdd.ConversationReference2.Conversation.Id;

            return AzureStorageHelper.InsertAsync<HandOffEntity>(
                _connectionRequestsTable,
                new HandOffEntity()
                {
                    Body = JsonConvert.SerializeObject(connectionToAdd),
                    PartitionKey = "handOffBot",
                    RowKey = rowKey
                }).Result;
        }
        #endregion

        #region Remove region
        public bool RemoveConversationReference(ConversationReference conversationReferenceToAdd)
        {
            CloudTable table;
            if (conversationReferenceToAdd.Bot != null)
                table = _botInstancesTable;
            else table = _usersTable;

            return AzureStorageHelper.DeleteEntryAsync<HandOffEntity>(
                table, 
                "handOffBot", 
                conversationReferenceToAdd.Conversation.Id).Result;
        }

        public bool RemoveAggregationChannel(ConversationReference toRemove)
        {
            return AzureStorageHelper.DeleteEntryAsync<HandOffEntity>(
                _connectionRequestsTable, 
                "handOffBot", 
                toRemove.Conversation.Id).Result;
        }

        public bool RemoveConnectionRequest(ConnectionRequest connectionRequestToRemove)
        {
            return AzureStorageHelper.DeleteEntryAsync<HandOffEntity>(
                _connectionRequestsTable, 
                "handOffBot", 
                connectionRequestToRemove.Requestor.Conversation.Id).Result;
        }

        public bool RemoveConnection(Connection connectionToRemove)
        {
            string rowKey = connectionToRemove.ConversationReference1.Conversation.Id +
                connectionToRemove.ConversationReference2.Conversation.Id;
            return AzureStorageHelper.DeleteEntryAsync<HandOffEntity>(
                _connectionsTable, 
                "handOffBot", 
                rowKey).Result;
        }
        #endregion

        // PARTIAL METHOD
        private static void CheckWichConversationReferenceIsNull(ConversationReference conversationOwnerConversationReference, ConversationReference conversationClientConversationReference, out string conversationOwnerAccountID, out string conversationClientAccountID)
        {
            if (conversationClientConversationReference.Bot != null)
                conversationClientAccountID = conversationClientConversationReference.Bot.Id;
            else conversationClientAccountID = conversationClientConversationReference.User.Id;

            if (conversationOwnerConversationReference.Bot != null)
                conversationOwnerAccountID = conversationOwnerConversationReference.Bot.Id;
            else conversationOwnerAccountID = conversationOwnerConversationReference.User.Id;
        }

        /// <summary>
        /// Makes sure the required tables exist.
        /// </summary>
        protected virtual async void MakeSureTablesExistAsync()
        {
            CloudTable[] cloudTables =
            {
                _botInstancesTable,
                _usersTable,
                _aggregationChannelsTable,
                _connectionRequestsTable,
                _connectionsTable
            };

            foreach (CloudTable cloudTable in cloudTables)
            {
                try
                {
                    await cloudTable.CreateIfNotExistsAsync();
                    System.Diagnostics.Debug.WriteLine($"Table '{cloudTable.Name}' created or did already exist");
                }
                catch (StorageException e)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create table '{cloudTable.Name}' (perhaps it already exists): {e.Message}");
                }
            }
        }

        protected virtual void OnPartiesTableCreateIfNotExistsFinished(IAsyncResult result)
        {
            if (result == null)
            {
                System.Diagnostics.Debug.WriteLine((result.IsCompleted)
                    ? "Create table operation for parties table completed"
                    : "Create table operation for parties table did not complete");
            }
        }

        protected virtual void OnConnectionsTableCreateIfNotExistsFinished(IAsyncResult result)
        {
            if (result == null)
            {
                System.Diagnostics.Debug.WriteLine((result.IsCompleted)
                    ? "Create table operation for connections table completed"
                    : "Create table operation for connections table did not complete");
            }
        }

        private List<ConversationReference> GetAllConversationReferencesFromEntities(IList<HandOffEntity> entities)
        {
            List<ConversationReference> conversationReferences = new List<ConversationReference>();
            foreach (HandOffEntity entity in entities)
            {
                ConversationReference conversationReference =
                    JsonConvert.DeserializeObject<ConversationReference>(entity.Body);
                conversationReferences.Add(conversationReference);
            }
            return conversationReferences;
        }

        private async Task<IList<HandOffEntity>> GetAllEntitiesFromTable(string partitionKey, CloudTable table)
        {
            TableQuery<HandOffEntity> query = new TableQuery<HandOffEntity>()
                .Where(TableQuery.GenerateFilterCondition(
                    "PartitionKey",
                    QueryComparisons.Equal,
                    partitionKey));

            return await table.ExecuteTableQueryAsync(query);
        }
    }

    public class HandOffEntity : TableEntity
    {
        public string Body { get; set; }
    }
}
