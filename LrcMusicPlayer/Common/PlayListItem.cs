using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using TagLib;

namespace LrcMusicPlayer.Common
{
    public class PlayListItem
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }

        public string IsCurrentCheckMark = "123434";

        public string FileToken { get { return _fileToken; } }
        private string _fileToken;

        public string DisplayName { get { return _displayName; } }
        private string _displayName;

        public bool IsFavorite {
            get { return _isFavorite; }
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
            } catch (Exception) { } finally {
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
                } catch (Exception) {
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
}
