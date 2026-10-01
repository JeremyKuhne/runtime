// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileProviders.Physical.Internal;
#if NET
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Touki.Io;
using Touki.Io.Globbing;
#endif
using Xunit;

namespace Microsoft.Extensions.FileProviders.Physical
{
    public class PollingWildCardChangeTokenTest : FileCleanupTestBase
    {
        [Fact]
        public void HasChanged_ReturnsFalseIfNoFilesExist()
        {
            // Arrange
            using var directory = new TempDirectory(GetTestFilePath());
            var clock = new TestClock();
            var token = new PollingWildCardChangeToken(directory.Path, "**/*.txt", clock);

            // Act
            clock.Increment();
            var result = token.HasChanged;

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Constructor_ThrowsForNullPattern()
        {
            using var directory = new TempDirectory(GetTestFilePath());

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new PollingWildCardChangeToken(directory.Path, null!));

            Assert.Equal("pattern", exception.ParamName);
        }

        [Fact]
        public void Constructor_ThrowsForParentSegmentAfterNormalSegment()
        {
            using var directory = new TempDirectory(GetTestFilePath());

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new PollingWildCardChangeToken(directory.Path, "**/../b"));

            Assert.Equal("\"..\" can be only added at the beginning of the pattern.", exception.Message);
        }

        [Fact]
        public void Constructor_AcceptsPatternWithLongLiteralBody()
        {
            using var directory = new TempDirectory(GetTestFilePath());
            string pattern = new string('a', char.MaxValue + 1) + "/*";

            var token = new PollingWildCardChangeToken(directory.Path, pattern);

            Assert.False(token.HasChanged);
        }

        [Fact]
        public void HasChanged_ReturnsFalseIfFilesDoNotChange()
        {
            // Arrange
            using var directory = new TempDirectory(GetTestFilePath());
            CreateFile(directory.Path, "1.txt");
            var clock = new TestClock();
            var token = new TestablePollingWildCardChangeToken(directory.Path, "**/*.txt", clock);

            // Act
            clock.Increment();
            var result = token.HasChanged;

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void HasChanged_ReturnsTrueIfNewFilesWereAdded()
        {
            // Arrange
            using var directory = new TempDirectory(GetTestFilePath());
            var filePath1 = "1.txt";
            var filePath2 = "2.txt";
            CreateFile(directory.Path, filePath1);
            var clock = new TestClock();
            var token = new TestablePollingWildCardChangeToken(directory.Path, "**/*.txt", clock);

            // Act - 1
            clock.Increment();
            var result1 = token.HasChanged;

            // Assert - 1
            Assert.False(result1);

            // Act - 2
            CreateFile(directory.Path, filePath2);
            clock.Increment();
            var result2 = token.HasChanged;

            // Assert - 2
            Assert.True(result2);
        }

        [Fact]
        public void HasChanged_ReturnsTrueIfFilesWereRemoved()
        {
            // Arrange
            using var directory = new TempDirectory(GetTestFilePath());
            var filePath1 = "1.txt";
            var filePath2 = "2.txt";
            CreateFile(directory.Path, filePath1);
            CreateFile(directory.Path, filePath2);
            var clock = new TestClock();
            var token = new TestablePollingWildCardChangeToken(directory.Path, "**/*.txt", clock);

            // Act - 1
            clock.Increment();
            var result1 = token.HasChanged;

            // Assert - 1
            Assert.False(result1);

            // Act - 2
            File.Delete(Path.Combine(directory.Path, filePath2));
            clock.Increment();
            var result2 = token.HasChanged;

            // Assert - 2
            Assert.True(result2);
        }

        [Fact]
        public void HasChanged_ReturnsTrueIfFilesWereModified()
        {
            // Arrange
            using var directory = new TempDirectory(GetTestFilePath());
            var filePath1 = "1.txt";
            var filePath2 = "2.txt";
            CreateFile(directory.Path, filePath1);
            CreateFile(directory.Path, filePath2);
            var clock = new TestClock();
            var token = new TestablePollingWildCardChangeToken(directory.Path, "**/*.txt", clock);

            // Act - 1
            clock.Increment();
            var result1 = token.HasChanged;

            // Assert - 1
            Assert.False(result1);

            // Act - 2
            token.FileTimestampLookup[filePath2] = clock.UtcNow.AddMilliseconds(1);
            clock.Increment();
            var result2 = token.HasChanged;

            // Assert - 2
            Assert.True(result2);
        }

        [Fact]
        public void HasChanged_ReturnsTrueIfFileWasModifiedButRetainedAnOlderTimestamp()
        {
            // Arrange
            using var directory = new TempDirectory(GetTestFilePath());
            var filePath1 = "1.txt";
            var filePath2 = "2.txt";
            CreateFile(directory.Path, filePath1);
            CreateFile(directory.Path, filePath2);
            var clock = new TestClock();
            var token = new TestablePollingWildCardChangeToken(directory.Path, "**/*.txt", clock);

            // Act - 1
            clock.Increment();
            var result1 = token.HasChanged;

            // Assert - 1
            Assert.False(result1);

            // Act - 2
            token.FileTimestampLookup[filePath2] = clock.UtcNow.AddMilliseconds(-100);
            clock.Increment();
            var result2 = token.HasChanged;

            // Assert - 2
            Assert.True(result2);
        }

#if NET
        [Theory]
        [InlineData("*")]
        [InlineData("*.*")]
        [InlineData("*.json")]
        [InlineData("**/*")]
        [InlineData("**/*.cs")]
        [InlineData("sub/**/*.cshtml")]
        [InlineData("sub/")]
        [InlineData("**/*.JSON")]
        public void FileSystemEnumeration_AgreesWithFileSystemGlobbing(string pattern)
        {
            using var directory = new TempDirectory(GetTestFilePath());
            CreateFile(directory.Path, "README");
            CreateFile(directory.Path, "root.cs");
            CreateFile(directory.Path, "root.json");
            CreateFile(directory.Path, "sub/view.cshtml");
            CreateFile(directory.Path, "sub/nested/data.json");
            CreateFile(directory.Path, "sub/nested/source.cs");
            CreateFile(directory.Path, "other/view.cshtml");

            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);
            string[] expected = matcher
                .Execute(new DirectoryInfoWrapper(new DirectoryInfo(directory.Path)))
                .Files
                .Select(static file => file.Path)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();

            GlobSpecification specification = GlobSpecification.Compile(
                pattern,
                GlobDialect.FileSystemGlobbing,
                GlobOptions.IgnoreCase,
                GlobPathSeparator.ForwardSlash,
                maxPatternLength: -1);
            var options = new EnumerationOptions
            {
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
                MatchCasing = MatchCasing.PlatformDefault,
                MatchType = MatchType.Simple,
                RecurseSubdirectories = true
            };
            var actual = new List<string>();
            using (FileSystemPathEnumerator enumerator = FileSystemPathEnumerator.Create(
                directory.Path,
                specification.CreateFileSystemMatcher(),
                options))
            {
                while (enumerator.MoveNext())
                {
                    actual.Add(enumerator.Current);
                }
            }

            actual.Sort(StringComparer.Ordinal);
            Assert.Equal(expected, actual);
        }
#endif

