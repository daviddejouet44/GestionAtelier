using GestionAtelier.Endpoints.Fabrication;

namespace GestionAtelier.Endpoints;

public static class FabricationEndpointsExtensions
{
    public static void MapFabricationEndpoints(this WebApplication app)
    {
        app.MapFabricationCrudEndpoints();
        app.MapFabricationPdfEndpoints();
        app.MapFinitionStepsEndpoints();
    }
}
