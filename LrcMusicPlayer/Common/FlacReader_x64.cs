﻿using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage;

namespace LrcMusicPlayer.Common
{
    class FlacReader_x64 : IDisposable
    {
        #region Api

        const string Dll = "LibFlac";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FLAC__stream_decoder_new();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern bool FLAC__stream_decoder_finish(IntPtr context);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern bool FLAC__stream_decoder_delete(IntPtr context);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern bool FLAC__stream_decoder_process_single(IntPtr context);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern bool FLAC__stream_decoder_process_until_end_of_stream(IntPtr context);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern long FLAC__stream_decoder_get_total_samples(IntPtr context);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int FLAC__stream_decoder_init_stream(IntPtr context,
            ReadCallback read,
            SeekCallback seek,
            TellCallback tell,
            LengthCallback length,
            EofCallback eof,
            WriteCallback write,
            MetadataCallback metadata,
            ErrorCallback error, IntPtr userData);

        // Callbacks
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ReadStatus ReadCallback(IntPtr context, IntPtr buffer, IntPtr size, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate Seek_Tell_LengthStatus SeekCallback(IntPtr context, IntPtr offset, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate Seek_Tell_LengthStatus TellCallback(IntPtr context, IntPtr offset, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate Seek_Tell_LengthStatus LengthCallback(IntPtr context, IntPtr length, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate bool EofCallback(IntPtr context, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate WriteStatus WriteCallback(IntPtr context, IntPtr frame, IntPtr buffer, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void ErrorCallback(IntPtr context, DecodeError status, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void MetadataCallback(IntPtr context, IntPtr metadata, IntPtr userData);

        private const int FlacMaxChannels = 8;

        struct FlacFrame
        {
            public FrameHeader Header;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = FlacMaxChannels)]
            public FlacSubFrame[] Subframes;
            public FrameFooter Footer;
        }

        struct FrameHeader
        {
            public int BlockSize;
            public int SampleRate;
            public int Channels;
            public int ChannelAssignment;
            public int BitsPerSample;
            public FrameNumberType NumberType;
            public long FrameOrSampleNumber;
            public byte Crc;
        }

        struct FlacSubFrame
        {
            public SubframeType Type;
            public IntPtr Data;
            public int WastedBits;
        }

        struct FrameFooter
        {
            public ushort Crc;
        }

        enum FrameNumberType
        {
            Frame,
            Sample
        }

        enum SubframeType
        {
            Constant,
            Verbatim,
            Fixed,
            LPC
        }

        enum DecodeError
        {
            LostSync,
            BadHeader,
            FrameCrcMismatch,
            UnparsableStream
        }

        enum WriteStatus
        {
            Continue,
            Abort
        }

        enum ReadStatus
        {
            Continue,
            EndOfStream,
            Abort
        }

        enum Seek_Tell_LengthStatus
        {
            Unsupported,
            Error,
            OK
        }
        #endregion

        #region Fields
        private IntPtr context;
        private Stream stream;
        private WavWriter writer;

        private int inputBitDepth;
        private int inputChannels;
        private int inputSampleRate;

        private int[] samples;
        private float[] samplesChannel;

        private long processedSamples = 0;
        private long totalSamples = -1;

        private ReadCallback read;
        private SeekCallback seek;
        private TellCallback tell;
        private LengthCallback length;
        private EofCallback eof;
        private WriteCallback write;
        private MetadataCallback metadata;
        private ErrorCallback error;
        #endregion

        #region Methods
        public FlacReader_x64(Stream input, WavWriter output) {
            if (output == null)
                throw new ArgumentNullException("WavWriter");

            writer = output;
            stream = input;
            context = FLAC__stream_decoder_new();

            if (context == IntPtr.Zero)
                throw new COMException("FLAC: Could not initialize stream decoder!");

            read = new ReadCallback(Read);
            seek = new SeekCallback(Seek);
            tell = new TellCallback(Tell);
            length = new LengthCallback(Length);
            eof = new EofCallback(Eof);
            write = new WriteCallback(Write);
            metadata = new MetadataCallback(Metadata);
            error = new ErrorCallback(Error);

            //var a = FLAC__stream_decoder_init_file(context, null, write, metadata, error, IntPtr.Zero);
            var a = FLAC__stream_decoder_init_stream(
                context, read, seek, tell, length, eof, write, metadata, error, IntPtr.Zero);
            if (a != 0)
                throw new COMException("FLAC: Could not open stream for reading!");
        }

        public void Dispose()
        {
            if (context != IntPtr.Zero)
            {
                Check(
                    FLAC__stream_decoder_finish(context),
                    "finalize stream decoder");

                Check(
                    FLAC__stream_decoder_delete(context),
                    "dispose of stream decoder instance");

                context = IntPtr.Zero;
            }
        }

        public void Close()
        {
            Dispose();
        }

        private void Check(bool result, string operation)
        {
            if (!result)
                throw new COMException(string.Format("FLAC: Could not {0}!", operation));
        }
        #endregion

        #region Callbacks
        private ReadStatus Read(IntPtr context, IntPtr buffer, IntPtr size, IntPtr userData){
            int rs = Marshal.ReadInt32(size);
            var data = new byte[rs];
            try {
                int bytes = stream.Read(data, 0, rs);
                Marshal.WriteInt32(size, bytes);
                Marshal.Copy(data, 0, buffer, data.Length);
                if (bytes==0) {
                    return ReadStatus.EndOfStream;
                }
            } catch (Exception) {
                return ReadStatus.Abort;
            }
            return ReadStatus.Continue;
        }

        private Seek_Tell_LengthStatus Seek(IntPtr context, IntPtr offset, IntPtr userData) {
            try {
                long pos = Marshal.ReadInt64(offset);
                stream.Seek(pos, SeekOrigin.Begin);
            } catch (Exception) {
                return Seek_Tell_LengthStatus.Error;
            }
            return Seek_Tell_LengthStatus.OK;
        }

        private Seek_Tell_LengthStatus Tell(IntPtr context, IntPtr offset, IntPtr userData) {
            try {
                long sp = stream.Position;
                Marshal.WriteInt64(offset, sp);
            } catch (Exception) {
                return Seek_Tell_LengthStatus.Error;
            }
            return Seek_Tell_LengthStatus.OK;
        }

        private Seek_Tell_LengthStatus Length(IntPtr context, IntPtr length, IntPtr userData) {
            try {
                long sl = stream.Length;
                Marshal.WriteInt64(length, sl);
            } catch (Exception) {
                return Seek_Tell_LengthStatus.Error;
            }
            return Seek_Tell_LengthStatus.OK;
        }

        private bool Eof(IntPtr context, IntPtr userData) {
            //var sr = new StreamReader(stream);
            //long position = stream.Position;
            //bool eof = sr.EndOfStream;
            //stream.Seek(position, SeekOrigin.Begin);
            return stream.Position >= stream.Length;
        }

        private WriteStatus Write(IntPtr context, IntPtr frame, IntPtr buffer, IntPtr userData)
        {
            FlacFrame f = Marshal.PtrToStructure<FlacFrame>(frame);

            int samplesPerChannel = f.Header.BlockSize;

            inputBitDepth = f.Header.BitsPerSample;
            inputChannels = f.Header.Channels;
            inputSampleRate = f.Header.SampleRate;

            if (!writer.HasHeader)
                writer.WriteHeader(inputSampleRate, inputBitDepth, inputChannels);

            if (totalSamples < 0)
                totalSamples = FLAC__stream_decoder_get_total_samples(context);

            if(samples == null) samples = new int[samplesPerChannel * inputChannels];
            if (samplesChannel == null) samplesChannel = new float[inputChannels];

            for (int i = 0; i < inputChannels; i++)
            {
                IntPtr pChannelBits = Marshal.ReadIntPtr(buffer, i * IntPtr.Size);

                Marshal.Copy(pChannelBits, samples, i * samplesPerChannel, samplesPerChannel);
            }

            // For each channel, there are BlockSize number of samples, so let's process these.
            for (int i = 0; i < samplesPerChannel; i++)
            {
                for (int c = 0; c < inputChannels; c++)
                {
                    int v = samples[i + c * samplesPerChannel];

                    switch (inputBitDepth / 8)
                    {
                        case sizeof(short): // 16-bit
                            writer.WriteInt16(v);
                            break;

                        case sizeof(int) - sizeof(byte): // 24-bit
                            writer.WriteInt24(v);
                            break;

                        default:
                             throw new NotSupportedException("Input FLAC bit depth is not supported!");
                    }
                }

                processedSamples += 1;
            }
            return WriteStatus.Continue;
        }

        private void Metadata(IntPtr context, IntPtr metadata, IntPtr userData)
        {
            // TODO
        }

        private void Error(IntPtr context, DecodeError status, IntPtr userData)
        {
            throw new COMException(string.Format("FLAC: Could not decode frame: {0}!", status));
        }

        public void Process()
        {
            //    while (reader.BaseStream.Position < reader.BaseStream.Length)
            //        Check(
            //            FLAC__stream_decoder_process_single(context),
            //            "process single");

            Check(
                FLAC__stream_decoder_process_until_end_of_stream(context),
                "process until eof");
            writer.WriteFooter();
        }
        #endregion
    }
}
