# PitMedic dependency inventory

This inventory records the packages resolved by the v0.4.4.0 .NET 10 project. It is a release-preparation aid and does not replace the license text or notices supplied by each project.

## Direct packages

| Package | Version | Purpose |
| --- | ---: | --- |
| LibreHardwareMonitorLib | 0.9.6 | CPU, GPU, memory, fan, clock, power, and related hardware sensor access. |
| System.Diagnostics.EventLog | 10.0.11 | Reads relevant Windows event evidence. |

## Resolved transitive packages

| Package | Version |
| --- | ---: |
| BlackSharp.Core | 1.0.7 |
| DiskInfoToolkit | 1.1.2 |
| HidSharp | 2.6.4 |
| Mono.Posix.NETStandard | 1.0.0 |
| RAMSPDToolkit-NDD | 1.4.2 |
| System.CodeDom | 10.0.2 |
| System.IO.FileSystem.AccessControl | 5.0.0 |
| System.IO.Ports | 10.0.3 |
| System.Management | 10.0.2 |

`System.IO.Ports` also resolves platform-specific runtime support packages. Only the Windows x64 assets are used by the PitMedic release target.

## External prerequisite

LibreHardwareMonitor may use PawnIO for supported low-level sensors. PitMedic does not compile PawnIO into its application assembly; the Windows prerequisite flow treats it as a separately installed component.

## Public-release requirement

Before the first public download, archive the exact license text and source/project link for every package above, verify the final published dependency graph, and include all notices and source-offer information required by those licenses. Re-run this review whenever any package version changes.
