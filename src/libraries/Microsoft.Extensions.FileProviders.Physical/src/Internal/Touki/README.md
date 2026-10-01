# Touki globbing source

The sources in this directory are carried from
[JeremyKuhne/touki](https://github.com/JeremyKuhne/touki) commit
`9d925032a5e7d100c9380a7fe40b9ef64527bcab` ("Simplify file-system matcher
APIs"). They provide the compiled glob matcher and physical file-system
enumerator used by `Microsoft.Extensions.FileProviders.Physical` on .NET 8 and
later.

The source is MIT licensed. Touki's `NOTICE` grants .NET Foundation projects
permission to record attribution in commit and pull request text rather than in
redistributed binaries.

## Runtime adaptations

- Top-level Touki types are internal to the Physical assembly.
- Runtime's shared `System.Text.ValueStringBuilder` replaces Touki's broader
  formatting implementation.
- Only the static MSBuild filename normalization needed by the shared glob
  compiler is retained; its compile-time source-position list uses `List<int>`.
- `StringSegment.Replace` uses `string.Create` instead of Touki's internal string
  allocation helper.
- Directory-prefix pruning is conservative for surrogate code units so full
  ordinal-ignore-case matching decides supplementary Unicode case pairs.
- Literal runs longer than `char.MaxValue` are emitted as adjacent opcodes so
  the FSG-compatible compiler remains unbounded when `maxPatternLength` is `-1`.
- XML documentation warnings are scoped off only where the carried internal
  subset omits related Touki types or parameter documentation.

When refreshing these files, preserve the runtime adaptations and rerun the
direct and physical-enumeration parity tests against
`Microsoft.Extensions.FileSystemGlobbing`.
