using System.Reflection;
using TercerTiempo.Application.MatchReports;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

namespace TercerTiempo.Application.Tests;

// Las lesiones que cuentan las crónicas: quién, con qué frase y qué tipo encaja. Los textos están escritos para la prueba,
// con la misma forma que las crónicas de futbolasturiano.es.
public sealed class MatchReportInjuryTests
{
    // El J11 Europa de Nava 1-1 CD Vallobín: Diego Espina (Vallobín, visitante) sale en el minuto 9 por una luxación de codo
    // y entra Pelayo Suárez.
    private static readonly IReadOnlyList<ReportPlayer> J11 =
    [
        new(MatchSide.Away, "ESPINA RODRÍGUEZ, DIEGO", 8, Played: true, MinuteOut: 9),
        new(MatchSide.Away, "SUÁREZ MENÉNDEZ, PELAYO", 14, Played: true, MinuteOut: null),
        new(MatchSide.Away, "GARCÍA FERNÁNDEZ, ALFONSO", 10, Played: true, MinuteOut: 75),
        new(MatchSide.Away, "RÍOS PRIETO, MARCOS", 9, Played: true, MinuteOut: null),
        new(MatchSide.Away, "GIL CUERVO, IVÁN", 16, Played: false, MinuteOut: null),
        new(MatchSide.Home, "ÁLVAREZ SUÁREZ, ESTEBAN", 5, Played: true, MinuteOut: null),
        new(MatchSide.Home, "DE LA FUENTE GARCÍA, JUAN CARLOS", 7, Played: true, MinuteOut: 60),
        new(MatchSide.Home, "LÓPEZ CANO, RUBÉN", 1, Played: true, MinuteOut: null),
    ];

    [Fact]
    public void La_luxacion_del_J11_sale_con_el_minuto_del_acta()
    {
        const string report =
            "El Europa de Nava y el Vallobín firmaron un empate en un partido muy disputado.\n"
            + "Pocos minutos después del inicio, en un balón dividido dentro del área, Diego Espina cayó mal y sufrió una luxación en el codo izquierdo. "
            + "En su lugar entró Pelayo Suárez.\n"
            + "Fonsi adelantó al Vallobín antes del descanso y el Europa empató en la segunda parte.";

        var found = Assert.Single(InjuryMentionDetector.Detect(report, J11));
        Assert.Equal("ESPINA RODRÍGUEZ, DIEGO", found.Player.FullName);
        Assert.Equal(9, found.Player.MinuteOut);
        Assert.Equal("luxación en el codo izquierdo", found.Phrase);
        Assert.StartsWith("Pocos minutos después", found.Excerpt);
    }

    [Fact]
    public void Con_dos_nombres_es_el_que_deja_el_campo()
    {
        var found = Assert.Single(InjuryMentionDetector.Detect("Diego Espina se lesionó y le sustituyó Pelayo Suárez.", J11));
        Assert.Equal("ESPINA RODRÍGUEZ, DIEGO", found.Player.FullName);
        Assert.Equal("lesionó y le sustituyó Pelayo Suárez", found.Phrase);
    }

    [Fact]
    public void Sin_nombre_en_la_frase_vale_el_de_la_anterior()
    {
        const string report = "Juan Carlos de la Fuente chocó con el portero en un córner. Tuvo que ser sustituido poco después.";

        var found = Assert.Single(InjuryMentionDetector.Detect(report, J11));
        Assert.Equal("DE LA FUENTE GARCÍA, JUAN CARLOS", found.Player.FullName);
        Assert.Equal(report, found.Excerpt);
        Assert.Equal("Tuvo que ser sustituido poco después", found.Phrase);
    }

    [Fact]
    public void Quien_entra_en_su_lugar_no_es_el_lesionado()
    {
        // "Madeira" es un apodo que no está en el acta: no se sabe quién es, y desde luego no es quien le sustituye.
        Assert.Empty(InjuryMentionDetector.Detect("En el 32, Madeira sufrió una lesión en el isquio y fue sustituido por Marcos Ríos.", J11));

        // Sin el lesionado en la frase (solo quien entra), vale el de la anterior.
        const string report = "Diego Espina cayó mal en un salto. Fue sustituido por Pelayo Suárez tras lesionarse en el codo.";
        var found = Assert.Single(InjuryMentionDetector.Detect(report, J11));
        Assert.Equal("ESPINA RODRÍGUEZ, DIEGO", found.Player.FullName);
        Assert.Equal(report, found.Excerpt);
    }

    [Fact]
    public void Un_solo_apellido_o_diminutivo_vale_si_nadie_mas_se_llama_asi()
    {
        Assert.Equal("RÍOS PRIETO, MARCOS", Assert.Single(InjuryMentionDetector.Detect("Al final, Ríos acabó con molestias en el gemelo.", J11)).Player.FullName);
        Assert.Equal("GARCÍA FERNÁNDEZ, ALFONSO", Assert.Single(InjuryMentionDetector.Detect("En el 75, Fonsi se retiró con molestias.", J11)).Player.FullName);
        // Suárez es Pelayo y también el segundo apellido de Esteban: no se sabe de quién habla.
        Assert.Empty(InjuryMentionDetector.Detect("En la segunda parte, Suárez se marchó lesionado.", J11));
    }

