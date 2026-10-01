// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
#if USE_TOUKI_GLOBBING
using Touki.Io;
using Touki.Io.Globbing;
#else
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
#endif
using Microsoft.Extensions.Primitives;

namespace Microsoft.Extensions.FileProviders.Physical
{
    /// <summary>
    /// A polling based <see cref="IChangeToken"/> for wildcard patterns.
    /// </summary>
    public class PollingWildCardChangeToken : IPollingChangeToken
    {
        private readonly object _enumerationLock = new();
#if USE_TOUKI_GLOBBING
        private static readonly EnumerationOptions s_enumerationOptions = new()
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.PlatformDefault,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = true
        };

        private readonly string _root;
        private readonly string _enumerationRoot;
        private readonly string _relativePathPrefix;
        private readonly GlobSpecification _matcher;
        private readonly bool _enumerateFiles;
#else
        private readonly DirectoryInfoBase _directoryInfo;
        private readonly Matcher _matcher;
#endif
        private bool _changed;
        private DateTime _lastScanTimeUtc;
#if !NET
        private byte[]? _byteBuffer;
#endif
        private byte[]? _previousHash;
        private CancellationTokenSource? _tokenSource;
        private CancellationChangeToken? _changeToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="PollingWildCardChangeToken"/> class.
        /// </summary>
        /// <param name="root">The root of the file system.</param>
        /// <param name="pattern">The pattern to watch.</param>
        public PollingWildCardChangeToken(
            string root,
            string pattern)
            : this(root, pattern, Physical.Clock.Instance)
        {
        }

        // Internal for unit testing.
        internal PollingWildCardChangeToken(
            string root,
            string pattern,
            IClock clock)
        {
            Clock = clock;

#if USE_TOUKI_GLOBBING
            _root = Path.GetFullPath(root);
            GlobSpecification matcher = PhysicalFilesWatcher.CreateWildcardMatcher(pattern);
            string enumerationPattern = RebaseLeadingParentSegments(
                _root,
                pattern,
                out _enumerationRoot,
                out _relativePathPrefix);
            _enumerateFiles = enumerationPattern.Length > 0;
            _matcher = enumerationPattern == pattern || !_enumerateFiles
                ? matcher
                : PhysicalFilesWatcher.CreateWildcardMatcher(enumerationPattern);
#else
            _directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(root));
            _matcher = PhysicalFilesWatcher.CreateWildcardMatcher(pattern);
#endif
            CalculateChanges();
        }

        /// <inheritdoc />
        public bool ActiveChangeCallbacks { get; internal set; }

        // Internal for unit testing.
        internal TimeSpan PollingInterval { get; set; } = PhysicalFilesWatcher.DefaultPollingInterval;

        [DisallowNull]
        internal CancellationTokenSource? CancellationTokenSource
        {
            get => _tokenSource;
            set
            {
                Debug.Assert(_tokenSource == null, "We expect CancellationTokenSource to be initialized exactly once.");

                _tokenSource = value;
                _changeToken = new CancellationChangeToken(_tokenSource.Token);
            }
        }

        CancellationTokenSource? IPollingChangeToken.CancellationTokenSource => CancellationTokenSource;

        private IClock Clock { get; }

        /// <inheritdoc />
        public bool HasChanged
        {
            get
            {
                if (_changed)
                {
                    return true;
                }

                if (ShouldRefresh())
                {
                    lock (_enumerationLock)
                    {
                        if (!_changed && ShouldRefresh())
                        {
                            _changed = CalculateChanges();
                        }
                    }
                }

                return _changed;

                bool ShouldRefresh() => Clock.UtcNow - _lastScanTimeUtc >= PollingInterval;
            }
        }

        private bool CalculateChanges()
        {
            using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (string file in GetFilePaths())
                {
                    DateTime lastWriteTimeUtc = GetLastWriteUtc(file);
                    if (_lastScanTimeUtc.Ticks != 0 && _lastScanTimeUtc < lastWriteTimeUtc)
                    {
                        // _lastScanTimeUtc is the greatest timestamp that any last writes could have been.
                        // If a file has a newer timestamp than this value, it must've changed.
                        return true;
                    }

                    ComputeHash(sha256, file, lastWriteTimeUtc);
                }

#if NET
                Span<byte> currentHash = stackalloc byte[256 / 8];
                sha256.GetHashAndReset(currentHash);
                if (_previousHash is null)
                {
                    _previousHash = currentHash.ToArray(); // First run
                }
                else if (!_previousHash.AsSpan().SequenceEqual(currentHash))
                {
                    return true;
                }
#else
                byte[] currentHash = sha256.GetHashAndReset();
                if (_previousHash is null)
                {
                    _previousHash = currentHash; // First run
                }
                else if (!_previousHash.AsSpan().SequenceEqual(currentHash.AsSpan()))
                {
                    return true;
                }
#endif

                _lastScanTimeUtc = Clock.UtcNow;
            }

            return false;
        }