        [Fact]
        public void GetLastWriteUtc_IsCalledInOrdinalPathOrder()
        {
            using var directory = new TempDirectory(GetTestFilePath());
            CreateFile(directory.Path, "z.txt");
            CreateFile(directory.Path, "sub/m.txt");
            CreateFile(directory.Path, "a.txt");

            var token = new RecordingPollingWildCardChangeToken(directory.Path, "**/*.txt", new TestClock());

            Assert.Equal(new[] { "a.txt", "sub/m.txt", "z.txt" }, token.Paths);
        }

        [Fact]
        public void LeadingParentPattern_EnumeratesOutsideRootAndPreservesRelativePath()
        {
            using var directory = new TempDirectory(GetTestFilePath());
            string root = Path.Combine(directory.Path, "root");
            string sibling = Path.Combine(directory.Path, "sibling");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(sibling);

            var clock = new TestClock();
            var token = new RecordingPollingWildCardChangeToken(root, "../sibling/*.txt", clock);
            Assert.Empty(token.Paths);

            CreateFile(sibling, "outside.txt");
            clock.Increment();

            Assert.True(token.HasChanged);
            Assert.Equal(new[] { "../sibling/outside.txt" }, token.Paths);
        }

        [Fact]
        public void MultipleLeadingParentSegments_EnumerateFromRebasedRoot()
        {
            using var directory = new TempDirectory(GetTestFilePath());
            string root = Path.Combine(directory.Path, "src", "project");
            Directory.CreateDirectory(root);
            CreateFile(directory.Path, "lib/library.cs");

            var token = new RecordingPollingWildCardChangeToken(
                root,
                "../../lib/**/*.cs",
                new TestClock());

            Assert.Equal(new[] { "../../lib/library.cs" }, token.Paths);
        }

        [Fact]
        public void ExtraSeparatorAfterLeadingParent_RequiresWildcardDirectory()
        {
            using var directory = new TempDirectory(GetTestFilePath());
            string root = Path.Combine(directory.Path, "root");
            Directory.CreateDirectory(root);
            CreateFile(directory.Path, "sibling/direct.txt");
            CreateFile(directory.Path, "one/sibling/nested.txt");

            var token = new RecordingPollingWildCardChangeToken(
                root,
                "..//sibling/*.txt",
                new TestClock());

            Assert.Equal(new[] { "../one/sibling/nested.txt" }, token.Paths);
        }

#if NET
        [Fact]
        public void SupplementaryUnicodeCasePair_DoesNotPruneMatchingDirectory()
        {
            using var directory = new TempDirectory(GetTestFilePath());
            const string Upper = "\U00010400";
            const string Lower = "\U00010428";
            CreateFile(directory.Path, $"{Lower}/probe.txt");

            var token = new RecordingPollingWildCardChangeToken(
                directory.Path,
                $"{Upper}/**/*.txt",
                new TestClock());

            Assert.Equal(new[] { $"{Lower}/probe.txt" }, token.Paths);
        }
#endif

        private static void CreateFile(string root, string filePath)
        {
            string fullPath = Path.Combine(root, filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, string.Empty);
        }

        private sealed class RecordingPollingWildCardChangeToken : PollingWildCardChangeToken
        {
            public RecordingPollingWildCardChangeToken(string root, string pattern, IClock clock)
                : base(root, pattern, clock)
            {
            }

            public List<string> Paths { get; } = [];

            protected override DateTime GetLastWriteUtc(string path)
            {
                Paths.Add(path);
                return DateTime.MinValue;
            }
        }

        private class TestablePollingWildCardChangeToken : PollingWildCardChangeToken
        {
            public TestablePollingWildCardChangeToken(
                string root,
                string pattern,
                IClock clock)
                : base(root, pattern, clock)
            {
            }

            public Dictionary<string, DateTime> FileTimestampLookup { get; } =
                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            protected override DateTime GetLastWriteUtc(string path)
            {
                DateTime value;
                if (!FileTimestampLookup.TryGetValue(path, out value))
                {
                    value = DateTime.MinValue;
                }

                return value;
            }
        }
    }
}
