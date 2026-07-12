namespace Jellyfin.Plugin.LetterboxdSync.Letterboxd;

public record LetterboxdFilm(string Title, int? Year);

public record LetterboxdListSummary(string Slug, string Title);

public record LetterboxdListContent(string Title, string? Description, IReadOnlyList<LetterboxdFilm> Films);
