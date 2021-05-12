// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Buffers
{
    public ref partial struct SpanReader<T> where T : unmanaged, IEquatable<T>
    {
        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySpan<T> span, T delimiter, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);

            if (index != -1)
            {
                span = index == 0 ? default : remaining.Slice(0, index);
                Advance(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            span = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>, ignoring delimiters that are
        /// preceded by <paramref name="delimiterEscape"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="delimiterEscape">If found prior to <paramref name="delimiter"/> it will skip that occurrence.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySpan<T> span, T delimiter, T delimiterEscape, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);

            if ((index > 0 && !remaining[index - 1].Equals(delimiterEscape)) || index == 0)
            {
                span = remaining.Slice(0, index);
                Advance(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            span = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiters"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiters">The delimiters to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the first found instance of any of the given <paramref name="delimiters"/>.</param>
        /// <returns>True if any of the <paramref name="delimiters"/> were found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadToAny(out ReadOnlySpan<T> span, ReadOnlySpan<T> delimiters, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = delimiters.Length == 2
                ? remaining.IndexOfAny(delimiters[0], delimiters[1])
                : remaining.IndexOfAny(delimiters);

            if (index != -1)
            {
                span = remaining.Slice(0, index);
                Advance(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            span = default;
            return false;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="span">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadTo(out ReadOnlySpan<T> span, ReadOnlySpan<T> delimiter, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);

            if (index >= 0)
            {
                span = remaining.Slice(0, index);
                Advance(index + (advancePastDelimiter ? delimiter.Length : 0));
                return true;
            }

            span = default;
            return false;
        }

        /// <summary>
        /// Advance until the given <paramref name="delimiter"/>, if found.
        /// </summary>
        /// <param name="delimiter">The delimiter to search for.</param>
        /// <param name="advancePastDelimiter">True to move past the <paramref name="delimiter"/> if found.</param>
        /// <returns>True if the given <paramref name="delimiter"/> was found.</returns>
        public bool TryAdvanceTo(T delimiter, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOf(delimiter);
            if (index != -1)
            {
                Advance(advancePastDelimiter ? index + 1 : index);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Advance until any of the given <paramref name="delimiters"/>, if found.
        /// </summary>
        /// <param name="delimiters">The delimiters to search for.</param>
        /// <param name="advancePastDelimiter">True to move past the first found instance of any of the given <paramref name="delimiters"/>.</param>
        /// <returns>True if any of the given <paramref name="delimiters"/> were found.</returns>
        public bool TryAdvanceToAny(ReadOnlySpan<T> delimiters, bool advancePastDelimiter = true)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            int index = remaining.IndexOfAny(delimiters);
            if (index != -1)
            {
                Advance(index + (advancePastDelimiter ? 1 : 0));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Advance past consecutive instances of the given <paramref name="value"/>.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public int AdvancePast(T value)
        {
            int start = Consumed;

            ReadOnlySpan<T> remaining = UnreadSpan;

            int i = 0;
            for (i = 0; i < remaining.Length && remaining[i].Equals(value); i++)
            {
            }

            Advance(i);
            return Consumed - start;
        }

        /// <summary>
        /// Skip consecutive instances of any of the given <paramref name="values"/>.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public int AdvancePastAny(ReadOnlySpan<T> values)
        {
            int start = Consumed;

            ReadOnlySpan<T> remaining = UnreadSpan;

            int i = 0;
            for (i = 0; i < remaining.Length && values.IndexOf(remaining[i]) != -1; i++)
            {
            }

            Advance(i);
            return Consumed - start;
        }

        /// <summary>
        /// Advance past consecutive instances of any of the given values.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public int AdvancePastAny(T value0, T value1, T value2, T value3)
        {
            int start = Consumed;

            ReadOnlySpan<T> remaining = UnreadSpan;

            int i = 0;
            for (i = 0; i < remaining.Length; i++)
            {
                T value = remaining[i];
                if (!value.Equals(value0) && !value.Equals(value1) && !value.Equals(value2) && !value.Equals(value3))
                {
                    break;
                }
            }

            Advance(i);
            return Consumed - start;
        }

        /// <summary>
        /// Advance past consecutive instances of any of the given values.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public int AdvancePastAny(T value0, T value1, T value2)
        {
            int start = Consumed;

            ReadOnlySpan<T> remaining = UnreadSpan;

            int i = 0;
            for (i = 0; i < remaining.Length; i++)
            {
                T value = remaining[i];
                if (!value.Equals(value0) && !value.Equals(value1) && !value.Equals(value2))
                {
                    break;
                }
            }

            Advance(i);
            return Consumed - start;
        }

        /// <summary>
        /// Advance past consecutive instances of any of the given values.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public int AdvancePastAny(T value0, T value1)
        {
            int start = Consumed;

            ReadOnlySpan<T> remaining = UnreadSpan;

            int i = 0;
            for (i = 0; i < remaining.Length; i++)
            {
                T value = remaining[i];
                if (!value.Equals(value0) && !value.Equals(value1))
                {
                    break;
                }
            }

            Advance(i);
            return Consumed - start;
        }

        /// <summary>
        /// Moves the reader to the end of the span.
        /// </summary>
        public void AdvanceToEnd()
        {
            Consumed = Length;
        }

        /// <summary>
        /// Check to see if the given <paramref name="next"/> value is next.
        /// </summary>
        /// <param name="next">The value to compare the next items to.</param>
        /// <param name="advancePast">Move past the <paramref name="next"/> value if found.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNext(T next, bool advancePast = false)
        {
            if (End)
            {
                return false;
            }

            if (Span[Consumed].Equals(next))
            {
                if (advancePast)
                {
                    Advance(1);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Check to see if the given <paramref name="next"/> values are next.
        /// </summary>
        /// <param name="next">The span to compare the next items to.</param>
        /// <param name="advancePast">Move past the <paramref name="next"/> values if found.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNext(ReadOnlySpan<T> next, bool advancePast = false)
        {
            ReadOnlySpan<T> remaining = UnreadSpan;
            if (remaining.StartsWith(next))
            {
                if (advancePast)
                {
                    Advance(next.Length);
                }
                return true;
            }

            return false;
        }
    }
}
