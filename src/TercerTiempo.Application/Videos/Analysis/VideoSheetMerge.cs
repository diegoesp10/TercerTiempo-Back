using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.TeamStats;

namespace TercerTiempo.Application.Videos.Analysis;

// Los datos de equipo que salen del vídeo y el cuerpo técnico puede aceptar uno a uno.
public enum VideoTeamField
{
    Possession = 1,
    Shots = 2,
    ShotsOnTarget = 3,
    Corners = 4,
    ThrowIns = 5,
    GoalKicks = 6,
}

// Un vídeo ya resuelto por el cuerpo técnico: cuál de los dos equipos detectados es el suyo.
internal sealed record VideoPartSelection(VideoProposal Proposal, int OurTeam, int AnalyzedSeconds);

// Lo que propone el vídeo para el equipo, visto desde el club («a favor» y «en contra»), sumadas todas sus partes.
public sealed record VideoTeamTotals(
    int? PossessionPct,
    int ShotsFor,
    int ShotsAgainst,
    int ShotsOnTargetFor,
    int ShotsOnTargetAgainst,
    int CornersFor,
    int CornersAgainst,
    int ThrowInsFor,
    int ThrowInsAgainst,
    int GoalKicksFor,
    int GoalKicksAgainst);

// Cómo se lleva la propuesta del vídeo al acta. Dos reglas, las mismas para el equipo y para cada jugador:
//   - un cero de la propuesta no es un dato (el vídeo no vio nada): nunca borra lo que ya hay apuntado;
//   - lo que cambiaría un valor ya apuntado se anota en «overwritten» (con el valor de antes y el de después), y
//     quien llama decide si hay confirmación;
//   - los goles de un jugador solo se apuntan si su línea no tiene ninguno: mandan los del acta oficial.
internal static class VideoSheetMerge
{
    public static VideoTeamTotals Totals(IReadOnlyList<VideoPartSelection> parts)
    {
        int? possession = null;
        var weighted = parts
            .Select(p => (Possession: p.Proposal.Team(p.OurTeam)?.PossessionPct, Weight: Math.Max(1, p.AnalyzedSeconds)))
            .Where(p => p.Possession is not null)
            .ToList();
        if (weighted.Count > 0)
            possession = (int)Math.Round(weighted.Sum(p => (double)p.Possession!.Value * p.Weight) / weighted.Sum(p => p.Weight), MidpointRounding.AwayFromZero);

        int Ours(Func<VideoProposalTeam, int> value) => parts.Sum(p => p.Proposal.Team(p.OurTeam) is { } team ? value(team) : 0);
        int Theirs(Func<VideoProposalTeam, int> value) => parts.Sum(p => p.Proposal.Team(1 - p.OurTeam) is { } team ? value(team) : 0);

        return new VideoTeamTotals(
            possession,
            Ours(t => t.Shots), Theirs(t => t.Shots),
            Ours(t => t.ShotsOnTarget), Theirs(t => t.ShotsOnTarget),
            Ours(t => t.Corners), Theirs(t => t.Corners),
            Ours(t => t.ThrowIns), Theirs(t => t.ThrowIns),
            Ours(t => t.GoalKicks), Theirs(t => t.GoalKicks));
    }

