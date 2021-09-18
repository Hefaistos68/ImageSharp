// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SixLabors.ImageSharp.Metadata.Profiles.Exif
{
    internal class ExifReader : BaseExifReader
    {
        public ExifReader(byte[] exifData)
            : base(new MemoryStream(exifData ?? throw new ArgumentNullException(nameof(exifData))))
        {
        }

        /// <summary>
        /// Reads and returns the collection of EXIF values.
        /// </summary>
        /// <returns>
        /// The <see cref="Collection{ExifValue}"/>.
        /// </returns>
        public List<IExifValue> ReadValues()
        {
            var values = new List<IExifValue>();

            // II == 0x4949
            this.IsBigEndian = this.ReadUInt16() != 0x4949;

            if (this.ReadUInt16() != 0x002A)
            {
                return values;
            }

            uint ifdOffset = this.ReadUInt32();
            this.ReadValues(values, ifdOffset);

            uint thumbnailOffset = this.ReadUInt32();
            this.GetThumbnail(thumbnailOffset);

            this.ReadSubIfd(values);

            this.ReadExtValues(values);

            return values;
        }

        private void GetThumbnail(uint offset)
        {
            if (offset == 0)
            {
                return;
            }

            var values = new List<IExifValue>();
            this.ReadValues(values, offset);

            foreach (ExifValue value in values)
            {
                if (value == ExifTag.JPEGInterchangeFormat)
                {
                    this.ThumbnailOffset = ((ExifLong)value).Value;
                }
                else if (value == ExifTag.JPEGInterchangeFormatLength)
                {
                    this.ThumbnailLength = ((ExifLong)value).Value;
                }
            }
        }
    }

    /// <summary>
    /// Reads and parses EXIF data from a stream.
    /// </summary>
    internal abstract class BaseExifReader
    {
        private readonly byte[] buf8 = new byte[8];
        private readonly byte[] buf4 = new byte[4];
        private readonly byte[] buf2 = new byte[2];

        private readonly Stream data;
        private List<ExifTag> invalidTags;

        private List<ulong> subIfds;

        protected BaseExifReader(Stream stream) =>
            this.data = stream ?? throw new ArgumentNullException(nameof(stream));

        private delegate TDataType ConverterMethod<TDataType>(ReadOnlySpan<byte> data);

        /// <summary>
        /// Gets the invalid tags.
        /// </summary>
        public IReadOnlyList<ExifTag> InvalidTags => this.invalidTags ?? (IReadOnlyList<ExifTag>)Array.Empty<ExifTag>();

        /// <summary>
        /// Gets or sets the thumbnail length in the byte stream.
        /// </summary>
        public uint ThumbnailLength { get; protected set; }

        /// <summary>
        /// Gets or sets the thumbnail offset position in the byte stream.
        /// </summary>
        public uint ThumbnailOffset { get; protected set; }

        public bool IsBigEndian { get; protected set; }

        public List<(ulong offset, ExifDataType dataType, ulong numberOfComponents, ExifValue exif)> ExtTags { get; } = new ();

        protected void ReadExtValues(List<IExifValue> values)
        {
            foreach ((ulong offset, ExifDataType dataType, ulong numberOfComponents, ExifValue exif) tag in this.ExtTags)
            {
                this.ReadExtValue(values, tag);
            }

            this.ExtTags.Clear();
        }

        /// <summary>
        /// Reads the values to the values collection.
        /// </summary>
        /// <param name="values">The values.</param>
        /// <param name="offset">The IFD offset.</param>
        protected void ReadValues(List<IExifValue> values, uint offset)
        {
            if (offset > this.data.Length)
            {
                return;
            }

            this.Seek(offset);
            int count = this.ReadUInt16();

            Span<byte> offsetBuffer = new byte[4];
            for (int i = 0; i < count; i++)
            {
                this.ReadValue(values, offsetBuffer);
            }
        }

        protected void ReadSubIfd(List<IExifValue> values)
        {
            if (this.subIfds is null)
            {
                return;
            }

            foreach (ulong subIfdOffset in this.subIfds)
            {
                this.ReadValues(values, (uint)subIfdOffset);
            }
        }

        protected void ReadValues64(List<IExifValue> values, ulong offset)
        {
            if (offset > (ulong)this.data.Length)
            {
                return;
            }

            this.Seek(offset);
            ulong count = this.ReadUInt64();

            Span<byte> offsetBuffer = new byte[8];
            for (ulong i = 0; i < count; i++)
            {
                this.ReadValue64(values, offsetBuffer);
            }
        }

        protected void ReadExtValue(IList<IExifValue> values, (ulong offset, ExifDataType dataType, ulong numberOfComponents, ExifValue exif) tag)
        {
            ulong size = tag.numberOfComponents * ExifDataTypes.GetSize(tag.dataType);
            byte[] dataBuffer = new byte[size];
            this.Seek(tag.offset);
            if (this.TryReadSpan(dataBuffer))
            {
                object value = this.ConvertValue(tag.dataType, dataBuffer, tag.numberOfComponents > 1 || tag.exif.IsArray);
                this.Add(values, tag.exif, value);
            }
        }

        protected void ReadSubIfd64(List<IExifValue> values)
        {
            if (this.subIfds is null)
            {
                return;
            }

            foreach (ulong subIfdOffset in this.subIfds)
            {
                this.ReadValues64(values, subIfdOffset);
            }
        }

        private static TDataType[] ToArray<TDataType>(ExifDataType dataType, ReadOnlySpan<byte> data, ConverterMethod<TDataType> converter)
        {
            int dataTypeSize = (int)ExifDataTypes.GetSize(dataType);
            int length = data.Length / dataTypeSize;

            var result = new TDataType[length];

            for (int i = 0; i < length; i++)
            {
                ReadOnlySpan<byte> buffer = data.Slice(i * dataTypeSize, dataTypeSize);

                result.SetValue(converter(buffer), i);
            }

            return result;
        }

        private byte ConvertToByte(ReadOnlySpan<byte> buffer) => buffer[0];

        private string ConvertToString(ReadOnlySpan<byte> buffer)
        {
            int nullCharIndex = buffer.IndexOf((byte)0);

            if (nullCharIndex > -1)
            {
                buffer = buffer.Slice(0, nullCharIndex);
            }

            return Encoding.UTF8.GetString(buffer);
        }

        private object ConvertValue(ExifDataType dataType, ReadOnlySpan<byte> buffer, bool isArray)
        {
            if (buffer.Length == 0)
            {
                return null;
            }

            switch (dataType)
            {
                case ExifDataType.Unknown:
                    return null;
                case ExifDataType.Ascii:
                    return this.ConvertToString(buffer);
                case ExifDataType.Byte:
                    if (!isArray)
                    {
                        return this.ConvertToByte(buffer);
                    }

                    return buffer.ToArray();
                case ExifDataType.DoubleFloat:
                    if (!isArray)
                    {
                        return this.ConvertToDouble(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToDouble);
                case ExifDataType.Long:
                case ExifDataType.Ifd:
                    if (!isArray)
                    {
                        return this.ConvertToUInt32(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToUInt32);
                case ExifDataType.Rational:
                    if (!isArray)
                    {
                        return this.ToRational(buffer);
                    }

                    return ToArray(dataType, buffer, this.ToRational);
                case ExifDataType.Short:
                    if (!isArray)
                    {
                        return this.ConvertToShort(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToShort);
                case ExifDataType.SignedByte:
                    if (!isArray)
                    {
                        return this.ConvertToSignedByte(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToSignedByte);
                case ExifDataType.SignedLong:
                    if (!isArray)
                    {
                        return this.ConvertToInt32(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToInt32);
                case ExifDataType.SignedRational:
                    if (!isArray)
                    {
                        return this.ToSignedRational(buffer);
                    }

                    return ToArray(dataType, buffer, this.ToSignedRational);
                case ExifDataType.SignedShort:
                    if (!isArray)
                    {
                        return this.ConvertToSignedShort(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToSignedShort);
                case ExifDataType.SingleFloat:
                    if (!isArray)
                    {
                        return this.ConvertToSingle(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToSingle);
                case ExifDataType.Long8:
                case ExifDataType.Ifd8:
                    if (!isArray)
                    {
                        return this.ConvertToUInt64(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToUInt64);
                case ExifDataType.SignedLong8:
                    if (!isArray)
                    {
                        return this.ConvertToInt64(buffer);
                    }

                    return ToArray(dataType, buffer, this.ConvertToUInt64);
                case ExifDataType.Undefined:
                    if (!isArray)
                    {
                        return this.ConvertToByte(buffer);
                    }

                    return buffer.ToArray();
                default:
                    throw new NotSupportedException($"Data type {dataType} is not supported.");
            }
        }

        private void ReadValue(List<IExifValue> values, Span<byte> offsetBuffer)
        {
            // 2   | 2    | 4     | 4
            // tag | type | count | value offset
            if ((this.data.Length - this.data.Position) < 12)
            {
                return;
            }

            var tag = (ExifTagValue)this.ReadUInt16();
            ExifDataType dataType = EnumUtils.Parse(this.ReadUInt16(), ExifDataType.Unknown);

            uint numberOfComponents = this.ReadUInt32();

            this.TryReadSpan(offsetBuffer);

            // Ensure that the data type is valid
            if (dataType == ExifDataType.Unknown)
            {
                return;
            }

            // Issue #132: ExifDataType == Undefined is treated like a byte array.
            // If numberOfComponents == 0 this value can only be handled as an inline value and must fallback to 4 (bytes)
            if (dataType == ExifDataType.Undefined && numberOfComponents == 0)
            {
                numberOfComponents = 4;
            }

            ExifValue exifValue = ExifValues.Create(tag) ?? ExifValues.Create(tag, dataType, numberOfComponents);

            if (exifValue is null)
            {
                this.AddInvalidTag(new UnkownExifTag(tag));
                return;
            }

            uint size = numberOfComponents * ExifDataTypes.GetSize(dataType);
            if (size > 4)
            {
                uint newOffset = this.ConvertToUInt32(offsetBuffer);

                // Ensure that the new index does not overrun the data.
                if (newOffset > int.MaxValue || (newOffset + size) > this.data.Length)
                {
                    this.AddInvalidTag(new UnkownExifTag(tag));
                    return;
                }

                this.ExtTags.Add((newOffset, dataType, numberOfComponents, exifValue));
            }
            else
            {
                object value = this.ConvertValue(dataType, offsetBuffer.Slice(0, (int)size), numberOfComponents > 1);
                this.Add(values, exifValue, value);
            }
        }

        private void ReadValue64(List<IExifValue> values, Span<byte> offsetBuffer)
        {
            if ((this.data.Length - this.data.Position) < 20)
            {
                return;
            }

            var tag = (ExifTagValue)this.ReadUInt16();
            ExifDataType dataType = EnumUtils.Parse(this.ReadUInt16(), ExifDataType.Unknown);

            ulong numberOfComponents = this.ReadUInt64();

            this.TryReadSpan(offsetBuffer);

            if (dataType == ExifDataType.Unknown)
            {
                return;
            }

            if (dataType == ExifDataType.Undefined && numberOfComponents == 0)
            {
                numberOfComponents = 8;
            }

            // The StripOffsets, StripByteCounts, TileOffsets, and TileByteCounts tags are allowed to have the datatype TIFF_LONG8 in BigTIFF.
            // Old datatypes TIFF_LONG, and TIFF_SHORT where allowed in the TIFF 6.0 specification, are still valid in BigTIFF, too.
            // Likewise, tags that point to other IFDs, like e.g. the SubIFDs tag, are now allowed to have the datatype TIFF_IFD8 in BigTIFF.
            // Again, the old datatypes TIFF_IFD, and the hardly recommendable TIFF_LONG, are still valid, too.
            // https://www.awaresystems.be/imaging/tiff/bigtiff.html
            ExifValue exifValue = null;
            switch (tag)
            {
                case ExifTagValue.StripOffsets:
                    exifValue = new ExifLong8Array(ExifTagValue.StripOffsets);
                    break;
                case ExifTagValue.StripByteCounts:
                    exifValue = new ExifLong8Array(ExifTagValue.StripByteCounts);
                    break;
                case ExifTagValue.TileOffsets:
                    exifValue = new ExifLong8Array(ExifTagValue.TileOffsets);
                    break;
                case ExifTagValue.TileByteCounts:
                    exifValue = new ExifLong8Array(ExifTagValue.TileByteCounts);
                    break;
                case ExifTagValue.SubIFDOffset:
                    exifValue = new ExifLong8(ExifTagValue.SubIFDOffset);
                    break;
                case ExifTagValue.GPSIFDOffset:
                    exifValue = new ExifLong8(ExifTagValue.GPSIFDOffset);
                    break;
                case ExifTagValue.SubIFDs:
                    exifValue = new ExifLong8Array(ExifTagValue.SubIFDs);
                    break;
                default:
                    exifValue = exifValue = ExifValues.Create(tag) ?? ExifValues.Create(tag, dataType, numberOfComponents);
                    break;
            }

            if (exifValue is null)
            {
                this.AddInvalidTag(new UnkownExifTag(tag));
                return;
            }

            ulong size = numberOfComponents * ExifDataTypes.GetSize(dataType);
            if (size > 8)
            {
                ulong newOffset = this.ConvertToUInt64(offsetBuffer);
                if (newOffset > ulong.MaxValue || newOffset > ((ulong)this.data.Length - size))
                {
                    this.AddInvalidTag(new UnkownExifTag(tag));
                    return;
                }

                this.ExtTags.Add((newOffset, dataType, numberOfComponents, exifValue));
            }
            else
            {
                object value = this.ConvertValue(dataType, offsetBuffer.Slice(0, (int)size), numberOfComponents > 1);
                this.Add(values, exifValue, value);
            }
        }

        private void Add(IList<IExifValue> values, IExifValue exif, object value)
        {
            if (!exif.TrySetValue(value))
            {
                return;
            }

            foreach (IExifValue val in values)
            {
                // Sometimes duplicates appear, can compare val.Tag == exif.Tag
                if (val == exif)
                {
                    Debug.WriteLine($"Duplicate Exif tag: tag={exif.Tag}, dataType={exif.DataType}");
                    return;
                }
            }

            if (exif.Tag == ExifTag.SubIFDOffset)
            {
                this.AddSubIfd(value);
            }
            else if (exif.Tag == ExifTag.GPSIFDOffset)
            {
                this.AddSubIfd(value);
            }
            else if (exif.Tag == ExifTag.SubIFDs)
            {
                ////foreach (object val in (Array)value)
                ////{
                ////    this.AddSubIfd(val);
                ////}
            }
            else
            {
                values.Add(exif);
            }
        }

        private void AddInvalidTag(ExifTag tag)
            => (this.invalidTags ??= new List<ExifTag>()).Add(tag);

        private void AddSubIfd(object val)
            => (this.subIfds ??= new List<ulong>()).Add(Convert.ToUInt64(val));

        private void Seek(ulong pos)
            => this.data.Seek((long)pos, SeekOrigin.Begin);

        private bool TryReadSpan(Span<byte> span)
        {
            int length = span.Length;
            if ((this.data.Length - this.data.Position) < length)
            {
                return false;
            }

            int read = this.data.Read(span);
            return read == length;
        }

        protected ulong ReadUInt64() =>
            this.TryReadSpan(this.buf8)
                ? this.ConvertToUInt64(this.buf8)
                : default;

        // Known as Long in Exif Specification.
        protected uint ReadUInt32() =>
            this.TryReadSpan(this.buf4)
                ? this.ConvertToUInt32(this.buf4)
                : default;

        protected ushort ReadUInt16() => this.TryReadSpan(this.buf2)
                ? this.ConvertToShort(this.buf2)
                : default;

        private long ConvertToInt64(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 8)
            {
                return default;
            }

            return this.IsBigEndian
                ? BinaryPrimitives.ReadInt64BigEndian(buffer)
                : BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }

        private ulong ConvertToUInt64(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 8)
            {
                return default;
            }

            return this.IsBigEndian
                ? BinaryPrimitives.ReadUInt64BigEndian(buffer)
                : BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        private double ConvertToDouble(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 8)
            {
                return default;
            }

            long intValue = this.IsBigEndian
                ? BinaryPrimitives.ReadInt64BigEndian(buffer)
                : BinaryPrimitives.ReadInt64LittleEndian(buffer);

            return Unsafe.As<long, double>(ref intValue);
        }

        private uint ConvertToUInt32(ReadOnlySpan<byte> buffer)
        {
            // Known as Long in Exif Specification.
            if (buffer.Length < 4)
            {
                return default;
            }

            return this.IsBigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(buffer)
                : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        private ushort ConvertToShort(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 2)
            {
                return default;
            }

            return this.IsBigEndian
                ? BinaryPrimitives.ReadUInt16BigEndian(buffer)
                : BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        }

        private float ConvertToSingle(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 4)
            {
                return default;
            }

            int intValue = this.IsBigEndian
                ? BinaryPrimitives.ReadInt32BigEndian(buffer)
                : BinaryPrimitives.ReadInt32LittleEndian(buffer);

            return Unsafe.As<int, float>(ref intValue);
        }

        private Rational ToRational(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 8)
            {
                return default;
            }

            uint numerator = this.ConvertToUInt32(buffer.Slice(0, 4));
            uint denominator = this.ConvertToUInt32(buffer.Slice(4, 4));

            return new Rational(numerator, denominator, false);
        }

        private sbyte ConvertToSignedByte(ReadOnlySpan<byte> buffer) => unchecked((sbyte)buffer[0]);

        private int ConvertToInt32(ReadOnlySpan<byte> buffer) // SignedLong in Exif Specification
        {
            if (buffer.Length < 4)
            {
                return default;
            }

            return this.IsBigEndian
                ? BinaryPrimitives.ReadInt32BigEndian(buffer)
                : BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        private SignedRational ToSignedRational(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 8)
            {
                return default;
            }

            int numerator = this.ConvertToInt32(buffer.Slice(0, 4));
            int denominator = this.ConvertToInt32(buffer.Slice(4, 4));

            return new SignedRational(numerator, denominator, false);
        }

        private short ConvertToSignedShort(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 2)
            {
                return default;
            }

            return this.IsBigEndian
                ? BinaryPrimitives.ReadInt16BigEndian(buffer)
                : BinaryPrimitives.ReadInt16LittleEndian(buffer);
        }
    }
}
