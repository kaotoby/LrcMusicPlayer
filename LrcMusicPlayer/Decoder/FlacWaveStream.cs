/*
	wp8libflac project
	© Alovchin, 2014
*/

/*
    This code heavily references FlacBox project's WaveOverFlacStream:
    https://flacbox.codeplex.com/
*/

using System;
using System.Collections.Generic;
using System.IO;
using Windows.Storage.Streams;
using LrcMusicDecoder;

namespace LrcMusicPlayer.Deocder
{
    /// <summary>
    /// Wraps FLAC stream to WAVE data stream.
    /// </summary>
    internal class FlacWaveStream : Stream
    {
        private static readonly ArraySegment<byte> _noCurrentData = new ArraySegment<byte>(new byte[0]);

        private readonly FlacDecoder _flacDecoder;

        private IEnumerator<ArraySegment<byte>> _streamIterator;
        private ArraySegment<byte> _currentData;
        private bool _isIteratorFinished;
        
        private bool _headerRead;
        private FlacStreamInfo _streamInfo;

        public FlacWaveStream(IRandomAccessStream fileStream)
        {
            this._flacDecoder = new FlacDecoder();
            this._flacDecoder.Initialize(fileStream);

            this._streamIterator = this.IterateOverStream();
            this._isIteratorFinished = false;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get
            {
                try
                {
                    this.EnsureHeaderRead();
                    return this._streamInfo.StreamLength;
                }
                catch (EndOfStreamException)
                {
                    return 0;
                }
            }
        }

        public override long Position
        {
            get { return this._flacDecoder.Position; }
            set { this._flacDecoder.Seek(value); }
        }

        /// <summary>
        /// Gets FLAC stream info.
        /// </summary>
        /// <returns>FLAC stream info.</returns>
        /// <exception cref="EndOfStreamException">This stream contains no data.</exception>
        public FlacStreamInfo GetStreamInfo()
        {
            this.EnsureHeaderRead();
            return this._streamInfo;
        }

        /// <summary>
        /// Gets the duration for the specified buffer size.
        /// </summary>
        /// <param name="bufferSize">Buffer size.</param>
        /// <returns>Duration.</returns>
        /// <exception cref="EndOfStreamException">This stream contains no data.</exception>
        public double GetDurationFromBufferSize(int bufferSize)
        {
            FlacStreamInfo streamInfo = this.GetStreamInfo();

            if (streamInfo.BytesPerSecond == 0)
                return 0;

            return (double) bufferSize/streamInfo.BytesPerSecond;
        }

        /// <summary>
        /// Gets the buffer size for the specified duration.
        /// </summary>
        /// <param name="duration">Duration.</param>
        /// <returns>Buffer size.</returns>
        /// <exception cref="EndOfStreamException">This stream contains no data.</exception>
        public int GetBufferSizeFromDuration(long duration)
        {
            FlacStreamInfo streamInfo = this.GetStreamInfo();
            return (int) (duration*streamInfo.BytesPerSecond/10000000);
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException();

            if (offset < 0 || count < 0)
                throw new ArgumentOutOfRangeException();

            if (offset + count > buffer.Length)
                throw new ArgumentException();

            this.EnsureHeaderRead();

            if (this._currentData.Count >= count)
            {
                Array.Copy(this._currentData.Array, this._currentData.Offset, buffer, offset, count);
                this._currentData = new ArraySegment<byte>(
                    this._currentData.Array, this._currentData.Offset + count, this._currentData.Count - count);
                return count;
            }

            int read = this._currentData.Count;
            Array.Copy(this._currentData.Array, this._currentData.Offset, buffer, offset, this._currentData.Count);
            this._currentData = _noCurrentData;

            while (this._streamIterator.MoveNext())
            {
                int rest = count - read;
                if (this._streamIterator.Current.Count >= rest)
                {
                    Array.Copy(this._streamIterator.Current.Array, 0, buffer, offset + read, rest);
                    read += rest;
                    this._currentData = new ArraySegment<byte>(this._streamIterator.Current.Array, rest,
                        this._streamIterator.Current.Count - rest);
                    break;
                }
                Array.Copy(this._streamIterator.Current.Array, 0, buffer, offset + read,
                    this._streamIterator.Current.Count);
                read += this._streamIterator.Current.Count;
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin != SeekOrigin.Begin)
            {
                throw new NotSupportedException();
            }

            if (this._isIteratorFinished)
            {
                this._streamIterator.Dispose();
                this._streamIterator = this.IterateOverStream();
            }
            this._streamIterator.MoveNext();

            return this.Position = offset;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

#if NETFX_CORE
        public void Close()
#else
        public override void Close()
#endif
        {
            this._flacDecoder.Close();
        }

        private IEnumerator<ArraySegment<byte>> IterateOverStream()
        {
            this._streamInfo = this._flacDecoder.GetStreamInfo();
            yield return new ArraySegment<byte>(new byte[0]);

            while (true)
            {
                FlacSample sample = this._flacDecoder.GetSample();

                if (sample != null)
                {
                    yield return new ArraySegment<byte>(sample.Buffer);
                }
                else
                {
                    this._isIteratorFinished = true;
                    break;
                }
            }
        }

        private void EnsureHeaderRead()
        {
            if (!this._headerRead)
            {
                if (!this._streamIterator.MoveNext())
                    throw new EndOfStreamException("This stream contains no data.");

                this._currentData = this._streamIterator.Current;
                this._headerRead = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this._streamIterator.Dispose();
                this._flacDecoder.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