    public static TeamMatchStatsInput MergeTeam(
        TeamMatchStatsInput current, VideoTeamTotals video, IReadOnlyCollection<VideoTeamField> fields, List<string> overwritten, ref int written)
    {
        var result = current;

        if (fields.Contains(VideoTeamField.Possession) && video.PossessionPct is { } possession)
        {
            if (current.PossessionPct is { } had && had != possession) overwritten.Add($"posesión ({had} → {possession})");
            if (current.PossessionPct != possession) written++;
            result = result with { PossessionPct = possession };
        }

        int Take(int had, int proposed, string label, ref int count)
        {
            if (proposed == 0 || proposed == had) return had;
            if (had != 0) overwritten.Add($"{label} ({had} → {proposed})");
            count++;
            return proposed;
        }

        if (fields.Contains(VideoTeamField.Shots))
            result = result with
            {
                ShotsFor = Take(current.ShotsFor, video.ShotsFor, "tiros", ref written),
                ShotsAgainst = Take(current.ShotsAgainst, video.ShotsAgainst, "tiros del rival", ref written),
            };
        if (fields.Contains(VideoTeamField.ShotsOnTarget))
            result = result with
            {
                ShotsOnTargetFor = Take(current.ShotsOnTargetFor, video.ShotsOnTargetFor, "tiros a puerta", ref written),
                ShotsOnTargetAgainst = Take(current.ShotsOnTargetAgainst, video.ShotsOnTargetAgainst, "tiros a puerta del rival", ref written),
            };
        if (fields.Contains(VideoTeamField.Corners))
            result = result with
            {
                CornersFor = Take(current.CornersFor, video.CornersFor, "córners", ref written),
                CornersAgainst = Take(current.CornersAgainst, video.CornersAgainst, "córners del rival", ref written),
            };
        if (fields.Contains(VideoTeamField.ThrowIns))
            result = result with
            {
                ThrowInsFor = Take(current.ThrowInsFor, video.ThrowInsFor, "saques de banda", ref written),
                ThrowInsAgainst = Take(current.ThrowInsAgainst, video.ThrowInsAgainst, "saques de banda del rival", ref written),
            };
        if (fields.Contains(VideoTeamField.GoalKicks))
            result = result with
            {
                GoalKicksFor = Take(current.GoalKicksFor, video.GoalKicksFor, "saques de puerta", ref written),
                GoalKicksAgainst = Take(current.GoalKicksAgainst, video.GoalKicksAgainst, "saques de puerta del rival", ref written),
            };

        return result;
    }

    // La línea del jugador con lo que propone el vídeo, o null si no hay nada que cambiar. Sin línea previa se crea con
    // los minutos que se le ha visto (el acta no admite acciones sin minutos): el cuerpo técnico los corrige después.
    // Tiros y tiros a puerta van juntos, y pases completados e intentados también, para que nunca queden descuadrados.
    public static MatchSheetEntryInput? MergeLine(
        Guid squadPlayerId, string playerName, MatchSheetEntryInput? current, VideoPlayerActions video, int minutesVisible, List<string> overwritten)
    {
        if (!video.HasSheetData) return null;

        var minutes = Math.Clamp(minutesVisible, 1, Domain.Performances.PerformanceStats.MaxMinutes);
        var line = current ?? new MatchSheetEntryInput(squadPlayerId, true, false, minutes, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, null);
        if (line.MinutesPlayed == 0) line = line with { MinutesPlayed = minutes };
        var before = line;

        void Changed(int had, int proposed, string label)
        {
            if (had != 0 && had != proposed) overwritten.Add($"{label} de {playerName} ({had} → {proposed})");
        }

        if (video.Shots > 0)
        {
            Changed(line.Shots, video.Shots, "tiros");
            Changed(line.ShotsOnTarget, video.ShotsOnTarget, "tiros a puerta");
            line = line with { Shots = video.Shots, ShotsOnTarget = video.ShotsOnTarget };
        }
        if (video.PassesAttempted > 0)
        {
            Changed(line.PassesAttempted, video.PassesAttempted, "pases intentados");
            Changed(line.PassesCompleted, video.PassesCompleted, "pases completados");
            line = line with { PassesAttempted = video.PassesAttempted, PassesCompleted = video.PassesCompleted };
        }
        if (video.Goals > 0 && line.Goals == 0)
            line = line with { Goals = video.Goals };
        if (video.Saves > 0)
        {
            Changed(line.Saves, video.Saves, "paradas");
            line = line with { Saves = video.Saves };
        }
        if (video.Recoveries > 0)
        {
            Changed(line.Recoveries, video.Recoveries, "recuperaciones");
            line = line with { Recoveries = video.Recoveries };
        }
        if (video.BallLosses > 0)
        {
            Changed(line.BallLosses, video.BallLosses, "pérdidas");
            line = line with { BallLosses = video.BallLosses };
        }

        return current is not null && line == before ? null : line;
    }

    public static string Describe(IReadOnlyList<string> overwritten, int shown = 6) =>
        overwritten.Count <= shown
            ? string.Join(", ", overwritten)
            : $"{string.Join(", ", overwritten.Take(shown))} y {overwritten.Count - shown} más";
}
