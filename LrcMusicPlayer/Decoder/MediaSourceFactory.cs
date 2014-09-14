/*
	wp8libflac project
	© Alovchin, 2014
*/

using System;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;

namespace LrcMusicPlayer.Deocder
{
    public sealed class MediaSourceFactory
    {
        /// <summary>
        /// Creates a <see cref="IMediaSource" /> corresponding to file format.
        /// </summary>
        /// <param name="filePath">File path.</param>
        public static async Task<MediaSourceAdapter> CreateAsync(string filePath)
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var mediaSourceAdapter = new FlacMediaSourceAdapter(storageFile);
            await mediaSourceAdapter.InitializeAsync();
            return mediaSourceAdapter;
        }

        public static async Task<MediaSourceAdapter> CreateAsync(StorageFile storageFile) {
            var mediaSourceAdapter = new FlacMediaSourceAdapter(storageFile);
            await mediaSourceAdapter.InitializeAsync();
            return mediaSourceAdapter;
        }
    }
}
