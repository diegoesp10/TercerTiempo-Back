using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TercerTiempo.Application.Squads.Suspensions;

namespace TercerTiempo.Application.Tests;

// La API escribe el JSON como la web: en camelCase y sin distinguir mayúsculas. Dos propiedades que solo cambian en eso
// («Matchday», la jornada, y «MatchDay», el día) tumban la respuesta y el documento OpenAPI; aquí sale antes.
public class DtoJsonContractTests
{
    [Fact]
    public void Every_dto_can_be_written_as_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.MakeReadOnly();

        var failures = new List<string>();
        foreach (var type in typeof(SquadDisciplineDto).Assembly.GetExportedTypes()
                     .Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal) && !t.IsGenericTypeDefinition && !t.IsAbstract))
        {
            try
            {
                options.GetTypeInfo(type);
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
            {
                failures.Add($"{type.Name}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }
}
