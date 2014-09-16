using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace LrcMusicPlayer.Common
{
    public class PlayList
    {
        private ObservableCollection<PlayListItem> _items = new ObservableCollection<PlayListItem>();
        public ObservableCollection<PlayListItem> Items { get { return _items; } }

        public int CurrentIndex { get { return _items.IndexOf(_currentItem); } }
        public PlayListItem CurrentItem { get { return _currentItem; } }
        private PlayListItem _currentItem;

        private Queue<int> shuffleQueue = new Queue<int>();
        private List<int> shufflePrevList = new List<int>();

        private StorageFile _playlistFile = null;
        private IEnumerable<string> FileTokens {
            get {
                return _items.Select(c => c.FileToken)
                    .Concat(_items.Select(c => c.LrcToken).Where(c => c != ""))
                    .Distinct();
            }
        }

        public PlayListItem GoToItem(int itemIndex) {
            if (itemIndex < _items.Count && itemIndex >= 0) {
                _currentItem = _items[itemIndex];
                return _currentItem;
            } else {
                return null;
            }
        }

        public PlayListItem NextItem(PlayStyle style) {
            if (_currentItem == null) {
                if (style == PlayStyle.Shuffle) {
                    if (shuffleQueue.Count == 0) ShfflePlayList();
                    _currentItem = _items[shuffleQueue.Dequeue()];
                } else {
                    _currentItem = _items[0];
                }
            } else {
                int currentIndex = CurrentIndex;
                switch (style) {
                    case PlayStyle.RepeatOnce:
                        shuffleQueue.Clear();
                        if (currentIndex + 1 < _items.Count) _currentItem = _items[currentIndex + 1];
                        else _currentItem = _items[0];
                        break;
                    case PlayStyle.RepeatAll:
                        
                        if (currentIndex + 1 < _items.Count) _currentItem = _items[currentIndex + 1];
                        else _currentItem = null;
                        break;
                    case PlayStyle.Shuffle:
                        if (shuffleQueue.Count == 0) ShfflePlayList();
                        _currentItem = _currentItem = _items[shuffleQueue.Dequeue()];
                        break;
                    case PlayStyle.RepeatSong:
                        shuffleQueue.Clear();
                        break;
                    case PlayStyle.SingleSong:
                        shuffleQueue.Clear();
                        _currentItem = null;
                        break;
                }
                if (style != PlayStyle.Shuffle) {
                    shuffleQueue.Clear();

                }
            }
            return _currentItem;
        }

        private void ShfflePlayList() {
            var shuffleIndexs = Enumerable.Range(0, _items.Count).ToArray();
            Random r = new Random();
            for (int i = shuffleIndexs.Length - 1; i > 0; i--) {
                int j = (int)Math.Floor(r.NextDouble() * (i + 1));
                int temp = shuffleIndexs[i];
                shuffleIndexs[i] = shuffleIndexs[j];
                shuffleIndexs[j] = temp;
            }
            foreach (var item in shuffleIndexs) {
                shuffleQueue.Enqueue(item);
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

        public enum PlayStyle
        {
            RepeatOnce,
            RepeatAll,
            Shuffle,
            RepeatSong,
            SingleSong
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
