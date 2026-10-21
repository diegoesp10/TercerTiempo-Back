using FluentValidation;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Common;

/// <summary>Reglas de FluentValidation con mensaje del catálogo: código, argumentos (para traducirlo) y texto por defecto.</summary>
internal static class ValidationExtensions
{
    public static IRuleBuilderOptions<T, TProperty> WithCatalogMessage<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule, string code, params object[] args) =>
        rule.WithErrorCode(code)
            .WithState(_ => args)
            .WithMessage(ErrorCatalog.Format(code, args));
}
