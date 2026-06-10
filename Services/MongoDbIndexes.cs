using System;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GestionAtelier.Services;

public static class MongoDbIndexes
{
    public static void EnsureIndexes()
    {
        // Collection deliveries
        var deliveries = MongoDbHelper.GetDeliveriesCollection();
        deliveries.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("fileName")));
        deliveries.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("fullPath")));

        // Collection fabrications
        var fab = MongoDbHelper.GetCollection<BsonDocument>("fabrications");
        fab.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("fileName")));
        fab.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("numeroDossier")));

        // Collection productionFolders
        var pf = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        pf.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("fileName")));
        pf.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("numeroDossier")));
        pf.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("createdAt")));

        // Collection client_accounts (portail)
        var clients = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
        clients.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("email"),
            new CreateIndexOptions { Unique = true }));

        // Collection activity_logs
        var logs = MongoDbHelper.GetCollection<BsonDocument>("activity_logs");
        logs.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("timestamp")));
    }
}
