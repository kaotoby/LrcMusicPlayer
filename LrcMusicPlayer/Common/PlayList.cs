using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading;
using Windows.Media;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Graphics.Imaging;
using TagLib;

namespace LrcMusicPlayer.Common
{
    enum PlayStyle
    {
        RepeatOnce,
        RepeatAll,
        Shuffle,
        RepeatSong,
        SingleSong
    }

    public class PlayList
    {
        private ObservableCollection<PlayListItem> _items = new ObservableCollection<PlayListItem>();
        public ObservableCollection<PlayListItem> Items { get { return _items; } }
        private StorageFile _playlistFile = null;
        public IEnumerable<string> FileTokens {
            get {
                return _items.Select(c => c.FileToken)
                    .Union(_items.Select(c => c.LrcToken).Where(c => c != ""));
            }
        }

        public async Task Save() {
            if (_playlistFile != null) await Save(_playlistFile);
            else {
                StorageFolder folder = ApplicationData.Current.RoamingFolder;
                StorageFile file = await folder.CreateFileAsync("playlist.lmp", CreationCollisionOption.ReplaceExisting);
                await Save(file);
            }
        }

        public async Task Save(StorageFile file) {
            await FileIO.WriteLinesAsync(file,
                _items.Select(c=>c.FileToken + "," + c.LrcToken)
                , Windows.Storage.Streams.UnicodeEncoding.Utf8);
        }

        public static async Task LoadStorageFiles(IEnumerable<StorageFile> selectedFiles, PlayList playList) {
            var _playList = new List<PlayListItem>();
            var LrcFiles = selectedFiles.Where(c => c.FileType == ".lrc");
            selectedFiles = selectedFiles.Where(c => c.FileType != ".lrc");

            foreach (var music in selectedFiles) {
                PlayListItem item= new PlayListItem(music, "", "", "");
                if (playList.FileTokens.Contains(item.FileToken)) continue;
                await item.CopyMetadataFromFile(music);

                _playList.Add(item);
            }
            foreach (var item in _playList) playList.Items.Add(item);

            foreach (var file in LrcFiles) {
                string token = StorageApplicationPermissions.FutureAccessList.Add(file);
                try {
                    playList.Items.First(c => c.DisplayName == file.DisplayName).LrcToken = token;
                } catch (Exception) { continue; }
            }

            await playList.Save();
        }

        public async Task SaveFavorite() {
            StorageFolder folder = ApplicationData.Current.RoamingFolder;
            StorageFile file = (await folder.TryGetItemAsync("favorite.lmp")) as StorageFile;
            if (file == null) file = await folder.CreateFileAsync("favorite.lmp");
            var favoriteList = await FileIO.ReadLinesAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            foreach (var music in _items.Where(c=>c.IsFavoriteChanged)) {
                if (music.IsFavorite) {
                    favoriteList.Add(music.FileToken + "," + music.LrcToken);
                } else {
                    favoriteList.Remove(favoriteList.First(c => c.Contains(music.FileToken)));                    
                }
            }
            await FileIO.WriteLinesAsync(file, favoriteList, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        }

        public static async Task<PlayList> LoadFromFile() {
            try {
                StorageFolder folder = ApplicationData.Current.RoamingFolder;
                StorageFile file = await folder.GetFileAsync("playlist.lmp");
                var playlist = await LoadFromFile(file);
                return playlist;
            } catch (Exception) {
                return new PlayList();
            }
        }

        public static async Task<PlayList> LoadFromFile(StorageFile file) {
            await PlayListItem.SetNocover();
            PlayList list = new PlayList();
            List<bool> favorite = new List<bool>();
            list._playlistFile = file;

            StorageFolder folder = ApplicationData.Current.RoamingFolder;
            StorageFile ffile = (await folder.TryGetItemAsync("favorite.lmp")) as StorageFile;
            if (file == null) file = await folder.CreateFileAsync("favorite.lmp");
            var favoriteList = await FileIO.ReadLinesAsync(ffile, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            IList<string> tokens = await FileIO.ReadLinesAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            if (tokens.Count > 0) {
                foreach (var line in tokens) {
                    var token = line.Split(',');
                    var music = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(token[0]);
                    PlayListItem item = new PlayListItem(music, "", "", "");
                    item.LrcToken = token[1];
                    if (favoriteList.Any(c => c.Contains(item.FileToken))) {
                        item.IsFavorite = true;
                        var clearData = item.IsFavoriteChanged;
                    }
                    await item.CopyMetadataFromFile(music);

                    list._items.Add(item);
                }
            }
            return list;
        }

        public void CopyTo(PlayList oldList) {
            oldList._playlistFile = _playlistFile;
            oldList._items.Clear();
            foreach (var item in _items) oldList._items.Add(item);
        }
    }

    public class PlayListItem
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }

