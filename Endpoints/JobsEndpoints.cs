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
using GestionAtelier.Endpoints.Jobs;

namespace GestionAtelier.Endpoints;

public static class JobsEndpointsExtensions
{
    public static void MapJobsEndpoints(this WebApplication app, string recyclePath)
    {
        app.MapJobsListEndpoints(recyclePath);
        app.MapJobsMoveEndpoints(recyclePath);
        app.MapJobsActionsEndpoints(recyclePath);
        app.MapJobsBatEndpoints(recyclePath);
    }
}
