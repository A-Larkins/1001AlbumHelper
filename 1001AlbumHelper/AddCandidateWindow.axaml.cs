using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Puts an album on the shortlist. Deliberately offline: nothing here touches the spreadsheet,
/// because a potential is only a note to decide on later — the sheet is written when it's kept.
/// <para>
/// The year is optional. Leaving it blank is the normal case, since the shortlist fills years in
/// from Discogs on its own.
/// </para>
/// </summary>
public partial class AddCandidateWindow : Window
{
    private readonly IReadOnlyList<CandidateAlbum> _existing;

    // Null when no Discogs token is configured: the form still works, just without lookup.
    private readonly DiscogsApiClient? _discogs = DiscogsApiClient.TryCreate();

    /// <summary>A new album to append to the shortlist, once the dialog closes.</summary>
    public CandidateAlbum? Result { get; private set; }

    /// <summary>
    /// An album already on the shortlist but ruled on before, which the user asked to reconsider.
    /// Reported rather than changed here, so the shortlist stays the one thing that edits itself.
    /// </summary>
    public CandidateAlbum? Reopened { get; private set; }

    public AddCandidateWindow(IReadOnlyList<CandidateAlbum> existing)
    {
        _existing = existing;
        InitializeComponent();
        Opened += (_, _) => TitleBox.Focus();

        LookupHint.Text = AlbumLookup.Attach(
            _discogs, TitleBox, ArtistBox, YearBox,
            pick =>
            {
                LookupHint.Text = AlbumLookup.Picked(pick);
                StatusText.Text = "";
            });
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        string title = TitleBox.Text?.Trim() ?? "";
        string artist = ArtistBox.Text?.Trim() ?? "";
        string year = YearBox.Text?.Trim() ?? "";
        string genre = GenreBox.Text?.Trim() ?? "";

        if (title.Length == 0) { Fail("Enter the album title."); TitleBox.Focus(); return; }
        if (artist.Length == 0) { Fail("Enter the artist."); ArtistBox.Focus(); return; }

        // Blank is fine — the shortlist looks it up. Anything typed still has to be a real year.
        if (year.Length > 0
            && (!int.TryParse(year, out int parsed) || parsed < 1900 || parsed > DateTime.Now.Year + 1))
        {
            Fail("That year doesn't look right — four digits, or leave it blank.");
            YearBox.Focus();
            return;
        }

        var (outcome, match) = ReplacementCandidates.Classify(_existing, title, artist);
        switch (outcome)
        {
            case CandidateAddOutcome.AlreadyPending:
                Fail($"“{match!.Title}” by {match.Artist} is already on the shortlist, "
                   + "waiting to be decided on.");
                return;

            case CandidateAddOutcome.AlreadyKept:
                Fail($"“{match!.Title}” has already been kept — it's on your replacements list.");
                return;

            case CandidateAddOutcome.Reopen:
                // Dropped before and now wanted back, so put that row back rather than adding a
                // second one for the same album.
                Reopened = match;
                Close();
                return;
        }

        Result = new CandidateAlbum
        {
            Title = title,
            Artist = artist,
            Year = year,
            Genre = genre,
            Status = CandidateStatus.Pending,
        };
        Close();
    }

    private void Fail(string message) => StatusText.Text = $"✗ {message}";

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