    [Theory]
    [InlineData("Marcos Ríos, que volvía tras su lesión, firmó el gol del empate.")]
    [InlineData("Marcos Ríos reaparecía tras dos meses de baja.")]
    [InlineData("Iván Gil, baja por lesión, vio el partido desde la grada.")]
    [InlineData("Diego Espina se dolió del tobillo, pero todo quedó en un susto.")]
    [InlineData("Este jugador acabó lesionado en el último partido de la temporada pasada.")]
    [InlineData("Pablo Espina, el delegado, contó que el portero rival se lesionó en el calentamiento.")]
    public void No_propone_lo_que_no_es_una_lesion_de_este_partido(string report) =>
        Assert.Empty(InjuryMentionDetector.Detect(report, J11));

    [Fact]
    public void Quien_no_jugo_no_se_lesiono_en_el_partido() =>
        Assert.Empty(InjuryMentionDetector.Detect("Iván Gil se lesionó en el calentamiento.", J11));

    [Fact]
    public void Se_queda_la_frase_que_dice_que_lesion_es()
    {
        const string report = "Diego Espina tuvo que retirarse en el minuto 9. Diego Espina sufrió un esguince de tobillo.";

        var found = Assert.Single(InjuryMentionDetector.Detect(report, J11));
        Assert.Equal("esguince de tobillo", found.Phrase);
    }

    [Fact]
    public void El_tipo_que_encaja_con_la_cronica()
    {
        var catalog = new[]
        {
            Type("AnkleSprain", "Esguince de tobillo"), Type("MuscleTear", "Rotura fibrilar"), Type("Fracture", "Fractura"),
            Type("Contusion", "Contusión / golpe"), Type("Other", "Otra"),
        };

        Assert.Equal("AnkleSprain", InjuryTypeSuggester.Suggest("sufrió un esguince en el tobillo derecho", catalog));
        Assert.Equal("Fracture", InjuryTypeSuggester.Suggest("se rompió la clavícula al caer", catalog));
        Assert.Equal("MuscleTear", InjuryTypeSuggester.Suggest("un pinchazo en el isquiotibial", catalog));
        Assert.Equal("Other", InjuryTypeSuggester.Suggest("luxación en el codo izquierdo", catalog));
        // Un tipo creado por el administrador se reconoce por su nombre.
        Assert.Equal("Luxation", InjuryTypeSuggester.Suggest("luxación en el codo izquierdo", [.. catalog, Type("Luxation", "Luxación")]));
    }

    [Fact]
    public void Una_cronica_se_lee_al_cerrar_el_acta_y_otra_vez_al_dia_siguiente()
    {
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var matchId = Guid.NewGuid();
        Assert.True(SyncMatchReportsHandler.IsDue(null, now));

        var withReport = MatchReportCheck.First(matchId, found: true, now);
        Assert.False(SyncMatchReportsHandler.IsDue(withReport, now.AddHours(23)));
        Assert.True(SyncMatchReportsHandler.IsDue(withReport, now.AddHours(24)));
        withReport.Again(found: true, now.AddHours(24));
        Assert.True(withReport.Done);
        Assert.False(SyncMatchReportsHandler.IsDue(withReport, now.AddDays(3)));

        var without = MatchReportCheck.First(matchId, found: false, now);
        Assert.False(SyncMatchReportsHandler.IsDue(without, now.AddHours(5)));
        Assert.True(SyncMatchReportsHandler.IsDue(without, now.AddHours(6)));
    }

    [Fact]
    public void Borrar_la_lesion_registrada_deja_la_propuesta_descartada()
    {
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var mention = MatchInjuryMention.Create(
            Guid.NewGuid(), MatchSide.Away, "ESPINA RODRÍGUEZ, DIEGO", 8, 9, "Diego Espina sufrió una luxación en el codo.", "luxación en el codo",
            "futbolasturiano", "https://example.com/cronica", now);
        var proposal = InjuryProposal.Create(Guid.NewGuid(), Guid.NewGuid(), mention, now);
        var injuryId = Guid.NewGuid();

        proposal.Register(injuryId, SquadEditor.Coach(Guid.NewGuid()), now);
        Assert.Equal(InjuryProposalStatus.Registered, proposal.Status);
        Assert.Equal(injuryId, proposal.InjuryId);

        proposal.ForgetInjury(now.AddDays(1));
        Assert.Equal(InjuryProposalStatus.Dismissed, proposal.Status);
        Assert.Null(proposal.InjuryId);
        Assert.False(proposal.IsPending);
        // Descartada no vuelve a cambiar con la crónica.
        Assert.False(proposal.Refresh(mention));
    }

    [Fact]
    public void La_cronica_en_texto_un_parrafo_por_linea()
    {
        const string html =
            "<h3><img src=\"https://example.com/a.jpg\" alt=\"\" /></h3>\n<h3>Empate en Nava</h3>\n"
            + "<p>Primer p&aacute;rrafo con <strong>negrita</strong>&nbsp;y un enlace <a href=\"#\">aqu&iacute;</a>.</p>\n"
            + "<blockquote><p>Una cita.</p></blockquote>\n<p></p>";

        Assert.Equal("Empate en Nava\nPrimer párrafo con negrita y un enlace aquí.\nUna cita.", ReportText.FromHtml(html));
    }

    private static InjuryTypeDefinition Type(string code, string label)
    {
        var type = (InjuryTypeDefinition)Activator.CreateInstance(typeof(InjuryTypeDefinition), nonPublic: true)!;
        Set(type, nameof(InjuryTypeDefinition.Code), code);
        Set(type, nameof(InjuryTypeDefinition.Label), label);
        Set(type, nameof(InjuryTypeDefinition.Active), true);
        return type;
    }

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
}
