// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Copyright (c) 2025 Jeremy W Kuhne
// SPDX-License-Identifier: MIT
// See LICENSE file in the project root for full license information

using Touki.Io.Globbing;

namespace Touki.Io;

internal static class MSBuildFileNamePattern
{
    internal static StringSegment RewriteStarDotStarSequences(
        StringSegment expression,
        bool allowExtGlob = false) =>
        RewriteStarDotStarSequences(expression, allowExtGlob, out _);

    internal static StringSegment RewriteStarDotStarSequences(
        StringSegment expression,
        bool allowExtGlob,
        out int[]? sourcePositions)
    {
        ReadOnlySpan<char> source = expression;
        sourcePositions = null;
        if (!ContainsStarDotStar(source))
        {
            return expression;
        }

        ValueStringBuilder builder = new(stackalloc char[256]);
        try
        {
            List<int> positions = new(source.Length);
            bool changed = false;
            RewriteScope(
                source,
                start: 0,
                source.Length,
                allowExtGlob,
                ref builder,
                positions,
                ref changed);

            if (!changed)
            {
                return expression;
            }

            sourcePositions = positions.ToArray();
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static void RewriteScope(
        ReadOnlySpan<char> source,
        int start,
        int end,
        bool allowExtGlob,
        ref ValueStringBuilder builder,
        List<int> sourcePositions,
        ref bool changed)
    {
        bool rewriteDirectSequences = !ContainsDirectEffectiveDoubleStar(source, start, end, allowExtGlob);
        int fileNameStart = FindScopeFileNameStart(source, start, end, allowExtGlob);
        int index = start;
        while (index < end)
        {
            if (allowExtGlob && IsExtGlobOpener(source, index, end))
            {
                int close = FindExtGlobClose(source, index, end);
                if (close < 0)
                {
                    AppendRange(source, index, end, ref builder, sourcePositions);
                    return;
                }

                AppendCharacter(source[index], index, ref builder, sourcePositions);
                AppendCharacter(source[index + 1], index + 1, ref builder, sourcePositions);
                RewriteAlternatives(
                    source,
                    index + 2,
                    close,
                    allowExtGlob,
                    ref builder,
                    sourcePositions,
                    ref changed);
                AppendCharacter(source[close], close, ref builder, sourcePositions);
                index = close + 1;
                continue;
            }

            if (rewriteDirectSequences
                && index >= fileNameStart
                && ShouldRewriteStarDotStarAt(source, index, end, allowExtGlob))
            {
                AppendCharacter('*', index, ref builder, sourcePositions);
                changed = true;
                index += 3;
                continue;
            }

            AppendCharacter(source[index], index, ref builder, sourcePositions);
            index++;
        }
    }

    private static int FindScopeFileNameStart(
        ReadOnlySpan<char> source,
        int start,
        int end,
        bool allowExtGlob)
    {
        int fileNameStart = start;
        int index = start;
        while (index < end)
        {
            if (allowExtGlob && IsExtGlobOpener(source, index, end))
            {
                int close = FindExtGlobClose(source, index, end);
                if (close < 0)
                {
                    break;
                }

                index = close + 1;
                continue;
            }

            if (source[index] is '/' or '\\')
            {
                fileNameStart = index + 1;
            }

            index++;
        }

        return fileNameStart;
    }

    private static void RewriteAlternatives(
        ReadOnlySpan<char> source,
        int start,
        int end,
        bool allowExtGlob,
        ref ValueStringBuilder builder,
        List<int> sourcePositions,
        ref bool changed)
    {
        int alternativeStart = start;
        int index = start;
        while (index < end)
        {
            if (IsExtGlobOpener(source, index, end))
            {
                int close = FindExtGlobClose(source, index, end);
                if (close < 0)
                {
                    break;
                }

                index = close + 1;
                continue;
            }

            if (source[index] == '|')
            {
                RewriteScope(
                    source,
                    alternativeStart,
                    index,
                    allowExtGlob,
                    ref builder,
                    sourcePositions,
                    ref changed);
                AppendCharacter('|', index, ref builder, sourcePositions);
                alternativeStart = index + 1;
            }

            index++;
        }

        RewriteScope(
            source,
            alternativeStart,
            end,
            allowExtGlob,
            ref builder,
            sourcePositions,
            ref changed);
    }

    private static bool ContainsDirectEffectiveDoubleStar(
        ReadOnlySpan<char> source,
        int start,
        int end,
        bool allowExtGlob)
    {
        int index = start;
        while (index < end)
        {
            if (allowExtGlob && IsExtGlobOpener(source, index, end))
            {
                int close = FindExtGlobClose(source, index, end);
                if (close < 0)
                {
                    return false;
                }

                index = close + 1;
                continue;
            }

            if (source[index] != '*')
            {
                index++;
                continue;
            }

            int runStart = index;
            while (index < end && source[index] == '*')
            {
                index++;
            }

            int effectiveRunLength = index - runStart;
            bool opensExtGlob = allowExtGlob
                && effectiveRunLength >= 2
                && index < end
                && source[index] == '(';
            if (opensExtGlob)
            {
                effectiveRunLength--;
            }

            if (effectiveRunLength >= 2)
            {
                return true;
            }

            if (opensExtGlob)
            {
                int close = FindExtGlobClose(source, index - 1, end);
                if (close < 0)
                {
                    return false;
                }

                index = close + 1;
            }
        }

        return false;
    }

    internal static bool ContainsEffectiveDoubleStar(
        ReadOnlySpan<char> expression,
        bool allowExtGlob) =>
        ContainsDirectEffectiveDoubleStar(
            expression,
            start: 0,
            expression.Length,
            allowExtGlob);

    private static bool ShouldRewriteStarDotStarAt(
        ReadOnlySpan<char> expression,
        int index,
        int end,
        bool allowExtGlob) =>
        index + 2 < end
        && expression[index] == '*'
        && expression[index + 1] == '.'
        && expression[index + 2] == '*'
        && (!allowExtGlob
            || index + 3 >= end
            || expression[index + 3] != '(');

    private static bool IsExtGlobOpener(ReadOnlySpan<char> source, int index, int end) =>
        index + 1 < end
        && source[index + 1] == '('
        && source[index] is '?' or '*' or '+' or '@' or '!';

    private static int FindExtGlobClose(ReadOnlySpan<char> source, int opener, int end)
    {
        int depth = 1;
        int index = opener + 2;
        while (index < end)
        {
            if (IsExtGlobOpener(source, index, end))
            {
                depth++;
                index += 2;
                continue;
            }

            if (source[index] == ')' && --depth == 0)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static void AppendRange(
        ReadOnlySpan<char> source,
        int start,
        int end,
        ref ValueStringBuilder builder,
        List<int> sourcePositions)
    {
        for (int index = start; index < end; index++)
        {
            AppendCharacter(source[index], index, ref builder, sourcePositions);
        }
    }

    private static void AppendCharacter(
        char character,
        int sourcePosition,
        ref ValueStringBuilder builder,
        List<int> sourcePositions)
    {
        builder.Append(character);
        sourcePositions.Add(sourcePosition);
    }

    private static bool ContainsStarDotStar(ReadOnlySpan<char> expression) =>
        expression.IndexOf("*.*".AsSpan(), StringComparison.Ordinal) >= 0;
}
