using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace _1001AlbumHelper;

/// <summary>
/// Lets the user see and remove tracks from a real Apple Music playlist directly, via
/// <see cref="MusicAppPlaylistWriter"/> (AppleScript against Music.app) — the workaround for Apple's
/// on-device MediaPlayer API being add-only. Mac-only; see PROJECT.md §5.
/// </summary>
public partial class AppleMusicPlaylistWindow : Window
{
    private string _playlistName = "PLAYLIST1";
    private bool _busy;

    public AppleMusicPlaylistWindow()
    {
        InitializeComponent();

        if (!MusicAppPlaylistWriter.IsAvailable)
        {
            StatusText.Text = "Music.app scripting is only available on macOS.";
            SetControlsEnabled(false);
            return;
        }

        Opened += async (_, _) => await LoadAsync();
    }

    private void OnPickPlaylist1(object? sender, RoutedEventArgs e) => SwitchPlaylist("PLAYLIST1");
    private void OnPickPlaylist2(object? sender, RoutedEventArgs e) => SwitchPlaylist("PLAYLIST2");

    private async void SwitchPlaylist(string name)
    {
        _playlistName = name;
        Playlist1Button.Classes.Set("on", name == "PLAYLIST1");
        Playlist2Button.Classes.Set("on", name == "PLAYLIST2");
        await LoadAsync();
    }

    private async void OnLoad(object? sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        SetControlsEnabled(false);
        StatusText.Text = $"Reading \"{_playlistName}\" from Music.app…";

        try
        {
            var albums = await MusicAppPlaylistWriter.ReadAlbumsAsync(_playlistName);
            Rows.ItemsSource = albums;
            CountText.Text = albums.Count == 1 ? "1 album" : $"{albums.Count} albums";
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            Rows.ItemsSource = Array.Empty<MusicAppAlbum>();
            CountText.Text = "";
            StatusText.Text = $"✗ {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetControlsEnabled(true);
        }
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { DataContext: MusicAppAlbum album }) return;

        _busy = true;
        SetControlsEnabled(false);
        StatusText.Text = $"Removing \"{album.Title}\"…";

        try
        {
            var (ok, message) = await MusicAppPlaylistWriter.RemoveAlbumAsync(_playlistName, album.Title, album.Artist);
            StatusText.Text = (ok ? "✓ " : "✗ ") + message;
            if (ok)
            {
                var remaining = ((IEnumerable<MusicAppAlbum>)(Rows.ItemsSource ?? Array.Empty<MusicAppAlbum>()))
                    .Where(a => a != album).ToList();
                Rows.ItemsSource = remaining;
                CountText.Text = remaining.Count == 1 ? "1 album" : $"{remaining.Count} albums";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"✗ {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetControlsEnabled(true);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void SetControlsEnabled(bool on)
    {
        LoadButton.IsEnabled = on;
        Playlist1Button.IsEnabled = on;
        Playlist2Button.IsEnabled = on;
    }
}
