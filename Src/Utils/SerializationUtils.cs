using System;
using System.Runtime.CompilerServices;
using FFS.Libraries.StaticPack;
using MemoryPack;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace FFS.Libraries.StaticEcs
{
    using MemoryPackWriter = MemoryPackWriter<System.Buffers.ArrayBufferWriter<byte>>;

    public delegate void WriteCollectionDelegate(ref BinaryPackWriter writer, int idx);
    
    public static class SerializationUtils
    {
        [MethodImpl(AggressiveInlining)]
        public static void WriteBool(this MemoryPackWriter writer, bool value) 
        {
            writer.WriteVarInt((byte) (value ? 1 : 0));
        }
        
        [MethodImpl(AggressiveInlining)]
        public static bool ReadBool(this MemoryPackReader reader) 
        {
            return reader.ReadVarIntByte() != 0;
        }
        
        [MethodImpl(AggressiveInlining)]
        public static void WriteGuid(this MemoryPackWriter writer, Guid value) 
        {
            writer.DangerousWriteUnmanaged(value);
        }
        
        [MethodImpl(AggressiveInlining)]
        public static Guid ReadGuid(this MemoryPackReader reader) 
        {
            return reader.ReadUnmanaged<Guid>();
        }
        
        [MethodImpl(AggressiveInlining)]
        public static ref byte Reserve<T>(this MemoryPackWriter writer) 
        {
            var size = Unsafe.SizeOf<T>();
            ref var point = ref writer.GetSpanReference(size);
            writer.Advance(size);
            return ref point;
        }
        
        public static TrackWrittenScope TrackWritten(this MemoryPackWriter writer)
        {
            return new TrackWrittenScope(writer);
        }
        
        public static TrackCountScope TrackCount(this MemoryPackWriter writer)
        {
            return new TrackCountScope(writer);
        }
        
        public static TrackCountShortScope TrackCountShort(this MemoryPackWriter writer)
        {
            return new TrackCountShortScope(writer);
        }

        public static MemoryPackReader AsReader(this MemoryPackWriter writer)
        {
            throw new NotImplementedException();
        }
    }

    public static class MemoryWriterPool
    {
        public static MemoryPackWriter Rent(uint sizeHint)
        {
            throw new NotImplementedException();
        }

        public static void Return(MemoryPackWriter writer)
        {
            throw new NotImplementedException();
        }
    }
    
    public readonly ref struct TrackWrittenScope : IDisposable
    {
        private readonly MemoryPackWriter _writer;
        private readonly ref byte _pointer;
        private readonly int _written;

        public TrackWrittenScope(MemoryPackWriter writer)
        {
            _writer = writer;
            _pointer = ref writer.Reserve<int>();
            _written = writer.WrittenCount;
        }

        public void Dispose()
        {
            Unsafe.WriteUnaligned(ref _pointer, _writer.WrittenCount - _written);
        }
    }
    
    public ref struct TrackCountScope : IDisposable
    {
        private readonly MemoryPackWriter _writer;
        private readonly ref byte _pointer;
        private int _count = 0;

        public TrackCountScope(MemoryPackWriter writer)
        {
            _writer = writer;
            _pointer = ref writer.Reserve<int>();
        }

        public void Increment()
        {
            _count++;
        }

        public void Dispose()
        {
            Unsafe.WriteUnaligned(ref _pointer, _count);
        }
    }
    
    public ref struct TrackCountShortScope : IDisposable
    {
        private readonly MemoryPackWriter _writer;
        private readonly ref byte _pointer;
        private ushort _count = 0;

        public TrackCountShortScope(MemoryPackWriter writer)
        {
            _writer = writer;
            _pointer = ref writer.Reserve<ushort>();
        }

        public void Increment()
        {
            _count++;
        }

        public void Dispose()
        {
            Unsafe.WriteUnaligned(ref _pointer, _count);
        }
    }
}