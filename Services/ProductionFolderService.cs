using System;
using System.IO;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class ProductionFolderService
{
    public static async Task EnsureProductionFolderAsync(string movedFilePath)
        => await BackendUtils.EnsureProductionFolderAsync(movedFilePath);

    public static async Task CopyToProductionFolderStageAsync(string filePath, string stage)
        => await BackendUtils.CopyToProductionFolderStageAsync(filePath, stage);
}
