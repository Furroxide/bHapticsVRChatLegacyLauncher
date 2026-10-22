# WebSocketDotNet transport patch

Source: https://github.com/SamboyCoding/WebSocketDotNet
Upstream version: 0.5.5
Upstream commit: f213d462f89991ee1dd8718d272f2baf3fecaab2
Copyright and license: Sam Byass (Samboy063), MIT; see LICENSE.

The C# sources are copied from that commit. The only parser change is in
Protocol/WebSocketFragment.cs: synchronous and asynchronous frame reads now
accumulate short stream reads until the requested field is complete, and
report end-of-stream when completion is impossible. This covers headers,
extended lengths, masks and payloads without changing the public API.

The local project builds the upstream net45 and netstandard2.1 targets, keeps
the assembly name/version, and embeds this notice and the full MIT license so
they remain available in the merged bHapticsOSC.exe. Other source files are
unmodified. The net45 build is the replacement used by bHapticsLib 1.0.5;
netstandard2.1 is used by the regression tests.
