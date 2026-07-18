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
            var result = await Operations.AddReplacementAlbumAsync(title, artist, year);
            switch (result.Outcome)
            {
                case Operations.AddOutcome.Added:
                    StatusText.Text = $"✓ Added “{title}” ({year}) at #{result.Position}. "
                                    + "The list was renumbered."
                                    + (result.Warning is null ? "" : $"\n{result.Warning}");
                    TitleBox.Text = "";
                    ArtistBox.Text = "";
                    YearBox.Text = "";
                    TitleBox.Focus();
                    break;

                case Operations.AddOutcome.AlreadyInReplacements:
                    StatusText.Text = $"⚠️ Not added. {result.Detail}";
                    break;

                case Operations.AddOutcome.AlreadyIn1001:
                    StatusText.Text = $"⚠️ Not added. {result.Detail}";
                    break;

                case Operations.AddOutcome.NotConfigured:
                    StatusText.Text = $"✗ {result.Detail}";
                    break;

                default:
                    StatusText.Text = $"✗ Couldn't add “{title}”: {result.Detail}";
                    break;
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
