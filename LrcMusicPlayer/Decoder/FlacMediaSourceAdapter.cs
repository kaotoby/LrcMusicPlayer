/*
	wp8libflac project
	© Alovchin, 2014
*/

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using LrcMusicDecoder;

namespace LrcMusicPlayer.Deocder
{
    internal sealed class FlacMediaSourceAdapter : MediaSourceAdapter
    {
        private readonly StorageFile _storageFile;

        private double _currentTime;
        private long _startPosition;

        private IRandomAccessStream _fileStream;
        private FlacWaveStream _flacWaveStream;
        
        public FlacMediaSourceAdapter(StorageFile storageFile)
        {
            this._storageFile = storageFile;
        }

        public async Task InitializeAsync()
        {
            this._fileStream = await this._storageFile.OpenAsync(FileAccessMode.Read);
            this._flacWaveStream = new FlacWaveStream(this._fileStream);

            FlacStreamInfo streamInfo = this._flacWaveStream.GetStreamInfo();

            AudioEncodingProperties encodingProperties = AudioEncodingProperties.CreatePcm(
                streamInfo.SampleRate, streamInfo.ChannelCount, streamInfo.BitsPerSample);

            CreateMediaSource(encodingProperties);
        }

        protected override void OnMediaSourceStarting(
            MediaStreamSource sender, MediaStreamSourceStartingEventArgs e)
        {
            MediaStreamSourceStartingRequestDeferral deferral = e.Request.GetDeferral();

            FlacStreamInfo streamInfo = this._flacWaveStream.GetStreamInfo();
            sender.Duration = TimeSpan.FromSeconds(streamInfo.Duration);
            sender.CanSeek = true;

            this._startPosition = this._flacWaveStream.Position;
            this._currentTime = 0;

            double startTime = this._flacWaveStream.GetDurationFromBufferSize((int) this._startPosition);
            e.Request.SetActualStartPosition(TimeSpan.FromSeconds(startTime));

            deferral.Complete();
        }

        protected override void OnMediaSourceSampleRequested(
            MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e)
        {
            MediaStreamSourceSampleRequestDeferral deferral = e.Request.GetDeferral();

            var buffer = new byte[4096];
            int read = this._flacWaveStream.Read(buffer, 0, buffer.Length);

            MediaStreamSample sample;
            if (read > 0)
            {
                sample = MediaStreamSample.CreateFromBuffer(
                    buffer.AsBuffer(), TimeSpan.FromSeconds(this._currentTime));
                sample.Processed += this.OnSampleProcessed;

                double sampleDuration = this._flacWaveStream.GetDurationFromBufferSize(buffer.Length);
                sample.Duration = TimeSpan.FromSeconds(sampleDuration);

                this._currentTime += sampleDuration;
            }
            else
            {
                sample = null;

                this._flacWaveStream.Seek(this._startPosition, SeekOrigin.Begin);
                this._currentTime = 0;
            }

            e.Request.Sample = sample;
            deferral.Complete();
        }

        private void OnSampleProcessed(MediaStreamSample sender, object args)
        {
            sender.Processed -= this.OnSampleProcessed;
            sender.Buffer.AsStream().Dispose();
        }

        protected override void OnMediaSourceClosed(
            MediaStreamSource sender, MediaStreamSourceClosedEventArgs e)
        {
            this._currentTime = 0;
            this._flacWaveStream.Close();
            RaiseStreamComplete();
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                if (this._flacWaveStream != null)
                    this._flacWaveStream.Dispose();

                if (this._fileStream != null)
                    this._fileStream.Dispose();
            }
        }
    }
}