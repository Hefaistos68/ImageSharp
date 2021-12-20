﻿// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Buffers;
using SixLabors.ImageSharp.Memory.Internals;

namespace SixLabors.ImageSharp.Memory
{
    internal unsafe struct MemoryGroupSpanCache
    {
        public SpanCacheMode Mode;
        public byte[] SingleArray;
        public void* SinglePointer;
        public void*[] MultiPointer;

        public static MemoryGroupSpanCache Create<T>(IMemoryOwner<T>[] memoryOwners)
            where T : struct
        {
            IMemoryOwner<T> owner0 = memoryOwners[0];
            MemoryGroupSpanCache memoryGroupSpanCache = default;
            if (memoryOwners.Length == 1)
            {
                if (owner0 is SharedArrayPoolBuffer<T> sharedPoolBuffer)
                {
                    memoryGroupSpanCache.Mode = SpanCacheMode.SingleArray;
                    memoryGroupSpanCache.SingleArray = sharedPoolBuffer.Array;
                }
                else if (owner0 is UnmanagedBuffer<T> unmanagedBuffer)
                {
                    memoryGroupSpanCache.Mode = SpanCacheMode.SinglePointer;
                    memoryGroupSpanCache.SinglePointer = unmanagedBuffer.Pointer;
                }
            }
            else
            {
                if (owner0 is UnmanagedBuffer<T>)
                {
                    memoryGroupSpanCache.Mode = SpanCacheMode.MultiPointer;
                    memoryGroupSpanCache.MultiPointer = new void*[memoryOwners.Length];
                    for (int i = 0; i < memoryOwners.Length; i++)
                    {
                        memoryGroupSpanCache.MultiPointer[i] = ((UnmanagedBuffer<T>)memoryOwners[i]).Pointer;
                    }
                }
            }

            return memoryGroupSpanCache;
        }
    }
}
