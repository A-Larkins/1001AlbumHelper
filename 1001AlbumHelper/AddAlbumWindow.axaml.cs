using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>Adds an album to the replacements list, placed by year and renumbered.</summary>
public partial class AddAlbumWindow : Window
{
    private bool _busy;

    public AddAlbumWindow()
    {
        InitializeComponent();
        Opened += (_, _) => TitleBox.Focus();
    }

    private async void OnAdd(object? sender, RoutedEventArgs e) => await AddAsync();

    private async Task AddAsync()
    {
        if (_busy) return;

        string title = TitleBox.Text?.Trim() ?? "";
        string artist = ArtistBox.Text?.Trim() ?? "";
        string yearText = YearBox.Text?.Trim() ?? "";

        if (title.Length == 0) { Fail("Enter the album title."); TitleBox.Focus(); return; }
        if (artist.Length == 0) { Fail("Enter the artist."); ArtistBox.Focus(); return; }
        if (!int.TryParse(yearText, out int year) || year < 1900 || year > DateTime.Now.Year + 1)
        {
            Fail("Enter a four-digit year.");
            YearBox.Focus();
            return;
        }

        _busy = true;
        AddButton.IsEnabled = false;
        StatusText.Text = "Adding…";

        try
        {
            int? position = await Operations.AddReplacementAlbumAsync(title, artist, year);
            if (position is null)
            {
                // Operations already explained why to the log; surface something actionable here.
                StatusText.Text = $"✗ Couldn't add “{title}” — it may already be on the list, " +
                                  "or Google Sheets isn't reachable. See the main window's log.";
            }
            else
            {
                StatusText.Text = $"✓ Added “{title}” ({year}) at #{position}. The list was renumbered.";
                TitleBox.Text = "";
                ArtistBox.Text = "";
                YearBox.Text = "";
                TitleBox.Focus();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✗ {ex.Message}";
        }
        finally
        {
            _busy = false;
            AddButton.IsEnabled = true;
        }
    }

    private void Fail(string message) => StatusText.Text = $"✗ {message}";

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
