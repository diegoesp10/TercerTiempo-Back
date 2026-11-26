using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Hosting;

namespace TercerTiempo.Application.Tests;

// Los mensajes son los que da .NET al enlazar los parámetros de un endpoint mínimo con ThrowOnBadRequest.
public class FrameworkProblemsTests
{
    [Theory]
    [InlineData("Required parameter \"Guid competitionId\" was not provided from query string.", ErrorCode.RequestMissingParameter, "competitionId")]
    [InlineData("Failed to bind parameter \"DateOnly from\" from \"ayer\".", ErrorCode.RequestInvalidParameter, "from")]
    [InlineData("Failed to bind parameter \"Dictionary<string, int> Filters\" from \"x\".", ErrorCode.RequestInvalidParameter, "filters")]
    [InlineData("Failed to read parameter \"CreateTeamRequest request\" from the request body as JSON.", ErrorCode.RequestInvalidBody, null)]
    [InlineData("Implicit body inferred for parameter \"request\" but no body was provided. Did you mean to use a Service instead?", ErrorCode.RequestInvalidBody, null)]
    [InlineData("Required parameter \"CreateTeamRequest request\" was not provided from body.", ErrorCode.RequestInvalidBody, null)]
    [InlineData("Unexpected end of request content.", ErrorCode.RequestInvalid, null)]
    public void A_bad_request_says_which_parameter_fails(string message, string code, string? parameter)
    {
        Assert.Equal((code, parameter), FrameworkProblems.FromBadRequest(StatusCodes.Status400BadRequest, message, null));
    }

    [Fact]
    public void Size_and_format_errors_keep_their_own_code()
    {
        Assert.Equal((ErrorCode.RequestTooLarge, null), FrameworkProblems.FromBadRequest(StatusCodes.Status413PayloadTooLarge, "Request body too large.", null));
        Assert.Equal((ErrorCode.RequestUnsupportedMediaType, null),
            FrameworkProblems.FromBadRequest(StatusCodes.Status415UnsupportedMediaType, "Expected a supported JSON media type but got \"text/plain\".", null));
        Assert.Equal((ErrorCode.RequestInvalidBody, null), FrameworkProblems.FromBadRequest(StatusCodes.Status400BadRequest, "x", new JsonException("y")));
    }

    [Fact]
    public void Empty_framework_responses_get_a_code_of_the_catalog()
    {
        Assert.Equal((ErrorCode.TitleUnauthenticated, ErrorCode.AuthSessionExpired), FrameworkProblems.ForStatus(401, sentToken: true, adminWrite: false));
        Assert.Equal((ErrorCode.TitleUnauthenticated, ErrorCode.AuthUnauthenticated), FrameworkProblems.ForStatus(401, sentToken: false, adminWrite: false));
        Assert.Equal((ErrorCode.TitleForbidden, ErrorCode.AuthAdminReadOnly), FrameworkProblems.ForStatus(403, sentToken: true, adminWrite: true));
        Assert.Equal((ErrorCode.TitleForbidden, ErrorCode.AuthForbidden), FrameworkProblems.ForStatus(403, sentToken: true, adminWrite: false));
        Assert.Equal((ErrorCode.TitleNotFound, ErrorCode.RequestNotFound), FrameworkProblems.ForStatus(404, sentToken: false, adminWrite: false));
        Assert.Equal(ErrorCode.RequestMethodNotAllowed, FrameworkProblems.ForStatus(405, sentToken: false, adminWrite: false).Code);
        Assert.Equal(ErrorCode.CommonUnexpected, FrameworkProblems.ForStatus(503, sentToken: false, adminWrite: false).Code);
    }

    [Fact]
    public async Task The_401_of_a_rejected_token_is_written_translated_and_with_its_code()
    {
        var services = new ServiceCollection();
        services.AddOptions().AddProblemDetails();
        services.AddSingleton<IErrorMessageResolver, CatalogResolver>();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Headers.Authorization = "Bearer caducado";
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Body = new MemoryStream();

        await FrameworkProblems.WriteStatusCodeAsync(new StatusCodeContext(http, new StatusCodePagesOptions(), _ => Task.CompletedTask));

        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("No autenticado", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(ErrorCatalog.Template(ErrorCode.AuthSessionExpired), json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(ErrorCode.AuthSessionExpired, json.RootElement.GetProperty("code").GetString());
    }

    private sealed class CatalogResolver : IErrorMessageResolver
    {
        public Task<string> ResolveAsync(string code, string language, object[] args, CancellationToken cancellationToken = default) =>
            Task.FromResult(ErrorCatalog.Format(code, args));
    }
}
