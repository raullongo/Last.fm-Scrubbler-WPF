﻿using Caliburn.Micro;
using IF.Lastfm.Core.Objects;
using Last.fm_Scrubbler_WPF.Models;
using Last.fm_Scrubbler_WPF.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace Last.fm_Scrubbler_WPF.ViewModels
{
  /// <summary>
  /// Media player database to scrobble tracks from.
  /// </summary>
  public enum MediaPlayerDatabaseType
  {
    /// <summary>
    /// Scrobble from iTunes database (.xml).
    /// </summary>
    iTunes_Or_Winamp,

    /// <summary>
    /// Scrobble from Windows Media Player.
    /// </summary>
    WMP
  }

  /// <summary>
  /// ViewModel for the <see cref="MediaPlayerDatabaseScrobbleView"/>.
  /// </summary>
  public class MediaPlayerDatabaseScrobbleViewModel : ScrobbleViewModelBase
  {
    #region Properties

    /// <summary>
    /// Path to the database file.
    /// </summary>
    public string DBFilePath
    {
      get { return _dbFilePath; }
      set
      {
        _dbFilePath = value;
        NotifyOfPropertyChange(() => DBFilePath);
      }
    }
    private string _dbFilePath;

    /// <summary>
    /// Media player database to scrobble tracks from.
    /// </summary>
    public MediaPlayerDatabaseType MediaPlayerDatabaseType
    {
      get { return _mediaPlayerDatabaseType; }
      set
      {
        _mediaPlayerDatabaseType = value;
        NotifyOfPropertyChange(() => MediaPlayerDatabaseType);
        NotifyOfPropertyChange(() => CanSelectFile);
      }
    }
    private MediaPlayerDatabaseType _mediaPlayerDatabaseType;

    /// <summary>
    /// List of parsed scrobbles.
    /// </summary>
    public ObservableCollection<MediaDBScrobbleViewModel> ParsedScrobbles
    {
      get { return _parsedScrobbles; }
      private set
      {
        _parsedScrobbles = value;
        NotifyOfPropertyChange(() => ParsedScrobbles);
      }
    }
    private ObservableCollection<MediaDBScrobbleViewModel> _parsedScrobbles;

    /// <summary>
    /// Gets if certain controls are enabled on the UI:
    /// </summary>
    public override bool EnableControls
    {
      get { return _enableControls; }
      protected set
      {
        _enableControls = value;
        NotifyOfPropertyChange(() => EnableControls);
        NotifyOfPropertyChange(() => CanScrobble);
        NotifyOfPropertyChange(() => CanPreview);
        NotifyOfPropertyChange(() => CanSelectAll);
        NotifyOfPropertyChange(() => CanSelectNone);
        NotifyOfPropertyChange(() => CanSelectFile);
      }
    }

    /// <summary>
    /// Gets if the "..." button is enabled on the ui.
    /// </summary>
    public bool CanSelectFile
    {
      get { return MediaPlayerDatabaseType != MediaPlayerDatabaseType.WMP && EnableControls; }
    }

    /// <summary>
    /// Gets if the "Scrobble" button is enabled on the UI.
    /// </summary>
    public override bool CanScrobble
    {
      get { return base.CanScrobble && ParsedScrobbles.Any(i => i.ToScrobble) && EnableControls; }
    }

    /// <summary>
    /// Gets if the preview button is enabled.
    /// </summary>
    public override bool CanPreview
    {
      get { return ParsedScrobbles.Any(i => i.ToScrobble) && EnableControls; }
    }
    /// <summary>
    /// Gets if the "Select ALL" button is enabled on the UI.
    /// </summary>
    public bool CanSelectAll
    {
      get { return ParsedScrobbles.Any(i => !i.ToScrobble) && EnableControls; }
    }

    /// <summary>
    /// Gets if the "Select NONE" button is enabled on the UI.
    /// </summary>
    public bool CanSelectNone
    {
      get { return ParsedScrobbles.Any(i => i.ToScrobble) && EnableControls; }
    }

    #endregion Properties

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="windowManager">WindowManager used to display dialogs.</param>
    public MediaPlayerDatabaseScrobbleViewModel(IWindowManager windowManager)
      : base(windowManager, "Media Player Database Scrobbler")
    {
      ParsedScrobbles = new ObservableCollection<MediaDBScrobbleViewModel>();
    }

    /// <summary>
    /// Shows an <see cref="OpenFileDialog"/> and lets the user
    /// select a database file.
    /// Filter will be applied depending on selected <see cref="MediaPlayerDatabaseType"/>.
    /// </summary>
    public void SelectFile()
    {
      using (OpenFileDialog ofd = new OpenFileDialog())
      {
        if (MediaPlayerDatabaseType == MediaPlayerDatabaseType.iTunes_Or_Winamp)
          ofd.Filter = "iTunes/Winamp Database XML (*.xml) | *.xml";

        if (ofd.ShowDialog() == DialogResult.OK)
          DBFilePath = ofd.FileName;
      }
    }

    /// <summary>
    /// Parses the <see cref="DBFilePath"/>.
    /// </summary>
    public void ParseFile()
    {
      if (MediaPlayerDatabaseType == MediaPlayerDatabaseType.iTunes_Or_Winamp)
        ParseItunesConformXML();
      else if (MediaPlayerDatabaseType == MediaPlayerDatabaseType.WMP)
        ParseWMPDatabase();
    }

    /// <summary>
    /// Marks all scrobbles as "ToScrobble".
    /// </summary>
    public void SelectAll()
    {
      foreach (MediaDBScrobbleViewModel s in ParsedScrobbles.Where(i => !i.ToScrobble))
      {
        s.ToScrobble = true;
      }
    }

    /// <summary>
    /// Marks all scrobbles as not "ToScrobble".
    /// </summary>
    public void SelectNone()
    {
      foreach (MediaDBScrobbleViewModel s in ParsedScrobbles.Where(i => i.ToScrobble))
      {
        s.ToScrobble = false;
      }
    }

    /// <summary>
    /// Parse the <see cref="DBFilePath"/>.
    /// A lot of media players use the same xml format.
    /// </summary>
    private async void ParseItunesConformXML()
    {
      EnableControls = false;

      XmlDocument xmlDocument = new XmlDocument();
      try
      {
        xmlDocument.Load(DBFilePath);
      }
      catch (Exception ex)
      {
        EnableControls = true;
        OnStatusUpdated("Could not load database file: " + ex.Message);
        return;
      }

      // node that points to all tracks
      XmlNode trackDictNode = null;
      try
      {
        trackDictNode = xmlDocument.ChildNodes[2].ChildNodes[0].ChildNodes.OfType<XmlNode>().First(i => i.Name == "dict");
        if (trackDictNode == null)
          throw new Exception("Could not find 'Tracks' node in xml file");
      }
      catch (Exception ex)
      {
        EnableControls = true;
        OnStatusUpdated("Error parsing database file: " + ex.Message);
        return;
      }

      List<MediaDBScrobbleViewModel> scrobbles = new List<MediaDBScrobbleViewModel>();
      Dictionary<XmlNode, string> errorNodes = new Dictionary<XmlNode, string>();

      int count = 1;
      var dictNodes = trackDictNode.ChildNodes.OfType<XmlNode>().Where(i => i.Name == "dict");
      foreach (XmlNode trackNode in dictNodes)
      {
        await Task.Run(() =>
        {
          try
          {
            var xmlNodes = trackNode.ChildNodes.OfType<XmlNode>();
            int playCount = int.Parse(xmlNodes.First(i => i.InnerText == "Play Count").NextSibling.InnerText);
            string trackName = xmlNodes.First(i => i.InnerText == "Name").NextSibling.InnerText;
            string artistName = xmlNodes.First(i => i.InnerText == "Artist").NextSibling.InnerText;
            string albumName = xmlNodes.FirstOrDefault(i => i.InnerText == "Album")?.NextSibling.InnerText;
            string albumArtist = xmlNodes.FirstOrDefault(i => i.InnerText == "Album Artist")?.NextSibling.InnerText;
            TimeSpan duration = TimeSpan.FromMilliseconds(int.Parse(xmlNodes.FirstOrDefault(i => i.InnerText == "Total Time")?.NextSibling.InnerText));
            DateTime lastPlayed = DateTime.Parse(xmlNodes.FirstOrDefault(i => i.InnerText == "Play Date UTC")?.NextSibling.InnerText);

            MediaDBScrobbleViewModel vm = new MediaDBScrobbleViewModel(new MediaDBScrobble(playCount, lastPlayed, trackName, artistName, albumName, albumArtist, duration));
            vm.ToScrobbleChanged += ToScrobbleChanged;
            scrobbles.Add(vm);
          }
          catch (Exception ex)
          {
            // corrupted or something, log and continue.
            errorNodes.Add(trackNode, ex.Message);
          }
          finally
          {
            OnStatusUpdated("Parsing database file... " + count++ + " / " + dictNodes.Count());
          }
        });
      }

      OnStatusUpdated("Successfully parsed database file");
      ParsedScrobbles = new ObservableCollection<MediaDBScrobbleViewModel>(scrobbles);
      EnableControls = true;
    }

    /// <summary>
    /// Parses the windows media player database.
    /// </summary>
    private async void ParseWMPDatabase()
    {
      EnableControls = false;

      try
      {
        await Task.Run(() =>
        {
          OnStatusUpdated("Parsing Windows Media Player library...");
          using (WMP wmp = new WMP())
          {
            // todo: this can be better
            var scrobbles = wmp.GetMusicLibrary();
            var scrobbleVMs = new List<MediaDBScrobbleViewModel>();
            foreach (var scrobble in scrobbles)
            {
              var vm = new MediaDBScrobbleViewModel(scrobble);
              vm.ToScrobbleChanged += ToScrobbleChanged;
              scrobbleVMs.Add(vm);
            }

            ParsedScrobbles = new ObservableCollection<MediaDBScrobbleViewModel>(scrobbleVMs);
          }
          OnStatusUpdated("Successfully parsed Windows Media Player library");
        });
      }
      catch (Exception ex)
      {
        OnStatusUpdated("Fatal error while parsing Windows Media Player library. Error: " + ex.Message);
      }
      finally
      {
        EnableControls = true;
      }
    }

    /// <summary>
    /// Scrobbles the selected tracks.
    /// </summary>
    /// <returns>Task.</returns>
    public override async Task Scrobble()
    {
      EnableControls = false;

      try
      {
        OnStatusUpdated("Trying to scrobble selected tracks...");

        var response = await Scrobbler.ScrobbleAsync(CreateScrobbles());
        if (response.Success)
          OnStatusUpdated("Successfully scrobbled!");
        else
          OnStatusUpdated("Error while scrobbling!");
      }
      catch (Exception ex)
      {
        OnStatusUpdated("Fatal error while trying to scrobble selected tracks. Error: " + ex.Message);
      }
      finally
      {
        EnableControls = true;
      }
    }

    /// <summary>
    /// Creates a list with scrobbles that will be scrobbled.
    /// </summary>
    /// <returns>List with scrobbles.</returns>
    protected override IEnumerable<Scrobble> CreateScrobbles()
    {
      List<Scrobble> scrobbles = new List<Scrobble>();

      DateTime time = DateTime.Now; ;
      foreach (var vm in ParsedScrobbles.Where(i => i.ToScrobble))
      {
        for (int i = 0; i < vm.Scrobble.PlayCount; i++)
        {
          scrobbles.Add(new Scrobble(vm.Scrobble.ArtistName, vm.Scrobble.AlbumName, vm.Scrobble.TrackName, time) { AlbumArtist = vm.Scrobble.AlbumArtist, Duration = vm.Scrobble.Duration});
          time = time.Subtract(TimeSpan.FromSeconds(1));
        }
      }

      return scrobbles;
    }

    /// <summary>
    /// Notifies the UI that ToScrobble has changed.
    /// </summary>
    /// <param name="sender">Ignored.</param>
    /// <param name="e">Ignored.</param>
    private void ToScrobbleChanged(object sender, EventArgs e)
    {
      NotifyOfPropertyChange(() => CanSelectAll);
      NotifyOfPropertyChange(() => CanSelectNone);
      NotifyOfPropertyChange(() => CanScrobble);
      NotifyOfPropertyChange(() => CanPreview);
    }
  }
}