#if USE_TOUKI_GLOBBING
        private List<string> GetFilePaths()
        {
            List<string> files = [];
            if (!_enumerateFiles || !Directory.Exists(_enumerationRoot))
            {
                return files;
            }

            using (FileSystemPathEnumerator enumerator = FileSystemPathEnumerator.Create(
                _enumerationRoot,
                _matcher.CreateFileSystemMatcher(),
                s_enumerationOptions))
            {
                while (enumerator.MoveNext())
                {
                    files.Add(_relativePathPrefix + enumerator.Current);
                }
            }

            files.Sort(StringComparer.Ordinal);
            return files;
#else
        private IEnumerable<string> GetFilePaths()
        {
            PatternMatchingResult result = _matcher.Execute(_directoryInfo);
            return result.Files.Select(static file => file.Path).OrderBy(static path => path, StringComparer.Ordinal);
#endif
        }

#if USE_TOUKI_GLOBBING
        private static string RebaseLeadingParentSegments(
            string root,
            string pattern,
            out string enumerationRoot,
            out string relativePathPrefix)
        {
            int start = 0;
            while (start < pattern.Length && IsSeparator(pattern[start]))
            {
                start++;
            }

            int end = pattern.Length;
            while (end > start && IsSeparator(pattern[end - 1]))
            {
                end--;
            }

            string normalizedPattern = end < pattern.Length
                ? string.Concat(pattern.AsSpan(start, end - start), "/**")
                : pattern.Substring(start);

            int parentCount = 0;
            int offset = 0;
            while (offset + 2 <= normalizedPattern.Length
                && normalizedPattern[offset] == '.'
                && normalizedPattern[offset + 1] == '.'
                && (offset + 2 == normalizedPattern.Length || IsSeparator(normalizedPattern[offset + 2])))
            {
                parentCount++;
                offset += 2;
                if (offset < normalizedPattern.Length)
                {
                    offset++;
                }
            }

            int emptySegmentCount = 0;
            while (offset < normalizedPattern.Length && IsSeparator(normalizedPattern[offset]))
            {
                emptySegmentCount++;
                offset++;
            }

            enumerationRoot = root;
            for (int index = 0; index < parentCount; index++)
            {
                enumerationRoot = Path.GetFullPath(Path.Combine(enumerationRoot, ".."));
            }

            relativePathPrefix = string.Create(checked(parentCount * 3), parentCount, static (destination, count) =>
            {
                for (int index = 0; index < count; index++)
                {
                    "../".AsSpan().CopyTo(destination[(index * 3)..]);
                }
            });

            string remainingPattern = normalizedPattern.Substring(offset);
            if (emptySegmentCount == 0)
            {
                return remainingPattern;
            }

            return string.Create(
                checked(remainingPattern.Length + (emptySegmentCount * 2)),
                (emptySegmentCount, remainingPattern),
                static (destination, state) =>
                {
                    int position = 0;
                    for (int index = 0; index < state.emptySegmentCount; index++)
                    {
                        destination[position++] = '*';
                        destination[position++] = '/';
                    }

                    state.remainingPattern.AsSpan().CopyTo(destination[position..]);
                });

            static bool IsSeparator(char value) => value is '/' or '\\';
        }
#endif

        /// <summary>
        /// Gets the last write time of the file at the specified <paramref name="path"/>.
        /// </summary>
        /// <param name="path">The root relative path.</param>
        /// <returns>The <see cref="DateTime"/> that the file was last modified.</returns>
        protected virtual DateTime GetLastWriteUtc(string path)
        {
#if USE_TOUKI_GLOBBING
            string filePath = Path.Combine(_root, path);
#else
            string filePath = Path.Combine(_directoryInfo.FullName, path);
#endif
            return FileSystemInfoHelper.GetFileLinkTargetLastWriteTimeUtc(filePath) ?? File.GetLastWriteTimeUtc(filePath);
        }

#if NET
        private static void ComputeHash(IncrementalHash sha256, string path, DateTime lastChangedUtc)
        {
            sha256.AppendData(MemoryMarshal.AsBytes(path.AsSpan()));
            sha256.AppendData(MemoryMarshal.AsBytes([lastChangedUtc]));
        }
#else
        private void ComputeHash(IncrementalHash sha256, string path, DateTime lastChangedUtc)
        {
            int byteCount = path.Length * 2;
            if (_byteBuffer == null || byteCount > _byteBuffer.Length)
            {
                _byteBuffer = new byte[Math.Max(byteCount, 256)];
            }

            MemoryMarshal.AsBytes(path.AsSpan()).CopyTo(_byteBuffer.AsSpan());
            sha256.AppendData(_byteBuffer, 0, byteCount);

            BinaryPrimitives.WriteInt64LittleEndian(_byteBuffer, lastChangedUtc.Ticks);
            sha256.AppendData(_byteBuffer, 0, sizeof(long));
        }
#endif

        IDisposable IChangeToken.RegisterChangeCallback(Action<object?> callback, object? state)
        {
            if (!ActiveChangeCallbacks)
            {
                return EmptyDisposable.Instance;
            }

            return _changeToken!.RegisterChangeCallback(callback, state);
        }
    }
}
