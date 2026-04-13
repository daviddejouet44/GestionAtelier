using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;
using GestionAtelier.Endpoints.Settings;

namespace GestionAtelier.Endpoints;

public static class SettingsEndpointsExtensions
{
    public static void MapSettingsEndpoints(this WebApplication app, string recyclePath)
    {
        app.MapScheduleEndpoints(recyclePath);
        app.MapMiscSettingsEndpoints(recyclePath);
        app.MapRoutingEndpoints(recyclePath);
        app.MapPrintConfigEndpoints(recyclePath);
        app.MapKanbanConfigEndpoints(recyclePath);
        app.MapAccountsEndpoints(recyclePath);
    }
}
