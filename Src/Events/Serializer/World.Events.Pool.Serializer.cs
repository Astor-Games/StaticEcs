using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MemoryPack;
using MemoryPackWriter = MemoryPack.MemoryPackWriter<System.Buffers.ArrayBufferWriter<byte>>;
using static System.Runtime.CompilerServices.MethodImplOptions;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace FFS.Libraries.StaticEcs {
    public delegate T EcsEventMigrationReader<T, WorldType>(ref MemoryPackReader reader, byte version)
        where T : struct
        where WorldType : struct, IWorldType;

    public delegate void EcsEventDeleteMigrationReader<WorldType>(ref MemoryPackReader reader, byte version)
        where WorldType : struct, IWorldType;

    #if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    #endif
    public abstract partial class World<WorldType> {
        #if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        #endif
        public abstract partial class Events {
            #if ENABLE_IL2CPP
            [Il2CppSetOption(Option.NullChecks, false)]
            [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
            #endif
            internal partial struct Pool<T> where T : struct, IEvent {
                #if ENABLE_IL2CPP
                [Il2CppSetOption(Option.NullChecks, false)]
                [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
                [Il2CppEagerStaticClassConstruction]
                #endif
                public struct Serializer {
                    internal static Serializer Value;

                    private IPackArrayStrategy<T> _readWriteArrayStrategy;
                    private EcsEventMigrationReader<T, WorldType> _migrationReader;
                    internal Guid guid;
                    internal byte version;

                    [MethodImpl(AggressiveInlining)]
                    public void Create(IEventConfig<T, WorldType> config) {
                        if (!MemoryPackFormatterProvider.IsRegistered<EventReceiver<WorldType, T>>()) {
                            MemoryPackFormatterProvider.RegisterWithCollections<EventReceiver<WorldType, T>, UnmanagedPackArrayStrategy<EventReceiver<WorldType, T>>>(
                                (ref MemoryPackWriter writer, in EventReceiver<WorldType, T> value) => writer.WriteVarInt(value._id),
                                (ref MemoryPackReader reader) => new EventReceiver<WorldType, T>(reader.ReadVarIntInt32())
                            );
                        }
                        
                        guid = config.Id();
                        version = config.Version();
                        if (guid != Guid.Empty) {
                            _readWriteArrayStrategy = config.ReadWriteStrategy();
                            _migrationReader = config.MigrationReader();
                            MemoryPackFormatterProvider.RegisterWithCollections(config.Writer(), config.Reader(), _readWriteArrayStrategy);
                        }
                    }

                    [MethodImpl(AggressiveInlining)]
                    public void Destroy() {
                        guid = default;
                        version = default;
                        _migrationReader = default;
                        _readWriteArrayStrategy = default;
                    }

                    [MethodImpl(AggressiveInlining)]
                    internal void WriteAll(ref MemoryPackWriter writer, ref Pool<T> pool) {
                        var notEmpty = pool.receiversCount > pool.deletedReceiversCount;
                        writer.WriteVarInt(version);
                        writer.WriteVarInt(pool.sequence);
                        writer.WriteBool(notEmpty);
                        writer.WriteVarInt((ushort) pool.receivers.Length);
                        writer.WriteUnmanagedArray(pool.receivers);
                        
                        if (notEmpty) {
                            var minSeq = pool.sequence;
                            var maxSeq = pool.sequence;
                            for (var i = 0; i < pool.receiversCount; i++) {
                                ref var receiver = ref pool.receivers[i];
                                if (!receiver.Deleted && receiver.Sequence < minSeq) {
                                    minSeq = receiver.Sequence;
                                }
                            }
                            var curPageIdx = (uint) ((minSeq >> EVENT_PAGE_SHIFT) & PAGES_OFFSET_MASK);
                            var maxPageIdx = (uint) ((maxSeq >> EVENT_PAGE_SHIFT) & PAGES_OFFSET_MASK);
                            var maxInPageIdx = (uint) (maxSeq & EVENT_PAGE_OFFSET_MASK);

                            var isUnmanaged = _readWriteArrayStrategy.IsUnmanaged();
                            writer.WriteBool(isUnmanaged);

                            using (var count = writer.TrackCountShort())
                            {
                                while (curPageIdx <= maxPageIdx) {
                                    if (curPageIdx == maxPageIdx && maxInPageIdx == 0) {
                                        break;
                                    }
                                
                                    ref var page = ref pool.pages[curPageIdx];
                                    writer.WriteVarInt(curPageIdx);
                                    writer.WriteVarInt(page.Version);
                                    writer.WriteUnmanagedArray(page.Mask);
                                    writer.WriteUnmanagedArray(page.UnreadReceiversCount);
                                    if (isUnmanaged) {
                                        _readWriteArrayStrategy.WriteArray(ref writer, page.Data);
                                    } else {
                                        for (var eIdx = 0; eIdx < EVENTS_PER_PAGE; eIdx++) {
                                            if ((page.Mask[eIdx >> EVENT_IN_PAGE_MASK_SHIFT] & (1Ul << (eIdx & EVENT_IN_PAGE_OFFSET_MASK))) != 0) {
                                                writer.WriteValue(in page.Data[eIdx]);
                                            }
                                        }
                                    }
                                    count.Increment();
                                    curPageIdx++;
                                }
                            }
                        }
                    }

                    [MethodImpl(AggressiveInlining)]
                    internal void ReadAll(ref MemoryPackReader reader, ref Pool<T> pool) {
                        var oldVersion = reader.ReadVarIntByte();
                        pool.sequence = reader.ReadVarIntUInt64();
                        var notEmpty = reader.ReadBool();
                        var len = reader.ReadVarIntUInt16();
                        if (len > pool.receivers.Length) {
                            Array.Resize(ref pool.receivers, len);
                        }
                        reader.ReadUnmanagedArray(ref pool.receivers);

                        if (notEmpty) {
                            var isUnmanaged = reader.ReadBool();
                            var count = reader.ReadVarIntUInt16();
                            for (var i = 0; i < count; i++) {
                                var pageIdx = reader.ReadVarIntUInt32();
                                ref var page = ref pool.pages[pageIdx];
                                page.Version = reader.ReadVarIntUInt16();
                                
                                if (pool.freePagesCount > 0) {
                                    page.FromFree(ref pool.freePages[--pool.freePagesCount]);
                                } else {
                                    page.InitNew();
                                    pool.maxPagesCount++;
                                }
                                reader.ReadUnmanagedArray(ref page.Mask);
                                reader.ReadUnmanagedArray(ref page.UnreadReceiversCount);
                                if (version == oldVersion) {
                                    if (isUnmanaged) {
                                        _readWriteArrayStrategy.ReadArray(ref reader, ref page.Data);
                                    } else {
                                        for (var eIdx = 0; eIdx < EVENTS_PER_PAGE; eIdx++) {
                                            if ((page.Mask[eIdx >> EVENT_IN_PAGE_MASK_SHIFT] & (1Ul << (eIdx & EVENT_IN_PAGE_OFFSET_MASK))) != 0) {
                                                page.Data[eIdx] = reader.ReadValue<T>();
                                            }
                                        }
                                    }
                                } else {
                                    uint oneSize = default;
                                    if (isUnmanaged) {
                                        _ = reader.ReadNullFlag();
                                        var size = reader.ReadVarIntInt32();
                                        var byteSize = reader.ReadVarIntUInt32();
                                        oneSize = (uint) (byteSize / size);
                                    }
                                    for (var eIdx = 0; eIdx < EVENTS_PER_PAGE; eIdx++) {
                                        if ((page.Mask[eIdx >> EVENT_IN_PAGE_MASK_SHIFT] & (1Ul << (eIdx & EVENT_IN_PAGE_OFFSET_MASK))) != 0) {
                                            page.Data[eIdx] = _migrationReader(ref reader, oldVersion);
                                        } else if (isUnmanaged) {
                                            reader.Advance(oneSize);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    internal static class EventSerializerUtils {
        [MethodImpl(AggressiveInlining)]
        [SuppressMessage("ReSharper", "UnusedVariable")]
        internal static void DeleteAllEventMigration<WorldType>(this ref MemoryPackReader reader, EcsEventDeleteMigrationReader<WorldType> migration)
            where WorldType : struct, IWorldType {
            var oldVersion = reader.ReadVarIntByte();
            var sequence = reader.ReadVarIntUInt64();
            var notEmpty = reader.ReadBool();
            var len = reader.ReadVarIntUInt16();

            reader.ReadUnmanagedArrayPooled<ReceiverData>(out var handle);
            handle.Return();
            
            if (notEmpty) {
                var isUnmanaged = reader.ReadBool();
                var count = reader.ReadVarIntUInt16();
                for (var i = 0; i < count; i++) {
                    var pageIdx = reader.ReadVarIntUInt32();
                    var Version = reader.ReadVarIntUInt16();

                    var mask = reader.ReadUnmanagedArrayPooled<ulong>(out var maskHandle).Array!;
                    reader.ReadUnmanagedArrayPooled<ushort>(out var unreadReceiversCountHandle);
                    unreadReceiversCountHandle.Return();
                    uint oneSize = default;
                    if (isUnmanaged) {
                        _ = reader.ReadNullFlag();
                        var size = reader.ReadVarIntInt32();
                        var byteSize = reader.ReadVarIntUInt32();
                        oneSize = (uint) (byteSize / size);
                    }

                    for (var eIdx = 0; eIdx < World<WorldType>.Events.EVENTS_PER_PAGE; eIdx++) {
                        if ((mask[eIdx >> World<WorldType>.Events.EVENT_IN_PAGE_MASK_SHIFT] & (1Ul << (eIdx & World<WorldType>.Events.EVENT_IN_PAGE_OFFSET_MASK))) != 0) {
                            migration(ref reader, oldVersion);
                        } else if (isUnmanaged) {
                            reader.Advance(oneSize);
                        }
                    }
                    maskHandle.Return();
                }
            }
        }
    }
}