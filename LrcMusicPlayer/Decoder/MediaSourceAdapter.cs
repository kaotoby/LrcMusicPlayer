/*
	wp8libflac project
	© Alovchin, 2014
*/

using System;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace LrcMusicPlayer.Deocder
{
    public abstract class MediaSourceAdapter : IDisposable
    {
        private MediaStreamSource _mediaSource;

        public IMediaSource MediaSource
        {
            get { return this._mediaSource; }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void CreateMediaSource(AudioEncodingProperties encodingProperties)
        {
            this._mediaSource = new MediaStreamSource(new AudioStreamDescriptor(encodingProperties));
            this._mediaSource.Starting += this.OnMediaSourceStarting;
            this._mediaSource.SampleRequested += this.OnMediaSourceSampleRequested;
            this._mediaSource.Closed += this.OnMediaSourceClosed;
        }

        protected abstract void OnMediaSourceStarting(
            MediaStreamSource sender, MediaStreamSourceStartingEventArgs e);

        protected abstract void OnMediaSourceSampleRequested(
            MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e);

        protected abstract void OnMediaSourceClosed(
            MediaStreamSource sender, MediaStreamSourceClosedEventArgs e);

        /// <summary>
        /// Stream complete event.
        /// </summary>
        public event EventHandler StreamComplete;

        protected void RaiseStreamComplete()
        {
            EventHandler handler = this.StreamComplete;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected virtual void Dispose(bool isDisposing)
        {
        }
    }
}