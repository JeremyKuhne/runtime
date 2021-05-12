// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Buffers
{
    public ref partial struct SpanReader<T> where T : unmanaged, IEquatable<T>
    {
        /// <summary>
        /// Create a <see cref="SpanReader{T}"/> over the given <see cref="ReadOnlySpan{T}"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SpanReader(ReadOnlySpan<T> span)
        {
            Span = span;
            Consumed = 0;
        }

        /// <summary>
        /// The underlying <see cref="ReadOnlySpan{T}"/> for the reader.
        /// </summary>
        public readonly ReadOnlySpan<T> Span { get; }

        /// <summary>
        /// True when there is no more data in the <see cref="Span"/>.
        /// </summary>
        public readonly bool End => Consumed == Length;

        /// <summary>
        /// Gets the unread portion of the <see cref="Span"/>.
        /// </summary>
        /// <value>
        /// The unread portion of the <see cref="Span"/>.
        /// </value>
        public readonly ReadOnlySpan<T> UnreadSpan => Span[Consumed..];

        /// <summary>
        /// The total number of <typeparamref name="T"/>'s processed by the reader.
        /// </summary>
        public int Consumed { readonly get; private set; }

        /// <summary>
        /// Remaining <typeparamref name="T"/>'s in the reader's <see cref="Span"/>.
        /// </summary>
        public readonly int Remaining => Length - Consumed;

        /// <summary>
        /// Count of <typeparamref name="T"/> in the reader's <see cref="Span"/>.
        /// </summary>
        public readonly int Length => Span.Length;

        /// <summary>
        /// Peeks at the next value without advancing the reader.
        /// </summary>
        /// <param name="value">The next value or default if at the end.</param>
        /// <returns>False if at the end of the reader.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryPeek(out T value)
        {
            if (!End)
            {
                value = Span[Consumed];
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Peeks at the next value at specific offset without advancing the reader.
        /// </summary>
        /// <param name="offset">The offset from current position.</param>
        /// <param name="value">The next value, or the default value if at the end of the reader.</param>
        /// <returns><c>true</c> if the reader is not at its end and the peek operation succeeded; <c>false</c> if at the end of the reader.</returns>
        public readonly bool TryPeek(int offset, out T value)
        {
            if (offset < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException_OffsetOutOfRange();

            // If we've got data and offset is not out of bounds
            if (End || Remaining <= offset)
            {
                value = default;
                return false;
            }

            value = Span[Consumed + offset];
            return true;
        }

        /// <summary>
        /// Read the next value and advance the reader.
        /// </summary>
        /// <param name="value">The next value or default if at the end.</param>
        /// <returns>False if at the end of the reader.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRead(out T value)
        {
            if (End)
            {
                value = default;
                return false;
            }

            value = Span[Consumed];
            Consumed++;
            return true;
        }

        /// <summary>
        /// Move the reader back the specified number of items.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if trying to rewind a negative amount or more than <see cref="Consumed"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rewind(int count)
        {
            if ((uint)count > (uint)Consumed)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count);
            }

            Consumed -= count;
        }

        /// <summary>
        /// Move the reader ahead the specified number of items.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            if (Remaining > count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count);
            }

            Consumed += count;
        }

        /// <summary>
        /// Copies data from the current position to the given <paramref name="destination"/> span if there
        /// is enough data to fill it.
        /// </summary>
        /// <remarks>
        /// This API is used to copy a fixed amount of data out of the sequence if possible. It does not advance
        /// the reader. To look ahead for a specific stream of data <see cref="IsNext(ReadOnlySpan{T}, bool)"/> can be used.
        /// </remarks>
        /// <param name="destination">Destination span to copy to.</param>
        /// <returns>True if there is enough data to completely fill the <paramref name="destination"/> span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryCopyTo(Span<T> destination)
        {
            // This API doesn't advance to facilitate conditional advancement based on the data returned.
            // We don't provide an advance option to allow easier utilizing of stack allocated destination spans.
            // (Because we can make this method readonly we can guarantee that we won't capture the span.)

            ReadOnlySpan<T> firstSpan = UnreadSpan;
            if (firstSpan.Length >= destination.Length)
            {
                firstSpan.Slice(0, destination.Length).CopyTo(destination);
                return true;
            }

            return false;
        }
    }
}
