// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;

namespace System.IO.Hashing
{
    /// <summary>
    ///   Provides an implementation of the CRC-32 algorithm, as used in
    ///   ITU-T V.42 and IEEE 802.3.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     For methods that return byte arrays or that write into spans of bytes, this implementation
    ///     emits the answer in the Little Endian byte order so that the CRC residue relationship
    ///     (CRC(message concat CRC(message))) is a fixed value) holds.
    ///     For CRC-32 this stable output is the byte sequence <c>{ 0x1C, 0xDF, 0x44, 0x21 }</c>,
    ///     the Little Endian representation of <c>0x2144DF1C</c>.
    ///   </para>
    ///   <para>
    ///     There are multiple, incompatible, definitions of a 32-bit cyclic redundancy
    ///     check (CRC) algorithm. When interoperating with another system, ensure that you
    ///     are using the same definition. The definition used by this implementation is not
    ///     compatible with the cyclic redundancy check described in ITU-T I.363.5.
    ///   </para>
    /// </remarks>
    public sealed partial class Crc32 : NonCryptographicHashAlgorithm
    {
        private const uint InitialState = 0xFFFF_FFFFu;
        private const int Size = sizeof(uint);

        private uint _crc = InitialState;

        /// <summary>
        ///   Initializes a new instance of the <see cref="Crc32"/> class.
        /// </summary>
        public Crc32()
            : base(Size)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="Crc32"/> class using the state from another instance.</summary>
        private Crc32(uint crc) : base(Size)
        {
            _crc = crc;
        }

        /// <summary>Returns a clone of the current instance, with a copy of the current instance's internal state.</summary>
        /// <returns>A new instance that will produce the same sequence of values as the current instance.</returns>
        public Crc32 Clone() => new(_crc);

        /// <summary>
        ///   Appends the contents of <paramref name="source"/> to the data already
        ///   processed for the current hash computation.
        /// </summary>
        /// <param name="source">The data to process.</param>
        public override void Append(ReadOnlySpan<byte> source)
        {
            _crc = Update(_crc, source);
        }

        /// <summary>
        ///   Resets the hash computation to the initial state.
        /// </summary>
        public override void Reset()
        {
            _crc = InitialState;
        }

        /// <summary>
        ///   Writes the computed hash value to <paramref name="destination"/>
        ///   without modifying accumulated state.
        /// </summary>
        /// <param name="destination">The buffer that receives the computed hash value.</param>
        protected override void GetCurrentHashCore(Span<byte> destination)
        {
            // The finalization step of the CRC is to perform the ones' complement.
            BinaryPrimitives.WriteUInt32LittleEndian(destination, ~_crc);
        }

        /// <summary>
        ///   Writes the computed hash value to <paramref name="destination"/>
        ///   then clears the accumulated state.
        /// </summary>
        protected override void GetHashAndResetCore(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, ~_crc);
            _crc = InitialState;
        }

        /// <summary>Gets the current computed hash value without modifying accumulated state.</summary>
        /// <returns>The hash value for the data already provided.</returns>
        [CLSCompliant(false)]
        public uint GetCurrentHashAsUInt32() => ~_crc;

        /// <summary>
        ///   Computes the CRC-32 hash of the provided data.
        /// </summary>
        /// <param name="source">The data to hash.</param>
        /// <returns>The CRC-32 hash of the provided data.</returns>
        /// <exception cref="ArgumentNullException">
        ///   <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        public static byte[] Hash(byte[] source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return Hash(new ReadOnlySpan<byte>(source));
        }

        /// <summary>
        ///   Computes the CRC-32 hash of the provided data.
        /// </summary>
        /// <param name="source">The data to hash.</param>
        /// <returns>The CRC-32 hash of the provided data.</returns>
        public static byte[] Hash(ReadOnlySpan<byte> source)
        {
            byte[] ret = new byte[Size];
            uint hash = HashToUInt32(source);
            BinaryPrimitives.WriteUInt32LittleEndian(ret, hash);
            return ret;
        }

        /// <summary>
        ///   Attempts to compute the CRC-32 hash of the provided data into the provided destination.
        /// </summary>
        /// <param name="source">The data to hash.</param>
        /// <param name="destination">The buffer that receives the computed hash value.</param>
        /// <param name="bytesWritten">
        ///   On success, receives the number of bytes written to <paramref name="destination"/>.
        /// </param>
        /// <returns>
        ///   <see langword="true"/> if <paramref name="destination"/> is long enough to receive
        ///   the computed hash value (4 bytes); otherwise, <see langword="false"/>.
        /// </returns>
        public static bool TryHash(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < Size)
            {
                bytesWritten = 0;
                return false;
            }

            uint hash = HashToUInt32(source);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, hash);
            bytesWritten = Size;
            return true;
        }

        /// <summary>
        ///   Computes the CRC-32 hash of the provided data into the provided destination.
        /// </summary>
        /// <param name="source">The data to hash.</param>
        /// <param name="destination">The buffer that receives the computed hash value.</param>
        /// <returns>
        ///   The number of bytes written to <paramref name="destination"/>.
        /// </returns>
        public static int Hash(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (destination.Length < Size)
            {
                ThrowDestinationTooShort();
            }

            uint hash = HashToUInt32(source);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, hash);
            return Size;
        }

        /// <summary>Computes the CRC-32 hash of the provided data.</summary>
        /// <param name="source">The data to hash.</param>
        /// <returns>The computed CRC-32 hash.</returns>
        [CLSCompliant(false)]
        public static uint HashToUInt32(ReadOnlySpan<byte> source) =>
            ~Update(InitialState, source);

        private static uint Update(uint crc, ReadOnlySpan<byte> source)
        {
#if NET
            if (CanBeVectorized(source))
            {
                return UpdateVectorized(crc, source);
            }
#endif

            return UpdateScalar(crc, source);
        }

        private static uint UpdateScalar(uint crc, ReadOnlySpan<byte> source)
        {
#if NET
            // Use ARM intrinsics for CRC if available. This is used for the trailing bytes on the vectorized path
            // and is the primary method if the vectorized path is unavailable.
            if (System.Runtime.Intrinsics.Arm.Crc32.Arm64.IsSupported)
            {
                return UpdateScalarArm64(crc, source);
            }

            if (System.Runtime.Intrinsics.Arm.Crc32.IsSupported)
            {
                return UpdateScalarArm32(crc, source);
            }
#endif

            ReadOnlySpan<uint> crcLookup = CrcLookup;
            for (int i = 0; i < source.Length; i++)
            {
                byte idx = (byte)crc;
                idx ^= source[i];
                crc = crcLookup[idx] ^ (crc >> 8);
            }

            return crc;
        }
    }
}