        public string FileToken { get { return _fileToken; } }
        private string _fileToken;

        public string DisplayName { get { return _displayName; } }
        private string _displayName;

        public bool IsFavorite { get { return _isFavorite; } 
            set {
                if (value != _isFavorite) {
                    _isFavorite = value;
                    _isFavoriteChanged = !_isFavoriteChanged;
                }
            } 
        }
        private bool _isFavorite = false;
        public bool IsFavoriteChanged { get { return getIsFavoriteChanged(); } }
        private bool _isFavoriteChanged = false;

        public string LrcToken { get { return _lrcToken; } set { _lrcToken = value; } }
        private string _lrcToken = "";

        static private IRandomAccessStream _nocover;
        static public IRandomAccessStream Nocover { get { return _nocover; } }

        public IRandomAccessStream ThumbnailStream { get; set; }
        public BitmapImage Thumbnail {
            get {
                if (ThumbnailStream == null) ThumbnailStream = _nocover;
                ThumbnailStream.Seek(0);
                BitmapImage _thumbnail = new BitmapImage();
                _thumbnail.SetSource(ThumbnailStream);
                return _thumbnail;
            }
        }

        private bool getIsFavoriteChanged() {
            bool _return = _isFavoriteChanged;
            _isFavoriteChanged = false;
            return _return;
        }

        public async Task<BitmapImage> GetOriginalPicture() {
            BitmapImage picture = new BitmapImage();
            var file = await this.GetFile();
            var fileStream = await file.OpenStreamForReadAsync();
            var tagFile = TagLib.File.Create(new StreamFileAbstraction(file.Name, fileStream, fileStream));
            var tag = tagFile.Tag;
            if (tag.Pictures.Count() == 0) return null;
            var picureStream = new MemoryStream(tag.Pictures[0].Data.Data);
            var bmpStream = new InMemoryRandomAccessStream();
            try {
                var decoder = await BitmapDecoder.CreateAsync(picureStream.AsRandomAccessStream());
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, bmpStream);
                var pixelData = (await decoder.GetPixelDataAsync()).DetachPixelData();
                encoder.SetPixelData(decoder.BitmapPixelFormat, BitmapAlphaMode.Straight,
                    decoder.OrientedPixelWidth, decoder.OrientedPixelHeight,
                    decoder.DpiX, decoder.DpiY, pixelData);
                await encoder.FlushAsync();
                await picture.SetSourceAsync(bmpStream.CloneStream());
            } catch (Exception) { } finally {
                picureStream.Dispose();
                bmpStream.Dispose();
            }
            return picture;
        }

        public static async Task SetNocover() {
            Uri fileUri = new Uri("ms-appx:///Assets/nocover.mp3");
            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(fileUri);
            var fileStream = await file.OpenStreamForReadAsync();
            var tagFile = TagLib.File.Create(new StreamFileAbstraction(file.Name, fileStream, fileStream));
            var tag = tagFile.Tag;
            var picureStream = new MemoryStream(tag.Pictures[0].Data.Data);
            var bmpStream = new InMemoryRandomAccessStream();
            try {
                var decoder = await BitmapDecoder.CreateAsync(picureStream.AsRandomAccessStream());
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, bmpStream);
                var pixelData = (await decoder.GetPixelDataAsync()).DetachPixelData();
                encoder.SetPixelData(decoder.BitmapPixelFormat, BitmapAlphaMode.Straight,
                    decoder.OrientedPixelWidth, decoder.OrientedPixelHeight,
                    decoder.DpiX, decoder.DpiY, pixelData);
                await encoder.FlushAsync();
                _nocover = bmpStream.CloneStream();
            } catch (Exception){ }
            finally {
                picureStream.Dispose();
                bmpStream.Dispose();
            }
        }

        public PlayListItem(StorageFile file, string formate) {
            Title = Artist = Album = "";
            _displayName = file.DisplayName;
            _fileToken = StorageApplicationPermissions.FutureAccessList.Add(file);
            Regex seq = new Regex("{(\\d)}");
            List<int> sequence = new List<int>();
            foreach (Match item in seq.Matches(formate)) {
                sequence.Add(int.Parse(item.Groups[1].Value));
            }
            Regex reg = new Regex("^" + seq.Replace(formate, "(.+)") + "$");
            var match = reg.Match(_displayName).Groups;
            for (int i = 1; i < match.Count; i++) {
                switch (sequence[i - 1]) {
                    case 0:
                        Title = match[i].Value;
                        break;
                    case 1:
                        Artist = match[i].Value;
                        break;
                    case 2:
                        Album = match[i].Value;
                        break;
                }
            }
            if (Title == "") {
                Title = file.DisplayName;
                Artist = "";
            }
        }

        public PlayListItem(StorageFile file, string title, string artist, string album) {
            Title = title; Artist = artist; Album = album;
            _displayName = file.DisplayName;
            _fileToken = StorageApplicationPermissions.FutureAccessList.Add(file);
        }

        public async Task CopyMetadataFromFile(StorageFile file) {
            ThumbnailStream = _nocover;
            var fileStream = await file.OpenStreamForReadAsync();
            var tagFile = TagLib.File.Create(new StreamFileAbstraction(file.Name, fileStream, fileStream));
            var tag = tagFile.Tag;
            Title = tag.Title;
            Artist = tag.Performers.Count() > 0 ? tag.Performers[0] : "";
            Album = tag.Album;

            if (tag.Pictures.Count() > 0) {
                var picureStream = new MemoryStream(tag.Pictures[0].Data.Data);
                var bmpStream = new InMemoryRandomAccessStream();
                try {
                    var decoder = await BitmapDecoder.CreateAsync(picureStream.AsRandomAccessStream());
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, bmpStream);
                    double height = decoder.OrientedPixelHeight, width = decoder.OrientedPixelWidth;
                    double hwRatio = height / width;
                    if (height < width) {
                        height = 150;
                        width = height / hwRatio;
                    } else {
                        width = 150;
                        height = width * hwRatio;
                    }
                    BitmapTransform transform = new BitmapTransform();
                    transform.ScaledWidth = (uint)width;
                    transform.ScaledHeight = (uint)height;
                    var pixelData = await decoder.GetPixelDataAsync(decoder.BitmapPixelFormat,
                        BitmapAlphaMode.Straight, transform, ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                    encoder.SetPixelData(decoder.BitmapPixelFormat, BitmapAlphaMode.Straight,
                        (uint)width, (uint)height, decoder.DpiX, decoder.DpiY, pixelData.DetachPixelData());
                    await encoder.FlushAsync();
                    ThumbnailStream = bmpStream.CloneStream();
                } catch (Exception){ 
                } finally {
                    picureStream.Dispose();
                    bmpStream.Dispose();
                }
            }

            if (Title == "") {
                Title = file.DisplayName;
                Artist = "";
            }
        }

        public async Task<StorageFile> GetFile() {
            return await StorageApplicationPermissions.FutureAccessList.GetFileAsync(_fileToken);
        }

        public static async Task<Dictionary<TimeSpan, string>> GetLyrics(string fileToken) {
            var dic = new Dictionary<string, string>();
            var file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(fileToken);
            string lrc = await FileIO.ReadTextAsync(file);
            Regex reg = new Regex(@"^\s*(?:\[\d\d:\d\d\.\d\d\])+(.*)?$", RegexOptions.Multiline);
            Regex regTime = new Regex(@"\[(\d\d:\d\d\.\d\d)\]");
            foreach (Match match in reg.Matches(lrc.Replace("\r", ""))) {
                foreach (Match item in regTime.Matches(match.Groups[0].Value)) {
                    dic[item.Groups[1].Value] = match.Groups[1].Value;
                }
            }
            return dic.OrderBy(c => c.Key)
                .ToDictionary(c => new TimeSpan(0, 0,
                int.Parse(c.Key.Substring(0, 2)),
                int.Parse(c.Key.Substring(3, 2)),
                int.Parse(c.Key.Substring(6, 2)) * 10),
                c => c.Value);
        }
    }

    public static class Extensions
    {
        public static Task ForEachAsync<TSource, TResult>(
            this IEnumerable<TSource> source,
            Func<TSource, Task<TResult>> taskSelector, Action<TSource, TResult> resultProcessor) {
            var oneAtATime = new SemaphoreSlim(5, 10);
            return Task.WhenAll(
                from item in source
                select ProcessAsync(item, taskSelector, resultProcessor, oneAtATime));
        }

        private static async Task ProcessAsync<TSource, TResult>(
            TSource item,
            Func<TSource, Task<TResult>> taskSelector, Action<TSource, TResult> resultProcessor,
            SemaphoreSlim oneAtATime) {
            TResult result = await taskSelector(item);
            await oneAtATime.WaitAsync();
            try {
                resultProcessor(item, result);
            } finally {
                oneAtATime.Release();
            }
        }
    }
}
